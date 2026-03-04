using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    public class UserConfirmationService : IDisposable
    {
        private readonly INotificationService _notificationService;
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;
        private readonly string _confirmationStatusFile;
        private readonly string _appDataDir;
        private readonly SemaphoreSlim _confirmationSemaphore;
        private FileSystemWatcher? _fileWatcher;
        private Timer? _reminderTimer;
        private DateTime _lastReminderSent = DateTime.MinValue;
        
        public event EventHandler<UserConfirmationEventArgs>? ConfirmationRequested;
        public event EventHandler<UserConfirmationEventArgs>? ConfirmationReceived;
        public event EventHandler? ConfirmationLost;

        public UserConfirmationService(INotificationService notificationService, ILoggingService loggingService, ISettingsService settingsService)
        {
            _notificationService = notificationService;
            _loggingService = loggingService;
            _settingsService = settingsService;
            _confirmationSemaphore = new SemaphoreSlim(1, 1);
            
            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(_appDataDir);
            _confirmationStatusFile = Path.Combine(_appDataDir, "daily_confirmation.json");
            
            StartFileWatcher();
            StartReminderSystem();
        }

        public async Task<bool> RequestDailyConfirmationAsync(int timeoutMinutes = 10)
        {
            await _confirmationSemaphore.WaitAsync();
            try
            {
                var today = DateTime.Now.Date;
                
                // Check if already confirmed today
                if (await IsConfirmedForDateAsync(today))
                {
                    _loggingService.LogInfo("Daily confirmation already received for today");
                    return true;
                }

                _loggingService.LogInfo($"Requesting daily confirmation with {timeoutMinutes} minute timeout");
                
                // Create confirmation request
                var confirmationId = Guid.NewGuid().ToString();
                var confirmationRequest = new UserConfirmationRequest
                {
                    Id = confirmationId,
                    Date = today,
                    RequestedAt = DateTime.Now,
                    TimeoutMinutes = timeoutMinutes,
                    IsConfirmed = false
                };

                // Notify listeners about confirmation request
                ConfirmationRequested?.Invoke(this, new UserConfirmationEventArgs(confirmationRequest));

                // Show notification to user
                await _notificationService.ShowNotificationAsync(
                    "Bekreft tilstedeværelse",
                    $"Trykk 'Ja, jeg er her' for å starte automatisering i dag. Timeout om {timeoutMinutes} minutter.",
                    NotificationLevel.Warning,
                    isHighPriority: true
                );

                // Wait for confirmation with timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
                
                try
                {
                    var confirmed = await WaitForConfirmationAsync(confirmationId, timeoutCts.Token);
                    
                    if (confirmed)
                    {
                        await SaveConfirmationAsync(today);
                        _loggingService.LogSuccess("Daily confirmation received - automation can proceed");
                        
                        await _notificationService.ShowNotificationAsync(
                            "Tilstedeværelse bekreftet",
                            "Automatisering starter nå for i dag",
                            NotificationLevel.Success
                        );
                        
                        return true;
                    }
                    else
                    {
                        _loggingService.LogWarning("Daily confirmation timed out - automation will not start");
                        
                        await _notificationService.ShowNotificationAsync(
                            "Bekreftelse utløpt",
                            "Automatisering starter ikke uten bekreftelse. Du kan starte manuelt senere.",
                            NotificationLevel.Warning
                        );
                        
                        return false;
                    }
                }
                catch (OperationCanceledException)
                {
                    _loggingService.LogWarning("Daily confirmation request was cancelled");
                    return false;
                }
            }
            finally
            {
                _confirmationSemaphore.Release();
            }
        }

        public async Task<bool> ConfirmPresenceAsync()
        {
            var today = DateTime.Now.Date;
            await SaveConfirmationAsync(today);
            
            _loggingService.LogSuccess("User confirmed presence for today");
            
            // Reset reminder timer since confirmation is received
            _lastReminderSent = DateTime.MinValue;
            
            // Notify listeners
            var confirmationRequest = new UserConfirmationRequest
            {
                Id = Guid.NewGuid().ToString(),
                Date = today,
                RequestedAt = DateTime.Now,
                IsConfirmed = true
            };
            
            ConfirmationReceived?.Invoke(this, new UserConfirmationEventArgs(confirmationRequest));
            
            return true;
        }
        /// <summary>
        /// Registers a Feide login as automatic presence confirmation for today
        /// </summary>
        public async Task RegisterFeideLoginConfirmationAsync()
        {
            try
            {
                var today = DateTime.Now.Date;
                var confirmationData = new DailyConfirmationData
                {
                    Date = today,
                    IsConfirmed = true,
                    ConfirmedAt = DateTime.Now,
                    ConfirmedViaFeide = true,
                    FeideLoginTime = DateTime.Now
                };

                var json = JsonSerializer.Serialize(confirmationData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_confirmationStatusFile, json);

                _loggingService.LogInfo("Presence automatically confirmed via Feide login");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error saving Feide confirmation: {ex.Message}");
            }
        }

        public async Task<bool> IsConfirmedForDateAsync(DateTime date)
        {
            try
            {
                if (!File.Exists(_confirmationStatusFile))
                {
                    _loggingService.LogDebug("No confirmation file");
                    return false;
                }

                var json = await File.ReadAllTextAsync(_confirmationStatusFile);
                var confirmationData = JsonSerializer.Deserialize<DailyConfirmationData>(json);

                if (confirmationData?.Date.Date != date.Date)
                {
                    _loggingService.LogDebug("Confirmation date mismatch");
                    return false;
                }

                // If confirmed via Feide, check if we're still within the grace period
                if (confirmationData.ConfirmedViaFeide && confirmationData.FeideLoginTime.HasValue)
                {
                    var timeSinceFeideLogin = DateTime.Now - confirmationData.FeideLoginTime.Value;
                    var gracePeriodHours = _settingsService.FeideGracePeriodHours; // Use configurable grace period

                    if (timeSinceFeideLogin.TotalHours <= gracePeriodHours)
                    {
                        _loggingService.LogDebug($"Within Feide grace period ({timeSinceFeideLogin.TotalHours:F1} hours since login, grace period: {gracePeriodHours}h)");
                        return true;
                    }
                    else
                    {
                        _loggingService.LogDebug($"Feide grace period expired ({timeSinceFeideLogin.TotalHours:F1} hours since login, grace period: {gracePeriodHours}h)");
                        return false;
                    }
                }

                var isConfirmed = confirmationData.IsConfirmed;
                _loggingService.LogDebug($"Regular confirmation: {isConfirmed}");

                return isConfirmed;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Confirmation check error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForConfirmationAsync(string confirmationId, CancellationToken cancellationToken)
        {
            // Poll for confirmation every 2 seconds
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await IsConfirmedForDateAsync(DateTime.Now.Date))
                {
                    return true;
                }
                
                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            
            return false;
        }

        private async Task SaveConfirmationAsync(DateTime date)
        {
            try
            {
                var confirmationData = new DailyConfirmationData
                {
                    Date = date,
                    IsConfirmed = true,
                    ConfirmedAt = DateTime.Now
                };

                var json = JsonSerializer.Serialize(confirmationData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_confirmationStatusFile, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error saving confirmation: {ex.Message}");
            }
        }

        public async Task ClearConfirmationAsync()
        {
            try
            {
                if (File.Exists(_confirmationStatusFile))
                {
                    await Task.Run(() => File.Delete(_confirmationStatusFile));
                    _loggingService.LogInfo("Daily confirmation cleared");
                    
                    // Reset reminder timer for new day
                    _lastReminderSent = DateTime.MinValue;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error clearing confirmation: {ex.Message}");
            }
        }

        private void StartFileWatcher()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(_appDataDir, "daily_confirmation.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Deleted += OnConfirmationFileDeleted;
                _fileWatcher.Changed += OnConfirmationFileChanged;
                
                _loggingService.LogDebug("File watcher started for confirmation file");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to start file watcher: {ex.Message}");
            }
        }

        private async void OnConfirmationFileDeleted(object sender, FileSystemEventArgs e)
        {
            _loggingService.LogWarning("Confirmation file was deleted - triggering confirmation lost event");
            ConfirmationLost?.Invoke(this, EventArgs.Empty);
        }

        private async void OnConfirmationFileChanged(object sender, FileSystemEventArgs e)
        {
            // File was modified - could be a new confirmation or cleared
            _loggingService.LogDebug("Confirmation file changed");
        }

        public void Dispose()
        {
            _fileWatcher?.Dispose();
            _reminderTimer?.Dispose();
        }

        public void StartReminderSystem()
        {
            if (_reminderTimer != null)
                return;

            _reminderTimer = new Timer(
                async _ => await CheckAndSendRemindersAsync(),
                null,
                TimeSpan.FromMinutes(1), // Check every minute
                TimeSpan.FromMinutes(1)
            );
            
            _loggingService.LogDebug("Confirmation reminder system started");
        }

        public void StopReminderSystem()
        {
            _reminderTimer?.Dispose();
            _reminderTimer = null;
            _loggingService.LogDebug("Confirmation reminder system stopped");
        }

        private async Task CheckAndSendRemindersAsync()
        {
            try
            {
                var today = DateTime.Now.Date;
                
                // Don't send reminders if already confirmed
                if (await IsConfirmedForDateAsync(today))
                    return;

                // Check if we're within school hours or approaching them
                var shouldStart = await ShouldSendReminderAsync();
                if (!shouldStart)
                    return;

                // Check if enough time has passed since last reminder (5 minutes)
                var timeSinceLastReminder = DateTime.Now - _lastReminderSent;
                if (timeSinceLastReminder.TotalMinutes < 5)
                    return;

                // Only send reminder if automation would actually start (i.e., not already completed, not manually stopped)
                if (!await ShouldAutomationStartAsync())
                    return;

                // Send reminder
                await SendConfirmationReminderAsync();
                _lastReminderSent = DateTime.Now;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error in reminder check: {ex.Message}");
            }
        }

        private async Task<bool> ShouldAutomationStartAsync()
        {
            try
            {
                // Use SchoolTimeChecker to see if automation should actually start
                var (shouldStart, _, _, _, _) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: true);
                return shouldStart;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking if automation should start: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ShouldSendReminderAsync()
        {
            try
            {
                // Check if we're within school hours or close to them (within 15 minutes)
                var now = DateTime.Now;
                var schoolHours = await GetSchoolHoursForTodayAsync();
                
                if (schoolHours == null)
                    return false;

                var (startTime, endTime) = schoolHours.Value;
                var todayStart = now.Date.Add(startTime);
                var todayEnd = now.Date.Add(endTime);
                
                // Send reminders if:
                // 1. We're within 15 minutes of school start time
                // 2. We're currently within school hours
                var withinReminderWindow = now >= todayStart.AddMinutes(-15) && now <= todayEnd;
                
                return withinReminderWindow;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking reminder conditions: {ex.Message}");
                return false;
            }
        }

        private async Task<(TimeSpan start, TimeSpan end)?> GetSchoolHoursForTodayAsync()
        {
            try
            {
                var settingsService = Services.DependencyInjection.ServiceContainer.GetOptionalService<ISettingsService>();
                if (settingsService == null)
                    return null;

                await settingsService.LoadSettingsAsync();
                var schoolHours = settingsService.SchoolHours;
                
                var today = DateTime.Now.DayOfWeek;
                if (!schoolHours.IsDayEnabled(today))
                    return null;

                return schoolHours.GetDayTimes(today);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting school hours: {ex.Message}");
                return null;
            }
        }

        private async Task SendConfirmationReminderAsync()
        {
            var now = DateTime.Now;
            var schoolHours = await GetSchoolHoursForTodayAsync();
            
            string message;
            if (schoolHours.HasValue)
            {
                var todayStart = now.Date.Add(schoolHours.Value.start);
                var minutesUntilStart = (todayStart - now).TotalMinutes;
                
                if (minutesUntilStart > 0 && minutesUntilStart <= 15)
                {
                    message = $"STU starter om {Math.Round(minutesUntilStart)} minutter. Bekreft tilstedeværelse nå.";
                }
                else
                {
                    message = "STU-tid har startet. Bekreft tilstedeværelse for å starte automatisering.";
                }
            }
            else
            {
                message = "Bekreft tilstedeværelse for å starte automatisering.";
            }

            await _notificationService.ShowNotificationAsync(
                "🔔 Påminnelse: Bekreft tilstedeværelse",
                message,
                NotificationLevel.Warning,
                isHighPriority: true
            );
            
            _loggingService.LogInfo("Sent confirmation reminder notification");
        }
    }

    public class UserConfirmationRequest
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public DateTime RequestedAt { get; set; }
        public int TimeoutMinutes { get; set; }
        public bool IsConfirmed { get; set; }
    }

    public class UserConfirmationEventArgs : EventArgs
    {
        public UserConfirmationRequest Request { get; }
        
        public UserConfirmationEventArgs(UserConfirmationRequest request)
        {
            Request = request;
        }
    }

    public class DailyConfirmationData
    {
        public DateTime Date { get; set; }
        public bool IsConfirmed { get; set; }
        public DateTime ConfirmedAt { get; set; }
        public bool ConfirmedViaFeide { get; set; }
        public DateTime? FeideLoginTime { get; set; }
    }
}