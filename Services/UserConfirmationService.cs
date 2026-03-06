using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                
                if (await IsConfirmedForDateAsync(today))
                {
                    _loggingService.LogInfo("Daily confirmation already received for today");
                    return true;
                }

                _loggingService.LogInfo($"Requesting daily confirmation with {timeoutMinutes} minute timeout");
                
                var confirmationId = Guid.NewGuid().ToString();
                var confirmationRequest = new UserConfirmationRequest
                {
                    Id = confirmationId,
                    Date = today,
                    RequestedAt = DateTime.Now,
                    TimeoutMinutes = timeoutMinutes,
                    IsConfirmed = false
                };

                ConfirmationRequested?.Invoke(this, new UserConfirmationEventArgs(confirmationRequest));

                await _notificationService.ShowNotificationAsync(
                    "Bekreft tilstedeværelse",
                    $"Trykk 'Ja, jeg er her' for å starte automatisering i dag. Timeout om {timeoutMinutes} minutter.",
                    NotificationLevel.Warning,
                    isHighPriority: true
                );

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
                
                try
                {
                    var confirmed = await WaitForConfirmationAsync(confirmationId, timeoutCts.Token);
                    
                    if (confirmed)
                    {
                        await SaveConfirmationAsync(today);
                        _loggingService.LogSuccess("Daily confirmation received - automation can proceed");
                        
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

        private bool _interactiveFeideLoginCompleted = false;
        private DateTime _interactiveFeideLoginTime = DateTime.MinValue;

        /// <summary>
        /// Marks that an interactive Feide login just completed
        /// This will be used when the user manually confirms presence
        /// </summary>
        public void MarkInteractiveFeideLoginCompleted()
                {
                    // DISABLED: No longer auto-confirm based on Feide login
                    // Always require manual confirmation regardless of Feide login
                    _loggingService.LogDebug("Feide login completed - manual confirmation still required");

                    // Clear any existing flags to prevent auto-confirmation
                    _interactiveFeideLoginCompleted = false;
                    _interactiveFeideLoginTime = DateTime.MinValue;
                }

        /// <summary>
        /// Confirms presence for today, with special handling for interactive Feide logins
        /// </summary>
        /// <summary>
        /// Confirms presence for today, with special handling for interactive Feide logins
        /// </summary>
        public async Task<bool> ConfirmPresenceAsync()
                {
                    try
                    {
                        var today = DateTime.Now.Date;

                        // NEVER auto-confirm - always require manual confirmation
                        _loggingService.LogInfo("Manual presence confirmation required");

                        // Clear any Feide login flags to prevent auto-confirmation
                        _interactiveFeideLoginCompleted = false;

                        // Always create manual confirmation
                        var manualConfirmationData = new DailyConfirmationData
                        {
                            Date = today,
                            IsConfirmed = true,
                            ConfirmedAt = DateTime.Now,
                            ConfirmedViaFeide = false,
                            FeideLoginTime = null
                        };

                        var manualJson = JsonSerializer.Serialize(manualConfirmationData, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(_confirmationStatusFile, manualJson);

                        _loggingService.LogInfo("Presence confirmed manually");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Error confirming presence: {ex.Message}");
                        return false;
                    }
                }
        /// <summary>
        /// Registers a Feide login as automatic presence confirmation for today
        /// </summary>
        /// <summary>
        /// Registers an interactive Feide login (through Feide window) as automatic presence confirmation for today
        /// This is different from automatic authentication using cached credentials
        /// </summary>
        /// <summary>
        /// DEPRECATED: This method is kept for compatibility but is now disabled
        /// Auto-confirmation now happens through the manual confirmation flow
        /// </summary>
        public async Task RegisterInteractiveFeideLoginConfirmationAsync()
        {
            _loggingService.LogDebug("RegisterInteractiveFeideLoginConfirmationAsync called but auto-confirmation now happens through manual confirmation flow");
            await Task.CompletedTask;
        }


        /// <summary>
        /// Registers a Feide login as automatic presence confirmation for today
        /// DEPRECATED: This method is kept for compatibility but should not be used for automatic authentication
        /// </summary>
        public async Task RegisterFeideLoginConfirmationAsync()
        {
            // This method is now essentially disabled - we only want interactive logins to auto-confirm
            _loggingService.LogDebug("RegisterFeideLoginConfirmationAsync called but auto-confirmation disabled for automatic authentication");
            await Task.CompletedTask;
        }
        /// <summary>
        /// Determines if Feide login should trigger auto-confirmation based on timing and context
        /// </summary>
        /// <summary>
        /// Determines if Feide login should trigger auto-confirmation based on timing and context
        /// </summary>
        /// <summary>
        /// Determines if Feide login should trigger auto-confirmation based on timing and context
        /// </summary>
        /// <summary>
        /// Determines if Feide login should trigger auto-confirmation based on timing and context
        /// </summary>
        /// <summary>
        /// Determines if interactive Feide login should trigger auto-confirmation based on timing and context
        /// Much more restrictive than automatic authentication - only during actual STU hours
        /// </summary>
        private async Task<bool> ShouldAutoConfirmOnInteractiveFeideLoginAsync(DateTime loginTime)
        {
            try
            {
                _loggingService.LogDebug($"[INTERACTIVE-AUTO-CONFIRM] Checking if interactive Feide login at {loginTime:HH:mm} should trigger auto-confirmation");

                // Check if auto-confirmation is disabled in settings
                await _settingsService.LoadSettingsAsync();
                if (!_settingsService.EnableFeideAutoConfirmation)
                {
                    _loggingService.LogDebug("[INTERACTIVE-AUTO-CONFIRM] Auto-confirmation disabled in settings");
                    return false;
                }

                if (_settingsService.FeideGracePeriodHours <= 0)
                {
                    _loggingService.LogDebug("[INTERACTIVE-AUTO-CONFIRM] Auto-confirmation disabled (grace period = 0)");
                    return false;
                }

                // Get today's STU times from the attendance service
                var stuTimes = await GetTodaysSTUTimesAsync();
                if (stuTimes == null || !stuTimes.Any())
                {
                    _loggingService.LogDebug("[INTERACTIVE-AUTO-CONFIRM] No STU times found for today - no auto-confirmation");
                    return false; // Much more restrictive - no STU times = no auto-confirmation
                }

                var firstSTUTime = stuTimes.Min();
                var lastSTUTime = stuTimes.Max();

                _loggingService.LogDebug($"[INTERACTIVE-AUTO-CONFIRM] STU times today: First={firstSTUTime}, Last={lastSTUTime}");

                // VERY RESTRICTIVE: Only auto-confirm if login happens during actual STU hours
                // No "early arrival" or "late arrival" - only during STU sessions
                var duringSTUHours = loginTime.TimeOfDay >= firstSTUTime && 
                                   loginTime.TimeOfDay <= lastSTUTime.Add(TimeSpan.FromMinutes(45)); // STU sessions are 45 min

                _loggingService.LogDebug($"[INTERACTIVE-AUTO-CONFIRM] Login at {loginTime:HH:mm} - During STU hours: {duringSTUHours}");
                _loggingService.LogDebug($"[INTERACTIVE-AUTO-CONFIRM] Decision: Should auto-confirm = {duringSTUHours}");

                return duringSTUHours;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[INTERACTIVE-AUTO-CONFIRM] Error checking auto-confirmation conditions: {ex.Message}");
                return false; // Don't auto-confirm on error
            }
        }

        /// <summary>
        /// Gets today's STU start times from the attendance service
        /// </summary>
        /// <summary>
        /// Gets today's STU start times from the attendance service
        /// </summary>
        /// <summary>
        /// Gets today's STU start times from the attendance service
        /// </summary>
        /// <summary>
        /// Gets today's STU start times from the attendance service
        /// </summary>
        private async Task<List<TimeSpan>?> GetTodaysSTUTimesAsync()
        {
            try
            {
                // Try to get attendance service from DI container first
                var attendanceService = Services.DependencyInjection.ServiceContainer.GetOptionalService<AttendanceDataService>();

                if (attendanceService == null)
                {
                    _loggingService.LogDebug("AttendanceDataService not available for STU time lookup - using fallback");
                    return await GetSTUTimesFromSettingsAsync(); // Fallback to settings-based times
                }

                // Get today's schedule
                var scheduleData = await attendanceService.GetTodayScheduleAsync();
                if (scheduleData?.AllTodayItems == null)
                {
                    _loggingService.LogDebug("No daily schedule available for STU time lookup - using fallback");
                    return await GetSTUTimesFromSettingsAsync(); // Fallback to settings-based times
                }

                return ExtractSTUTimesFromSchedule(scheduleData.AllTodayItems);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting today's STU times: {ex.Message}");
                return await GetSTUTimesFromSettingsAsync(); // Fallback to settings-based times
            }
        }

        /// <summary>
        /// Extracts STU times from schedule items
        /// </summary>
        private List<TimeSpan>? ExtractSTUTimesFromSchedule(List<ScheduleItem> scheduleItems)
        {
            var stuTimes = new List<TimeSpan>();

            foreach (var item in scheduleItems)
            {
                if (item.Fag?.Contains("STU", StringComparison.OrdinalIgnoreCase) == true ||
                    item.Fag?.Contains("Studietid", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (TryParseTime(item.StartKl, out var startTime))
                    {
                        stuTimes.Add(startTime);
                    }
                }
            }

            return stuTimes.Any() ? stuTimes : null;
        }

        /// <summary>
        /// Fallback method to get STU times from school hours settings
        /// </summary>
        private async Task<List<TimeSpan>?> GetSTUTimesFromSettingsAsync()
        {
            try
            {
                var schoolHours = await GetSchoolHoursForTodayAsync();
                if (schoolHours == null)
                    return null;

                var (startTime, endTime) = schoolHours.Value;

                // Return the school start time as the first STU time
                // This is a reasonable fallback when actual schedule isn't available
                return new List<TimeSpan> { startTime };
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting STU times from settings: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses time string in format "0815" to TimeSpan
        /// </summary>
        private bool TryParseTime(string? timeStr, out TimeSpan time)
        {
            time = TimeSpan.Zero;

            if (string.IsNullOrEmpty(timeStr) || timeStr.Length != 4)
                return false;

            if (int.TryParse(timeStr.Substring(0, 2), out int hours) &&
                int.TryParse(timeStr.Substring(2, 2), out int minutes))
            {
                time = new TimeSpan(hours, minutes, 0);
                return true;
            }

            return false;
        }


        public async Task<bool> IsConfirmedForDateAsync(DateTime date)
                {
                    try
                    {
                        if (!File.Exists(_confirmationStatusFile))
                        {
                            _loggingService.LogDebug("No confirmation file - user needs to confirm");
                            return false;
                        }

                        var json = await File.ReadAllTextAsync(_confirmationStatusFile);
                        var confirmationData = JsonSerializer.Deserialize<DailyConfirmationData>(json);

                        if (confirmationData?.Date.Date != date.Date)
                        {
                            _loggingService.LogDebug("Confirmation date mismatch - user needs to confirm");
                            return false;
                        }

                        // NEVER use Feide auto-confirmation - always require manual confirmation
                        // Only accept manual confirmations
                        if (confirmationData.ConfirmedViaFeide)
                        {
                            _loggingService.LogDebug("Feide auto-confirmation found but ignored - manual confirmation required");
                            return false;
                        }

                        var isConfirmed = confirmationData.IsConfirmed;
                        _loggingService.LogDebug($"Manual confirmation status: {isConfirmed}");

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
                            EnableRaisingEvents = true,
                            IncludeSubdirectories = false
                        };

                        _fileWatcher.Deleted += OnConfirmationFileDeleted;
                        _fileWatcher.Changed += OnConfirmationFileChanged;
                        _fileWatcher.Error += OnFileWatcherError;

                        _loggingService.LogDebug($"File watcher started for confirmation file in: {_appDataDir}");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Failed to start file watcher: {ex.Message}");
                    }
                }

                private void OnFileWatcherError(object sender, ErrorEventArgs e)
                {
                    _loggingService.LogError($"File watcher error: {e.GetException().Message}");

                    // Try to restart the file watcher
                    try
                    {
                        _fileWatcher?.Dispose();
                        StartFileWatcher();
                        _loggingService.LogInfo("File watcher restarted after error");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Failed to restart file watcher: {ex.Message}");
                    }
                }

        private async void OnConfirmationFileDeleted(object sender, FileSystemEventArgs e)
                {
                    _loggingService.LogWarning($"Confirmation file was deleted: {e.FullPath} - triggering confirmation lost event");

                    // Add a small delay to ensure file system operations are complete
                    await Task.Delay(100);

                    _loggingService.LogInfo("Invoking ConfirmationLost event");
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

                // Check if automation should start but needs confirmation
                var (shouldStart, reason, _, _, needsConfirmation) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: true);

                if (!needsConfirmation)
                    return; // No confirmation needed

                // Check if it's actually time to start automation (ignoring confirmation requirement)
                var (wouldStartWithoutConfirmation, _, _, _) = await SchoolTimeChecker.ShouldAutoStartAutomationAsync(silent: true);

                if (!wouldStartWithoutConfirmation)
                {
                    // Not time to start yet, but check if we should send early reminder
                    var shouldSendEarlyReminder = await ShouldSendReminderAsync();
                    if (!shouldSendEarlyReminder)
                        return;
                }

                // Check if enough time has passed since last reminder
                var timeSinceLastReminder = DateTime.Now - _lastReminderSent;
                var reminderInterval = wouldStartWithoutConfirmation ? 2 : 5; // More frequent when automation should start

                if (timeSinceLastReminder.TotalMinutes < reminderInterval)
                    return;

                // Send reminder with urgency based on timing
                var isUrgent = wouldStartWithoutConfirmation;
                await SendConfirmationReminderAsync(isUrgent);
                _lastReminderSent = DateTime.Now;

                _loggingService.LogInfo($"Sent confirmation reminder (urgent: {isUrgent}, interval: {reminderInterval} min)");
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
                        var now = DateTime.Now;

                        // Get today's STU times instead of using school hours
                        var stuTimes = await GetTodaysSTUTimesAsync();
                        if (stuTimes == null || !stuTimes.Any())
                        {
                            _loggingService.LogDebug("No STU times found - no reminders needed");
                            return false;
                        }

                        var firstSTUTime = stuTimes.Min();
                        var lastSTUTime = stuTimes.Max();

                        var todayFirstSTU = now.Date.Add(firstSTUTime);
                        var todayLastSTU = now.Date.Add(lastSTUTime).Add(TimeSpan.FromMinutes(45)); // Add session duration

                        // Send reminders if:
                        // 1. We're within 15 minutes of first STU time
                        // 2. We're currently within STU hours
                        var reminderWindow = now >= todayFirstSTU.AddMinutes(-15) && now <= todayLastSTU;

                        _loggingService.LogDebug($"Reminder check: Now={now:HH:mm}, First STU={firstSTUTime}, Last STU={lastSTUTime}, In window={reminderWindow}");

                        return reminderWindow;
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

        private async Task SendConfirmationReminderAsync(bool isUrgent = false)
        {
            try
            {
                // Get today's STU times for more specific messaging
                var stuTimes = await GetTodaysSTUTimesAsync();
                string timeInfo = "i dag";
                string title = "Bekreft Tilstedeværelse";
                var level = NotificationLevel.Info;

                if (stuTimes != null && stuTimes.Any())
                {
                    var firstSTU = stuTimes.Min();
                    var now = DateTime.Now.TimeOfDay;

                    if (now < firstSTU)
                    {
                        // Before first STU - show the actual first STU time
                        timeInfo = $"før {firstSTU:hh\\:mm} (første STU-time)";
                    }
                    else
                    {
                        // During or after STU hours
                        timeInfo = "nå";
                        if (isUrgent)
                        {
                            title = "Bekreftelse Påkrevd Nå";
                            level = NotificationLevel.Warning;
                        }
                    }
                }

                var message = isUrgent 
                    ? $"Trykk 'Ja, jeg er her' {timeInfo} for å starte automatisk registrering."
                    : $"Husk å bekrefte at du er til stede {timeInfo} for automatisk registrering av studietimer.";

                await _notificationService.ShowNotificationAsync(
                    title,
                    message,
                    level,
                    isHighPriority: isUrgent
                );

                _loggingService.LogInfo($"Sent confirmation reminder for {timeInfo} (urgent: {isUrgent})");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error sending confirmation reminder: {ex.Message}");
            }
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