using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using AkademiTrack.Common;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Services.Http;
using AkademiTrack.Services.Configuration;

namespace AkademiTrack.Services
{
    // NOTE: Other members of AutomationService are defined elsewhere in this file.
    // The following field is used to track background verification tasks to avoid
    // unobserved exceptions from fire-and-forget Task.Run usage.
    public class AutomationService : IAutomationService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly HttpClient _httpClient;
        private readonly MacOSCaffeinateService _caffeinateService;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _runningTask; // Tracks the active StartAsync task so StopAsync can await actual completion
        private TaskCompletionSource<bool>? _stoppedTcs; // Signals when StartAsync's finally block has fully completed
        private bool _isRunning;
        private string _currentStatus = "Ready";
        private UserParameters? _userParameters;
        private Dictionary<string, string>? _cookies;
        private List<ScheduleItem>? _cachedScheduleData;
        private bool _disposed = false;

        // Resource tracking for proper disposal
        private readonly List<IDisposable> _disposables = new();
        private readonly object _disposalLock = new object();
        private readonly List<Task> _backgroundVerificationTasks = new List<Task>();

        // Constants
        private const int MONITORING_INTERVAL_MS = 30_000; // 30 seconds
        private const int REGISTRATION_WINDOW_MINUTES = Constants.Time.REGISTRATION_WINDOW_MINUTES;

        public event EventHandler<AutomationStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AutomationProgressEventArgs>? ProgressUpdated;
        public event EventHandler<SessionRegisteredEventArgs>? SessionRegistered;

        public bool IsRunning => _isRunning;
        public string CurrentStatus => _currentStatus;

        /// <summary>
        /// Force stop automation immediately without waiting - for UI emergency stop
        /// </summary>
        public async Task<AutomationResult> ForceStopAsync()
        {
            _loggingService.LogWarning("Force stop requested - immediately stopping automation");
            
            try
            {
                // Immediately set to stopped
                _isRunning = false;
                _currentStatus = "Forcibly stopped";
                
                // Cancel everything
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                // Clear background tasks
                _backgroundVerificationTasks.Clear();
                
                // Stop caffeinate
                await _caffeinateService.StopCaffeinateAsync();
                
                // Notify UI immediately
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));
                
                await _notificationService.ShowNotificationAsync(
                    "Automatisering force-stoppet", 
                    "Automatisering har blitt force-stoppet", 
                    NotificationLevel.Warning
                );
                
                return AutomationResult.Successful("Automation force stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error during force stop: {ex.Message}");
                return AutomationResult.Failed($"Force stop failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get diagnostic information about automation state
        /// </summary>
        public string GetDiagnosticInfo()
        {
            return $"IsRunning: {_isRunning}, Status: {_currentStatus}, " +
                   $"CancellationToken: {(_cancellationTokenSource?.Token.IsCancellationRequested ?? true)}, " +
                   $"BackgroundTasks: {_backgroundVerificationTasks.Count}";
        }

        public AutomationService(ILoggingService loggingService, INotificationService notificationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _httpClient = HttpClientFactory.DefaultClient;
            _caffeinateService = new MacOSCaffeinateService();
            
            // Track the HttpClient for monitoring (but don't dispose it as it's shared)
            // Track the caffeinate service for disposal
            TrackDisposable(_caffeinateService);
            _loggingService.LogDebug("[AutomationService] Initialized with shared HttpClient and caffeinate service");
        }

        /// <summary>
        /// Adds a disposable resource to be tracked and disposed when service is disposed
        /// </summary>
        private void TrackDisposable(IDisposable disposable)
        {
            lock (_disposalLock)
            {
                if (!_disposed)
                {
                    _disposables.Add(disposable);
                }
                else
                {
                    // If already disposed, dispose immediately
                    try
                    {
                        disposable?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogError($"Error disposing tracked resource: {ex.Message}");
                    }
                }
            }
        }

        public async Task<AutomationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                return AutomationResult.Failed("Automation is already running");
            }

            string finalStatus = "Ready"; // Track what final status to send
            CancellationTokenSource? localCancellationSource = null;

            // Create a TCS that StopAsync can await to know the loop has truly finished
            var stoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _stoppedTcs = stoppedTcs;

            try
            {
                _isRunning = true;
                _currentStatus = "Starting automation";
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(true, _currentStatus));

                // Load credentials and parameters
                var loadResult = await LoadCredentialsAsync();
                if (!loadResult.Success)
                {
                    finalStatus = "Autentisering feilet";
                    return loadResult;
                }

                // Check internet connection
                if (!await CheckInternetConnectionAsync(cancellationToken))
                {
                    finalStatus = "Ingen internett-tilkobling";
                    return AutomationResult.Failed("No internet connection available");
                }

                // PRE-CHECK: Check for already registered STU sessions
                _loggingService.LogInfo("Sjekker allerede registrerte STU-økter...");
                
                // TEMPORARILY DISABLE PRE-CHECK - it's causing false positives
                _loggingService.LogInfo("Pre-check midlertidig deaktivert for å unngå feil telling");
                var preCheckResult = new STUPreCheckResult
                {
                    TotalSessions = 0,
                    RegisteredSessions = new List<string>(),
                    AllSessionsRegistered = false
                };
                
                /*
                var preCheckResult = await PreCheckRegisteredSTUSessionsAsync();
                if (preCheckResult.AllSessionsRegistered)
                {
                    _loggingService.LogSuccess("Alle STU-økter for i dag er allerede registrert!");
                    await _notificationService.ShowNotificationAsync(
                        "Alle STU-økter registrert",
                        $"Alle {preCheckResult.TotalSessions} STU-økter for i dag er allerede registrert. Ingen automatisering nødvendig.",
                        NotificationLevel.Success
                    );
                    await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                    finalStatus = "Alle STU-økter allerede registrert";
                    return AutomationResult.Successful("All STU sessions already registered");
                }
                else if (preCheckResult.RegisteredSessions.Count > 0)
                {
                    _loggingService.LogInfo($"Fant {preCheckResult.RegisteredSessions.Count} av {preCheckResult.TotalSessions} STU-økter allerede registrert");
                }
                */

                // Mark today as started to prevent duplicate auto-starts
                await SchoolTimeChecker.MarkTodayAsStartedAsync();

                // Create and track cancellation token source
                localCancellationSource = new CancellationTokenSource();
                TrackDisposable(localCancellationSource);
                _cancellationTokenSource = localCancellationSource;
                
                var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);
                TrackDisposable(linkedCancellationSource);
                var combinedToken = linkedCancellationSource.Token;

                _loggingService.LogInfo("Starter automatisering");
                await _notificationService.ShowNotificationAsync(
                    "Automatisering startet",
                    "STU tidsregistrering automatisering kjører nå",
                    NotificationLevel.Success
                );

                // Start caffeinate to keep macOS awake during automation
                await _caffeinateService.StartCaffeinateAsync();

                // Start the monitoring loop with pre-check results
                var loopResult = await RunMonitoringLoopAsync(combinedToken, preCheckResult);

                // Set final status based on loop result
                if (loopResult.HasValue)
                {
                    finalStatus = loopResult.Value switch
                    {
                        MonitoringLoopResult.NoSessionsFound => "Ingen STUDIE-økter funnet for i dag",
                        MonitoringLoopResult.AllSessionsConflict => "Alle STU-økter har konflikter",
                        MonitoringLoopResult.AllComplete => "Alle STU-økter er håndtert",
                        _ => "Ready"
                    };
                }

                return AutomationResult.Successful("Automation completed successfully");
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogInfo("Automatisering stoppet av bruker");
                await _notificationService.ShowNotificationAsync(
                    "Automatisering stoppet",
                    "Overvåking har blitt stoppet",
                    NotificationLevel.Info
                );
                finalStatus = "Automatisering stoppet";
                return AutomationResult.Successful("Automation stopped by user");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Automatisering feil: {ex.Message}");
                
                // Track automation error in analytics
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "automation_unexpected_error",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log automation error: {analyticsEx.Message}");
                }
                
                // Log error but don't show notification to user
                _loggingService.LogError($"Automation failed: {ex.Message}");
                
                finalStatus = "Automatisering feilet";
                return AutomationResult.Failed($"Automation failed: {ex.Message}", ex);
            }
            finally
            {
                _isRunning = false;
                _currentStatus = finalStatus; // Use the tracked final status instead of always "Ready"
                
                // Stop caffeinate when automation ends
                await _caffeinateService.StopCaffeinateAsync();
                
                // Clean up cancellation token source
                if (localCancellationSource != null)
                {
                    lock (_disposalLock)
                    {
                        _disposables.Remove(localCancellationSource);
                    }
                    localCancellationSource.Dispose();
                }
                _cancellationTokenSource = null;
                
                // Single authoritative status update — StopAsync waits for this before returning
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));
                
                // Signal StopAsync (or anyone waiting) that the loop has fully wound down
                stoppedTcs.TrySetResult(true);
                _stoppedTcs = null;
            }
        }

        private enum MonitoringLoopResult
        {
            NoSessionsFound,
            AllSessionsConflict,
            AllComplete,
            Cancelled
        }

        public async Task<AutomationResult> StopAsync()
        {
            if (!_isRunning)
            {
                _loggingService.LogInfo("Automation is already stopped");
                return AutomationResult.Successful("Automation is already stopped");
            }

            try
            {
                _loggingService.LogInfo("Stopp forespurt - stopper automatisering");

                // Capture the TCS before cancelling so we can await it
                var stoppedTcs = _stoppedTcs;

                // Cancel the monitoring loop — this is the only thing StopAsync needs to do
                _cancellationTokenSource?.Cancel();

                // Clear background verification tasks
                if (_backgroundVerificationTasks.Count > 0)
                {
                    _loggingService.LogDebug($"Clearing {_backgroundVerificationTasks.Count} background verification tasks");
                    _backgroundVerificationTasks.Clear();
                }

                // Wait for StartAsync's finally block to finish (it owns the status update and cleanup).
                // 5-second timeout as a safety net — if the loop doesn't exit cleanly we don't hang forever.
                if (stoppedTcs != null)
                {
                    var completed = await Task.WhenAny(stoppedTcs.Task, Task.Delay(5000));
                    if (completed != stoppedTcs.Task)
                    {
                        _loggingService.LogWarning("StopAsync: timed out waiting for loop to exit — forcing state");
                        _isRunning = false;
                        _currentStatus = "Automatisering stoppet";
                        StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));
                    }
                    // If it completed normally, StartAsync's finally already fired StatusChanged — nothing to do here
                }

                await _notificationService.ShowNotificationAsync(
                    "Automatisering stoppet",
                    "Automatisering har blitt stoppet av bruker",
                    NotificationLevel.Info
                );

                _loggingService.LogInfo("Automation stopped successfully");
                return AutomationResult.Successful("Automation stopped successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error during stop: {ex.Message}");

                // Ensure we're marked as stopped even if there's an error
                _isRunning = false;
                _currentStatus = "Automatisering stoppet (feil)";
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));

                return AutomationResult.Failed($"Failed to stop automation: {ex.Message}", ex);
            }
        }

        public async Task<AutomationResult> RefreshAuthenticationAsync()
        {
            try
            {
                _loggingService.LogInfo("Starting re-authentication");
                
                using (var authService = new AuthenticationService(_notificationService))
                {
                    var authResult = await authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Cookies.Count > 0)
                {
                    // Update cookies
                    _cookies = authResult.Cookies;
                    
                    // Update user parameters if available
                    if (authResult.Parameters != null && authResult.Parameters.IsComplete)
                    {
                        _userParameters = authResult.Parameters;
                        _loggingService.LogSuccess($"Authentication complete! Got {authResult.Cookies.Count} cookies and user parameters");
                    }
                    else
                    {
                        _loggingService.LogSuccess($"Authentication complete! Got {authResult.Cookies.Count} cookies");
                    }

                    return AutomationResult.Successful("Authentication refreshed successfully");
                }
                else
                {
                    _loggingService.LogError("Authentication returned no cookies or failed");
                    return AutomationResult.Failed("Authentication failed - no cookies received");
                }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Authentication exception: {ex.Message}");
                return AutomationResult.Failed($"Authentication refresh failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> CheckSchoolHoursAsync()
        {
            try
            {
                var result = await SchoolTimeChecker.ShouldStartAutomationAsync();
                return result.shouldStart;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking school hours: {ex.Message}");
                return false;
            }
        }

        private async Task<AutomationResult> LoadCredentialsAsync()
        {
            try
            {
                _loggingService.LogDebug("[AUTOMATION] Loading credentials from storage");
                
                // Load cookies
                _cookies = await SecureCredentialStorage.LoadCookiesAsync();
                _loggingService.LogDebug($"[AUTOMATION] Loaded {_cookies?.Count ?? 0} cookies from storage");
                
                // Load user parameters from storage
                await LoadUserParametersAsync();
                
                if (_cookies == null || _cookies.Count == 0)
                {
                    _loggingService.LogInfo("Ingen cookies funnet - autentiserer på nytt");
                    var authResult = await RefreshAuthenticationAsync();
                    if (!authResult.Success)
                    {
                        return AutomationResult.Failed("Failed to authenticate");
                    }
                }

                // Check if we have user parameters after loading/authentication
                if (_userParameters == null || !_userParameters.IsComplete)
                {
                    _loggingService.LogError($"[AUTOMATION] User parameters missing or incomplete after loading. Params null: {_userParameters == null}, Complete: {_userParameters?.IsComplete ?? false}");
                    return AutomationResult.Failed("Missing user parameters");
                }
                
                _loggingService.LogSuccess($"[AUTOMATION] Credentials loaded successfully: {_cookies?.Count ?? 0} cookies, user params complete: {_userParameters.IsComplete}");

                return AutomationResult.Successful();
            }
            catch (Exception ex)
            {
                return AutomationResult.Failed($"Failed to load credentials: {ex.Message}", ex);
            }
        }

        private async Task<bool> CheckInternetConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _loggingService.LogDebug("Checking internet connectivity");
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Constants.Time.SHORT_TIMEOUT_SECONDS));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                var response = await _httpClient.GetAsync("https://www.google.com", linkedCts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<MonitoringLoopResult?> RunMonitoringLoopAsync(CancellationToken cancellationToken, STUPreCheckResult? preCheckResult = null)
        {
            int cycleCount = 0;
            var lastCheckTime = DateTime.Now; // For wake-from-sleep detection
            const int WAKE_DETECTION_THRESHOLD_MINUTES = 5;

            _loggingService.LogInfo("Henter timeplandata for hele dagen");
            _cachedScheduleData = await GetFullDayScheduleDataAsync();

            if (_cachedScheduleData == null)
            {
                _loggingService.LogError("Kunne ikke hente timeplandata - cookies kan være utløpt");
                throw new InvalidOperationException("Failed to fetch schedule data");
            }

            _loggingService.LogSuccess($"Hentet {_cachedScheduleData.Count} timeplan elementer for hele dagen");

            var today = DateTime.Now.ToString("yyyyMMdd");
            var todaysStuSessions = _cachedScheduleData
                .Where(item => item.Dato == today && item.KNavn == "STU")
                .ToList();

            _loggingService.LogInfo($"Fant {todaysStuSessions.Count} STU-økter for i dag ({DateTime.Now:yyyy-MM-dd})");

            if (todaysStuSessions.Count == 0)
            {
                _loggingService.LogInfo("Ingen STUDIE-økter funnet for i dag - stopper automatisering");
                await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                
                // Track no sessions found
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "automation_no_stu_sessions_found",
                        "No STU sessions found for today"
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log no sessions: {analyticsEx.Message}");
                }
                
                await _notificationService.ShowNotificationAsync(
                    "Ingen STU-økter i dag",
                    "Ingen studietimer å registrere i dag.",
                    NotificationLevel.Info
                );
                return MonitoringLoopResult.NoSessionsFound;
            }

            var validStuSessions = FilterValidStuSessions(todaysStuSessions);

            if (validStuSessions.Count == 0)
            {
                _loggingService.LogInfo("Alle STU-økter har konflikter med andre timer - ingen å registrere");
                await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                
                // Track all sessions conflict
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "automation_all_sessions_conflict",
                        $"All {todaysStuSessions.Count} STU sessions conflict with regular classes"
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log session conflicts: {analyticsEx.Message}");
                }
                
                await _notificationService.ShowNotificationAsync(
                    "Ingen STU-økter i dag",
                    "Ingen studietimer å registrere i dag.",
                    NotificationLevel.Warning
                );
                return MonitoringLoopResult.AllSessionsConflict;
            }

            _loggingService.LogInfo($"Etter konflikt-sjekking: {validStuSessions.Count} av {todaysStuSessions.Count} STU-økter er gyldige");

            // Load previously registered sessions from disk ONLY (disable pre-check for now)
            // CLEAR any potentially corrupted cache first
            var registeredSessionsFile = GetRegisteredSessionsFilePath();
            if (File.Exists(registeredSessionsFile))
            {
                _loggingService.LogInfo("Sletter eksisterende cache for å unngå feil data");
                File.Delete(registeredSessionsFile);
            }
            
            var registeredSessions = await GetRegisteredSessionsForTodayAsync();
            var registeredSessionKeys = new HashSet<string>(registeredSessions.Keys);
            
            _loggingService.LogInfo($"Starter med tom cache - {registeredSessionKeys.Count} registrerte økter");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Explicit cancellation check at start of each iteration
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Wake-from-sleep detection
                    var currentTime = DateTime.Now;
                    var timeSinceLastCheck = currentTime - lastCheckTime;
                    if (timeSinceLastCheck.TotalMinutes > WAKE_DETECTION_THRESHOLD_MINUTES)
                    {
                        _loggingService.LogInfo($"[WAKE DETECTION] 💤 System wake detected - time gap: {timeSinceLastCheck.TotalMinutes:F1} minutes");
                        _loggingService.LogInfo("[WAKE DETECTION] Refreshing schedule data after wake from sleep");
                        
                        // Refresh schedule data after wake - registration windows may have changed
                        _cachedScheduleData = await GetFullDayScheduleDataAsync();
                        if (_cachedScheduleData == null)
                        {
                            _loggingService.LogError("Kunne ikke hente timeplandata etter oppvåkning - cookies kan være utløpt");
                            throw new InvalidOperationException("Failed to refresh schedule data after wake");
                        }
                        
                        // Recalculate valid STU sessions after wake
                        var todayAfterWake = DateTime.Now.ToString("yyyyMMdd");
                        todaysStuSessions = _cachedScheduleData
                            .Where(item => item.Dato == todayAfterWake && item.KNavn == "STU")
                            .ToList();

                        validStuSessions = FilterValidStuSessions(todaysStuSessions);
                        _loggingService.LogInfo($"[WAKE DETECTION] Recalculated: {validStuSessions.Count} valid STU sessions after wake");
                        
                        // Check if all registration windows closed while sleeping
                        bool allWindowsClosed = validStuSessions.All(session =>
                        {
                            var status = GetRegistrationWindowStatus(session);
                            return status == RegistrationWindowStatus.Closed;
                        });
                        
                        if (allWindowsClosed && validStuSessions.Count > 0)
                        {
                            _loggingService.LogInfo("[WAKE DETECTION] All STU registration windows closed while sleeping - stopping automation");
                            await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                            
                            await _notificationService.ShowNotificationAsync(
                                "Automatisering stoppet",
                                "Ingen flere studietimer å registrere.",
                                NotificationLevel.Info
                            );
                            return MonitoringLoopResult.AllComplete;
                        }
                    }
                    lastCheckTime = currentTime;
                    
                    cycleCount++;
                    var currentTimeString = currentTime.ToString("HH:mm");
                    _loggingService.LogInfo($"Syklus #{cycleCount} - Sjekker STU registreringsvinduer (kl. {currentTimeString})");

                    // Check if we're still within school hours - auto-stop if AFTER school ends
                    var (isWithin, isAfter) = await SchoolTimeChecker.GetSchoolHoursStatusAsync();
                    if (!isWithin && isAfter)
                    {
                        _loggingService.LogInfo("Utenfor skoletid - stopper automatisering automatisk");
                        await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                        await _notificationService.ShowNotificationAsync(
                            "Automatisering stoppet",
                            "Skoletiden er over.",
                            NotificationLevel.Info
                        );
                        return MonitoringLoopResult.AllComplete;
                    }

                    ProgressUpdated?.Invoke(this, new AutomationProgressEventArgs(
                        "Sjekker registreringsvinduer", cycleCount));

                    int openWindows = 0;
                    int closedWindows = 0;
                    int notYetOpenWindows = 0;
                    int unregisteredClosedWindows = 0;

                    foreach (var stuSession in validStuSessions)
                    {
                        // Check for cancellation before processing each session
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var sessionKey = $"{stuSession.StartKl}-{stuSession.SluttKl}";

                        // Check if already registered (from disk)
                        if (registeredSessionKeys.Contains(sessionKey))
                        {
                            // Double-verify this session is actually registered by checking online
                            _loggingService.LogInfo($"[DEBUG] Økt {sessionKey} er markert som registrert lokalt - verifiserer online...");
                            
                            // For now, trust local cache but add verification
                            closedWindows++;
                            continue;
                        }

                        var registrationStatus = GetRegistrationWindowStatus(stuSession);

                        switch (registrationStatus)
                        {
                            case RegistrationWindowStatus.Open:
                                openWindows++;
                                
                                // Double-check if registered (in case another instance registered it)
                                if (await IsSessionRegisteredAsync(sessionKey))
                                {
                                    _loggingService.LogInfo($"STU økt {sessionKey} er allerede registrert (oppdaget under åpen vindu sjekk)");
                                    registeredSessionKeys.Add(sessionKey);
                                    closedWindows++;
                                    openWindows--;
                                    break;
                                }
                                
                                _loggingService.LogInfo($"Registreringsvindu er ÅPENT for STU økt {stuSession.StartKl}-{stuSession.SluttKl}");

                                try
                                {
                                    // Check for cancellation before attempting registration
                                    cancellationToken.ThrowIfCancellationRequested();
                                    
                                    var registrationResult = await RegisterAttendanceAsync(stuSession, cancellationToken);
                                    if (registrationResult)
                                    {
                                        _loggingService.LogSuccess($"Registrerte oppmøte for {stuSession.StartKl}-{stuSession.SluttKl}!");
                                        await _notificationService.ShowNotificationAsync(
                                            "STU registrert",
                                            $"Studietimen {stuSession.StartKl}–{stuSession.SluttKl} er registrert.",
                                            NotificationLevel.Success
                                        );
                                        
                                        // Mark as registered on disk
                                        await MarkSessionAsRegisteredAsync(sessionKey);
                                        registeredSessionKeys.Add(sessionKey);
                                    }
                                }
                                catch (Exception regEx)
                                {
                                    _loggingService.LogError($"Registrering feilet: {regEx.Message}");
                                    
                                    // Track registration failure
                                    try
                                    {
                                        var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                                        await analyticsService.LogErrorAsync(
                                            "automation_registration_failed",
                                            $"Failed to register STU {stuSession.StartKl}-{stuSession.SluttKl}: {regEx.Message}",
                                            regEx
                                        );
                                    }
                                    catch (Exception analyticsEx)
                                    {
                                        Debug.WriteLine($"[Analytics] Failed to log registration error: {analyticsEx.Message}");
                                    }
                                    
                                    await _notificationService.ShowNotificationAsync(
                                        "Registrering feilet",
                                        $"Kunne ikke registrere {stuSession.StartKl}–{stuSession.SluttKl}. Prøver igjen.",
                                        NotificationLevel.Error
                                    );
                                }
                                break;

                            case RegistrationWindowStatus.NotYetOpen:
                                notYetOpenWindows++;
                                break;

                            case RegistrationWindowStatus.Closed:
                                // Session window is closed but not registered - this is incomplete
                                unregisteredClosedWindows++;
                                break;
                        }
                    }

                    // Check completion: All sessions are either registered OR have closed registration windows
                    bool allSessionsRegistered = registeredSessionKeys.Count == validStuSessions.Count;
                    
                    // Add detailed debugging information
                    _loggingService.LogInfo($"[DEBUG] Fullføring sjekk: registeredSessionKeys.Count={registeredSessionKeys.Count}, validStuSessions.Count={validStuSessions.Count}");
                    _loggingService.LogInfo($"[DEBUG] Registrerte økter: [{string.Join(", ", registeredSessionKeys)}]");
                    _loggingService.LogInfo($"[DEBUG] Gyldige økter: [{string.Join(", ", validStuSessions.Select(s => $"{s.StartKl}-{s.SluttKl}"))}]");
                    
                    if (allSessionsRegistered)
                    {
                        _loggingService.LogSuccess($"Alle {validStuSessions.Count} gyldige STU-økter er registrert for i dag!");
                        await SchoolTimeChecker.MarkTodayAsCompletedAsync();

                        await _notificationService.ShowNotificationAsync(
                            "Alle studietimer registrert",
                            "Alle STU-økter for i dag er registrert.",
                            NotificationLevel.Success
                        );
                        return MonitoringLoopResult.AllComplete;
                    }

                    // Check if all STU sessions have finished - stop when there's nothing more to do
                    bool hasOpenWindows = openWindows > 0;
                    bool hasFutureWindows = notYetOpenWindows > 0;
                    bool hasActionableWork = hasOpenWindows || hasFutureWindows;

                    _loggingService.LogInfo($"[DEBUG] Arbeid sjekk: hasOpenWindows={hasOpenWindows}, hasFutureWindows={hasFutureWindows}, hasActionableWork={hasActionableWork}");
                    _loggingService.LogInfo($"[DEBUG] Status oversikt: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} registrerte, {unregisteredClosedWindows} uregistrerte lukkede");

                    // Stop automation if there's no more work to do (no open windows, no future windows)
                    if (!hasActionableWork)
                    {
                        _loggingService.LogSuccess($"Ingen flere STU-økter å håndtere for i dag. Registrerte {registeredSessionKeys.Count} av {validStuSessions.Count} økter.");
                        await SchoolTimeChecker.MarkTodayAsCompletedAsync();

                        await _notificationService.ShowNotificationAsync(
                            "Automatisering fullført",
                            "Ingen flere studietimer å registrere.",
                            NotificationLevel.Info
                        );
                        return MonitoringLoopResult.AllComplete;
                    }

                    _loggingService.LogInfo($"Status: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} registrerte, {unregisteredClosedWindows} uregistrerte lukkede");

                    await Task.Delay(TimeSpan.FromSeconds(Constants.Time.RETRY_DELAY_SECONDS), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return MonitoringLoopResult.Cancelled;
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Overvåkingsfeil: {ex.Message}");

                    if (cycleCount % 10 == 0)
                    {
                        // Track monitoring error (only every 10th to avoid spam)
                        try
                        {
                            var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                            await analyticsService.LogErrorAsync(
                                "automation_monitoring_error",
                                $"Monitoring error (cycle {cycleCount}): {ex.Message}",
                                ex
                            );
                        }
                        catch (Exception analyticsEx)
                        {
                            Debug.WriteLine($"[Analytics] Failed to log monitoring error: {analyticsEx.Message}");
                        }
                        
                        await _notificationService.ShowNotificationAsync(
                            "Midlertidig feil",
                            "Noe gikk galt. Prøver igjen.",
                            NotificationLevel.Warning
                        );
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Constants.Time.RETRY_DELAY_SECONDS), cancellationToken);
                }
            }

            return MonitoringLoopResult.Cancelled;
        }

        private readonly SemaphoreSlim _sessionFileLock = new SemaphoreSlim(1, 1);

        private async Task MarkSessionAsRegisteredAsync(string sessionKey)
        {
            await _sessionFileLock.WaitAsync();
            try
            {
                var filePath = GetRegisteredSessionsFilePath();
                var registeredSessions = await GetRegisteredSessionsForTodayAsync();
                
                if (!registeredSessions.ContainsKey(sessionKey))
                {
                    registeredSessions[sessionKey] = DateTime.Now;
                    
                    var json = JsonSerializer.Serialize(registeredSessions, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, json);
                    
                    _loggingService.LogDebug($"[SESSION] Marked {sessionKey} as registered");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SESSION] Error marking session: {ex.Message}");
            }
            finally
            {
                _sessionFileLock.Release();
            }
        }

        private async Task<Dictionary<string, DateTime>> GetRegisteredSessionsForTodayAsync()
        {
            await _sessionFileLock.WaitAsync();
            try
            {
                var filePath = GetRegisteredSessionsFilePath();
                
                if (!File.Exists(filePath))
                    return new Dictionary<string, DateTime>();

                var json = await File.ReadAllTextAsync(filePath);
                var allSessions = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) 
                    ?? new Dictionary<string, DateTime>();
                
                var today = DateTime.Now.Date;
                
                // Filter to only today's sessions
                var todaySessions = allSessions
                    .Where(kvp => kvp.Value.Date == today)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                
                // Auto-cleanup: If we found old sessions, rewrite file with only today's
                if (allSessions.Count != todaySessions.Count)
                {
                    var oldCount = allSessions.Count - todaySessions.Count;
                    _loggingService.LogInfo($"Rydder opp {oldCount} gamle registrering(er)");
                    
                    var cleanJson = JsonSerializer.Serialize(todaySessions, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, cleanJson);
                }
                
                return todaySessions;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[SESSION] Error reading sessions: {ex.Message}");
                return new Dictionary<string, DateTime>();
            }
            finally
            {
                _sessionFileLock.Release();
            }
        }

        private async Task<bool> IsSessionRegisteredAsync(string sessionKey)
        {
            var sessions = await GetRegisteredSessionsForTodayAsync();
            return sessions.ContainsKey(sessionKey);
        }

        private string GetRegisteredSessionsFilePath()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "registered_sessions.json");
        }

        private async Task<List<ScheduleItem>?> GetFullDayScheduleDataAsync()
        {
            const int MAX_RETRIES = Constants.Network.MAX_RETRY_ATTEMPTS;
            int retryCount = 0;
            
            while (retryCount < MAX_RETRIES)
            {
                try
                {
                    // Check and fix user parameters
                    if (_userParameters == null || !_userParameters.IsComplete)
                    {
                        _loggingService.LogWarning("User parameters missing or incomplete - attempting re-authentication");
                        var authResult = await RefreshAuthenticationAsync();
                        
                        if (!authResult.Success)
                        {
                            _loggingService.LogError($"Re-authentication failed (attempt {retryCount + 1}/{MAX_RETRIES})");
                            retryCount++;
                            if (retryCount < MAX_RETRIES)
                            {
                                await Task.Delay(2000);
                                continue;
                            }
                            return null;
                        }
                        
                        _loggingService.LogSuccess("Re-authentication successful - parameters restored");
                    }

                    // Check and fix cookies
                    if (_cookies == null || _cookies.Count == 0)
                    {
                        _loggingService.LogWarning("Cookies are missing - attempting re-authentication");
                        var authResult = await RefreshAuthenticationAsync();
                        
                        if (!authResult.Success)
                        {
                            _loggingService.LogError($"Re-authentication failed (attempt {retryCount + 1}/{MAX_RETRIES})");
                            retryCount++;
                            if (retryCount < MAX_RETRIES)
                            {
                                await Task.Delay(2000);
                                continue;
                            }
                            return null;
                        }
                        
                        _loggingService.LogSuccess("Re-authentication successful - cookies restored");
                    }

                    // Log attempt
                    _loggingService.LogInfo($"📡 Attempting to fetch schedule data (attempt {retryCount + 1}/{MAX_RETRIES})");

                    // Try fetching with current credentials
                    var scheduleData = await FetchScheduleDataAsync();
                    
                    // Success!
                    if (scheduleData != null && scheduleData.Count > 0)
                    {
                        _loggingService.LogSuccess($"Successfully fetched {scheduleData.Count} schedule items");
                        return scheduleData;
                    }

                    // Empty schedule (might be normal if no classes today)
                    if (scheduleData != null && scheduleData.Count == 0)
                    {
                        _loggingService.LogWarning("Schedule fetch returned 0 items - this might be normal if no classes today");
                        return scheduleData;
                    }

                    // If fetch returned null, cookies are likely expired - re-authenticate
                    _loggingService.LogWarning($"Schedule fetch failed - cookies likely expired (attempt {retryCount + 1}/{MAX_RETRIES})");
                    _loggingService.LogInfo("Attempting to refresh authentication");
                    
                    var reAuthResult = await RefreshAuthenticationAsync();
                    if (!reAuthResult.Success)
                    {
                        _loggingService.LogError("Re-authentication failed");
                        retryCount++;
                        
                        if (retryCount < MAX_RETRIES)
                        {
                            _loggingService.LogInfo($"Waiting 3 seconds before retry {retryCount + 1}");
                            await Task.Delay(3000);
                        }
                        continue;
                    }
                    
                    _loggingService.LogSuccess("Re-authentication successful - retrying schedule fetch");
                    
                    // Retry with fresh cookies (don't increment retry count since we just re-authed)
                    scheduleData = await FetchScheduleDataAsync();
                    
                    if (scheduleData != null)
                    {
                        _loggingService.LogSuccess($"Successfully fetched {scheduleData.Count} schedule items after re-auth");
                        return scheduleData;
                    }

                    // Still failed after re-auth
                    _loggingService.LogError("Schedule fetch still failed even after re-authentication");
                    retryCount++;
                    
                    if (retryCount < MAX_RETRIES)
                    {
                        _loggingService.LogInfo($"Waiting 3 seconds before retry {retryCount + 1}");
                        await Task.Delay(3000);
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Exception while fetching schedule data: {ex.Message}");
                    _loggingService.LogDebug($"Stack trace: {ex.StackTrace}");
                    retryCount++;
                    
                    if (retryCount < MAX_RETRIES)
                    {
                        _loggingService.LogInfo($"Waiting 3 seconds before retry {retryCount + 1}");
                        await Task.Delay(3000);
                    }
                }
            }
            
            _loggingService.LogError($"Failed to fetch schedule data after {MAX_RETRIES} attempts");
            
            // Track schedule fetch failure
            try
            {
                var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                await analyticsService.LogErrorAsync(
                    "automation_schedule_fetch_failed",
                    $"Failed to fetch schedule after {MAX_RETRIES} attempts"
                );
            }
            catch (Exception analyticsEx)
            {
                Debug.WriteLine($"[Analytics] Failed to log schedule fetch error: {analyticsEx.Message}");
            }
            
            await _notificationService.ShowNotificationAsync(
                "Kunne ikke hente timeplan",
                "Automatiseringen kunne ikke hente timeplandata etter flere forsøk. Sjekk nettverkstilkobling og prøv igjen.",
                NotificationLevel.Error,
                isHighPriority: true
            );
            return null;
        }

        private async Task<List<ScheduleItem>?> FetchScheduleDataAsync()
        {
            try
            {
                if (_userParameters == null)
                {
                    _loggingService.LogError("[FETCH] User parameters are null");
                    return null;
                }

                if (!_userParameters.IsComplete)
                {
                    _loggingService.LogError("[FETCH] User parameters are incomplete");
                    _loggingService.LogDebug($"FylkeId: {_userParameters.FylkeId}, PlanPeri: {_userParameters.PlanPeri}, SkoleId: {_userParameters.SkoleId}");
                    return null;
                }

                if (_cookies == null || _cookies.Count == 0)
                {
                    _loggingService.LogError("[FETCH] Cookies are missing or empty");
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                
                if (string.IsNullOrEmpty(jsessionId))
                {
                    _loggingService.LogError("[FETCH] JSESSIONID cookie is missing or empty");
                    _loggingService.LogDebug($"Available cookies: {string.Join(", ", _cookies.Keys)}");
                    return null;
                }

                // Build URL
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&offset=0&totalResults=true";

                _loggingService.LogDebug($"[FETCH] URL: {url.Replace(jsessionId, "***")}"); // Hide session ID in logs

                // Build request
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                _loggingService.LogDebug($"[FETCH] Sending request with {_cookies.Count} cookies");

                // Send request
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _loggingService.LogDebug($"[FETCH] Response status: {response.StatusCode}");
                _loggingService.LogDebug($"[FETCH] Response length: {responseContent.Length} characters");
                
                // Check for authentication errors
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _loggingService.LogWarning($"[FETCH] Authentication error ({response.StatusCode}) - cookies likely expired");
                        return null; // Signal to parent to re-authenticate
                    }
                    
                    _loggingService.LogError($"[FETCH] HTTP error {response.StatusCode}");
                    _loggingService.LogDebug($"Response content: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
                    return null;
                }

                if (responseContent.Contains("JSESSIONID") && responseContent.Contains("expired") ||
                    responseContent.Contains("login") && responseContent.Contains("required"))
                {
                    _loggingService.LogWarning("[FETCH] Response indicates session expired");
                    return null; 
                }

                // Try to parse the response
                try
                {
                    var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (scheduleResponse == null)
                    {
                        _loggingService.LogError("[FETCH] Failed to deserialize response - got null");
                        _loggingService.LogDebug($"Response preview: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}");
                        return null;
                    }

                    if (scheduleResponse.Items == null)
                    {
                        _loggingService.LogWarning("[FETCH] Response deserialized but Items is null");
                        _loggingService.LogDebug($"Response preview: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}");
                        return new List<ScheduleItem>();
                    }

                    _loggingService.LogSuccess($"[FETCH] Successfully parsed {scheduleResponse.Items.Count} schedule items");
                    
                    // Log a sample of what we got if there are items
                    if (scheduleResponse.Items.Count > 0)
                    {
                        var firstItem = scheduleResponse.Items[0];
                        _loggingService.LogDebug($"[FETCH] Sample item: Fag={firstItem.Fag}, KNavn={firstItem.KNavn}, Dato={firstItem.Dato}");
                    }
                    else
                    {
                        _loggingService.LogDebug($"[FETCH] Raw response: {responseContent}");
                    }
                    
                    return scheduleResponse.Items;
                }
                catch (JsonException jsonEx)
                {
                    _loggingService.LogError($"[FETCH] JSON deserialization error: {jsonEx.Message}");
                    _loggingService.LogDebug($"Response content: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
                    return null;
                }
            }
            catch (HttpRequestException httpEx)
            {
                _loggingService.LogError($"[FETCH] Network error: {httpEx.Message}");
                _loggingService.LogDebug($"Stack trace: {httpEx.StackTrace}");
                return null;
            }
            catch (TaskCanceledException timeoutEx)
            {
                _loggingService.LogError($"[FETCH] Request timeout: {timeoutEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[FETCH] Unexpected exception: {ex.Message}");
                _loggingService.LogDebug($"Exception type: {ex.GetType().Name}");
                _loggingService.LogDebug($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private bool HasConflictingClass(ScheduleItem stuSession, List<ScheduleItem> allScheduleItems)
        {
            try
            {
                if (!TimeSpan.TryParse(stuSession.StartKl, out var stuStartTime) ||
                    !TimeSpan.TryParse(stuSession.SluttKl, out var stuEndTime))
                {
                    return false;
                }

                var conflictingClasses = allScheduleItems
                    .Where(item => item.Dato == stuSession.Dato &&
                                  item.KNavn != "STU" &&
                                  item.Id != stuSession.Id)
                    .ToList();

                foreach (var otherClass in conflictingClasses)
                {
                    if (!TimeSpan.TryParse(otherClass.StartKl, out var otherStartTime) ||
                        !TimeSpan.TryParse(otherClass.SluttKl, out var otherEndTime))
                    {
                        continue;
                    }

                    bool hasOverlap = stuStartTime < otherEndTime && otherStartTime < stuEndTime;
                    if (hasOverlap)
                    {
                        _loggingService.LogInfo($"CONFLICT: STU {stuSession.StartKl}-{stuSession.SluttKl} overlaps with {otherClass.KNavn} ({otherClass.StartKl}-{otherClass.SluttKl})");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking for conflicts: {ex.Message}");
                return false;
            }
        }

        private RegistrationWindowStatus GetRegistrationWindowStatus(ScheduleItem stuTime)
        {
            if (stuTime.TidsromTilstedevaerelse == null)
                return RegistrationWindowStatus.Closed;

            var parts = stuTime.TidsromTilstedevaerelse.Split(" - ");
            if (parts.Length != 2)
                return RegistrationWindowStatus.Closed;

            if (!TimeSpan.TryParse(parts[0], out var startTime) ||
                !TimeSpan.TryParse(parts[1], out var endTime))
            {
                return RegistrationWindowStatus.Closed;
            }

            var now = DateTime.Now.TimeOfDay;

            if (now < startTime)
                return RegistrationWindowStatus.NotYetOpen;
            else if (now >= startTime && now <= endTime)
                return RegistrationWindowStatus.Open;
            else
                return RegistrationWindowStatus.Closed;
        }

        private async Task<bool> RegisterAttendanceAsync(ScheduleItem stuTime, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return false;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

                var publicIp = await GetPublicIpAsync();

                var payload = new
                {
                    name = "lagre_oppmote",
                    parameters = new object[]
                    {
                        new { fylkeid = _userParameters.FylkeId },
                        new { skoleid = _userParameters.SkoleId },
                        new { planperi = _userParameters.PlanPeri },
                        new { ansidato = stuTime.Dato },
                        new { stkode = stuTime.Stkode },
                        new { kl_trinn = stuTime.KlTrinn },
                        new { kl_id = stuTime.KlId },
                        new { k_navn = stuTime.KNavn },
                        new { gruppe_nr = stuTime.GruppeNr },
                        new { timenr = stuTime.Timenr },
                        new { fravaerstype = "M" },
                        new { ip = publicIp }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Origin", "https://iskole.net");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Check for network error in response
                    if (responseContent.Contains("Du må være koblet på skolens nettverk"))
                    {
                        _loggingService.LogError($"NETTVERKSFEIL: Må være tilkoblet skolens nettverk for å registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}");
                        
                        // Track network requirement error
                        try
                        {
                            var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                            await analyticsService.LogErrorAsync(
                                "automation_school_network_required",
                                $"School network required for STU {stuTime.StartKl}-{stuTime.SluttKl}"
                            );
                        }
                        catch (Exception analyticsEx)
                        {
                            Debug.WriteLine($"[Analytics] Failed to log network error: {analyticsEx.Message}");
                        }
                        
                        await _notificationService.ShowNotificationAsync(
                            "Koble til Skolens Nettverk",
                            $"Du må være tilkoblet skolens WiFi for å registrere STU {stuTime.StartKl}-{stuTime.SluttKl}.",
                            NotificationLevel.Warning
                        );
                        return false;
                    }

                    SessionRegistered?.Invoke(this, new SessionRegisteredEventArgs 
                    { 
                        SessionTime = $"{stuTime.StartKl}-{stuTime.SluttKl}",
                        RegistrationTime = DateTime.Now
                    });

                    // Schedule delayed verification (20 seconds later)
                    var verificationTask = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                            await VerifyRegistrationAsync(stuTime, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when automation is stopped - don't log as error
                            _loggingService.LogDebug("Verification task cancelled (automation stopped)");
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError($"Verification task failed: {ex.Message}");
                        }
                    }, cancellationToken);
                    _backgroundVerificationTasks.Add(verificationTask);

                    return true;
                }
                else
                {
                    _loggingService.LogError($"Registration failed with status {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Registration error: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                var ip = await _httpClient.GetStringAsync("https://api.ipify.org");
                return ip;
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>
        /// Verify that a registration was actually saved in iSkole
        /// Called 20 seconds after registration attempt
        /// </summary>
        private async Task VerifyRegistrationAsync(ScheduleItem stuTime, CancellationToken cancellationToken = default)
        {
            try
            {
                _loggingService.LogDebug($"[VERIFY] Checking if {stuTime.StartKl}-{stuTime.SluttKl} was actually registered in iSkole...");

                // Fetch fresh schedule data from iSkole
                var scheduleData = await FetchScheduleDataAsync();
                
                if (scheduleData == null)
                {
                    _loggingService.LogWarning($"[VERIFY] Could not fetch schedule data to verify registration");
                    return;
                }

                // Find the session we just registered
                var registeredSession = scheduleData.FirstOrDefault(s => 
                    s.Dato == stuTime.Dato &&
                    s.StartKl == stuTime.StartKl &&
                    s.SluttKl == stuTime.SluttKl &&
                    s.Stkode == stuTime.Stkode
                );

                if (registeredSession == null)
                {
                    _loggingService.LogWarning($"[VERIFY] Could not find session {stuTime.StartKl}-{stuTime.SluttKl} in schedule data");
                    return;
                }

                // Check if it's marked as attended (Typefravaer = "M" means "Møtt" / attended)
                if (registeredSession.Typefravaer == "M")
                {
                    _loggingService.LogSuccess($"[VERIFY] Confirmed: {stuTime.StartKl}-{stuTime.SluttKl} is registered in iSkole!");
                }
                else
                {
                    // Registration didn't stick - try again
                    _loggingService.LogWarning($"[VERIFY] Registration not found in iSkole for {stuTime.StartKl}-{stuTime.SluttKl}. Typefravaer status: {registeredSession.Typefravaer ?? "null"}");
                    _loggingService.LogInfo($"[VERIFY] Attempting to re-register...");

                    var retryResult = await RegisterAttendanceAsync(stuTime, cancellationToken);
                    
                    if (retryResult)
                    {
                        _loggingService.LogInfo($"[VERIFY] Re-registration sent successfully. Will verify again in 20 seconds.");
                        
                        // Verify the retry after another 20 seconds
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        var reVerifyData = await FetchScheduleDataAsync();
                        
                        if (reVerifyData != null)
                        {
                            var reVerifySession = reVerifyData.FirstOrDefault(s => 
                                s.Dato == stuTime.Dato &&
                                s.StartKl == stuTime.StartKl &&
                                s.SluttKl == stuTime.SluttKl &&
                                s.Stkode == stuTime.Stkode
                            );

                            if (reVerifySession?.Typefravaer == "M")
                            {
                                _loggingService.LogSuccess($"[VERIFY] Re-registration confirmed: {stuTime.StartKl}-{stuTime.SluttKl} is now registered!");
                            }
                            else
                            {
                                _loggingService.LogError($"[VERIFY] Re-registration also failed for {stuTime.StartKl}-{stuTime.SluttKl}");
                                
                                // Track verification failure
                                try
                                {
                                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                                    await analyticsService.LogErrorAsync(
                                        "automation_verification_failed",
                                        $"Registration verification failed for STU {stuTime.StartKl}-{stuTime.SluttKl} after retry"
                                    );
                                }
                                catch (Exception analyticsEx)
                                {
                                    Debug.WriteLine($"[Analytics] Failed to log verification error: {analyticsEx.Message}");
                                }
                                
                                await _notificationService.ShowNotificationAsync(
                                    "Registrering feilet",
                                    $"Kunne ikke registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}. Vennligst registrer manuelt.",
                                    NotificationLevel.Error,
                                    isHighPriority: true
                                );
                            }
                        }
                    }
                    else
                    {
                        _loggingService.LogError($"[VERIFY] Re-registration attempt failed");
                        
                        // Track re-registration failure
                        try
                        {
                            var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                            await analyticsService.LogErrorAsync(
                                "automation_reregistration_failed",
                                $"Re-registration attempt failed for STU {stuTime.StartKl}-{stuTime.SluttKl}"
                            );
                        }
                        catch (Exception analyticsEx)
                        {
                            Debug.WriteLine($"[Analytics] Failed to log re-registration error: {analyticsEx.Message}");
                        }
                        
                        await _notificationService.ShowNotificationAsync(
                            "Registrering feilet",
                            $"Kunne ikke registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}. Vennligst registrer manuelt.",
                            NotificationLevel.Error,
                            isHighPriority: true
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[VERIFY] Verification failed: {ex.Message}");
            }
        }

        public void SetCredentials(UserParameters userParameters, Dictionary<string, string> cookies)
        {
            _userParameters = userParameters;
            _cookies = cookies;
        }

        private async Task LoadUserParametersAsync()
        {
            try
            {
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                var filePath = Path.Combine(appSupportDir, "user_parameters.json");
                
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _userParameters = JsonSerializer.Deserialize<UserParameters>(json);
                    _loggingService.LogDebug($"[AUTOMATION] Loaded user parameters from file: FylkeId={_userParameters?.FylkeId}, SkoleId={_userParameters?.SkoleId}, PlanPeri={_userParameters?.PlanPeri}");
                    return;
                }
                
                // Fallback: try to load from keychain (old method) and migrate to file
                var keychainJson = await SecureCredentialStorage.GetCredentialAsync("user_parameters");
                if (!string.IsNullOrEmpty(keychainJson))
                {
                    _userParameters = JsonSerializer.Deserialize<UserParameters>(keychainJson);
                    if (_userParameters != null)
                    {
                        _loggingService.LogDebug("[AUTOMATION] Migrating user parameters from keychain to file");
                        
                        // Save to file
                        Directory.CreateDirectory(appSupportDir);
                        var json = JsonSerializer.Serialize(_userParameters, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(filePath, json);
                        
                        // Clean up old keychain entry
                        await SecureCredentialStorage.DeleteCredentialAsync("user_parameters");
                        _loggingService.LogDebug("[AUTOMATION] Migration complete - old keychain entry removed");
                        
                        _loggingService.LogDebug($"[AUTOMATION] Loaded user parameters from storage: FylkeId={_userParameters?.FylkeId}, SkoleId={_userParameters?.SkoleId}, PlanPeri={_userParameters?.PlanPeri}");
                        return;
                    }
                }
                
                _loggingService.LogDebug("[AUTOMATION] No user parameters found in file or keychain");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[AUTOMATION] Failed to load user parameters: {ex.Message}");
                _userParameters = null;
            }
        }

        /// <summary>
        /// Pre-check to see which STU sessions are already registered online
        /// </summary>
        private async Task<STUPreCheckResult> PreCheckRegisteredSTUSessionsAsync()
        {
            try
            {
                // Get attendance service
                var attendanceService = Services.DependencyInjection.ServiceContainer.GetOptionalService<AttendanceDataService>();
                if (attendanceService == null)
                {
                    _loggingService.LogWarning("AttendanceDataService not available for pre-check - using local cache only");
                    return new STUPreCheckResult
                    {
                        TotalSessions = 0,
                        RegisteredSessions = new List<string>(),
                        AllSessionsRegistered = false
                    };
                }

                // Set credentials for attendance service
                if (_userParameters != null && _cookies != null)
                {
                    attendanceService.SetCredentials(_userParameters, _cookies);
                }

                // Fetch today's schedule items directly
                var todayScheduleItems = await FetchTodayScheduleItemsAsync();
                if (todayScheduleItems == null || todayScheduleItems.Count == 0)
                {
                    _loggingService.LogWarning("Could not fetch today's schedule items for pre-check");
                    return new STUPreCheckResult
                    {
                        TotalSessions = 0,
                        RegisteredSessions = new List<string>(),
                        AllSessionsRegistered = false
                    };
                }

                // Find STU sessions for today
                var today = DateTime.Now.ToString("yyyyMMdd");
                var stuSessions = todayScheduleItems
                    .Where(item => item.Dato == today && 
                                  (item.Fag?.Contains("STU") == true || item.Fagnavn?.Contains("Studietid") == true))
                    .ToList();

                _loggingService.LogInfo($"[DEBUG PRE-CHECK] Fant {stuSessions.Count} STU-økter for i dag ({today})");
                foreach (var session in stuSessions)
                {
                    var startTime = ExtractTimeFromDateTime(session.Fradato);
                    var endTime = ExtractTimeFromDateTime(session.Tildato);
                    _loggingService.LogInfo($"[DEBUG PRE-CHECK] STU økt: {startTime}-{endTime}, Dato: {session.Dato}, Fravaer: '{session.Fravaer}'");
                }

                if (stuSessions.Count == 0)
                {
                    _loggingService.LogInfo("No STU sessions found for today in pre-check");
                    return new STUPreCheckResult
                    {
                        TotalSessions = 0,
                        RegisteredSessions = new List<string>(),
                        AllSessionsRegistered = false
                    };
                }

                // Filter out sessions that conflict with regular classes
                var regularClasses = todayScheduleItems
                    .Where(item => item.Dato == today && 
                                  item.Fag != null && 
                                  !item.Fag.Contains("STU") &&
                                  item.Fagnavn != null &&
                                  !item.Fagnavn.Contains("Studietid"))
                    .ToList();

                var validStuSessions = new List<MonthlyScheduleItem>();
                foreach (var stuSession in stuSessions)
                {
                    bool hasConflict = false;
                    foreach (var regularClass in regularClasses)
                    {
                        if (DoSessionsOverlap(stuSession, regularClass))
                        {
                            hasConflict = true;
                            break;
                        }
                    }
                    
                    if (!hasConflict)
                    {
                        validStuSessions.Add(stuSession);
                    }
                }

                // Check which sessions are already registered (Fravaer == "M")
                var registeredSessions = new List<string>();
                foreach (var session in validStuSessions)
                {
                    // Extract time from Fradato and Tildato
                    var startTime = ExtractTimeFromDateTime(session.Fradato);
                    var endTime = ExtractTimeFromDateTime(session.Tildato);
                    
                    if (!string.IsNullOrEmpty(startTime) && !string.IsNullOrEmpty(endTime))
                    {
                        var sessionKey = $"{startTime}-{endTime}";
                        
                        // Add detailed debugging for each session
                        _loggingService.LogInfo($"[DEBUG] Sjekker økt {sessionKey}: Fravaer='{session.Fravaer}', Fag='{session.Fag}', Fagnavn='{session.Fagnavn}'");
                        
                        // Check if this session is marked as registered online
                        if (!string.IsNullOrEmpty(session.Fravaer) && session.Fravaer == "M")
                        {
                            registeredSessions.Add(sessionKey);
                            _loggingService.LogInfo($"STU økt {sessionKey} er allerede registrert online (Fravaer='M')");
                            
                            // DON'T mark it in local cache - let the monitoring loop handle verification
                            // await MarkSessionAsRegisteredAsync(sessionKey);
                        }
                        else
                        {
                            _loggingService.LogInfo($"STU økt {sessionKey} er IKKE registrert online (Fravaer='{session.Fravaer}')");
                        }
                    }
                }

                bool allRegistered = registeredSessions.Count == validStuSessions.Count && validStuSessions.Count > 0;

                _loggingService.LogInfo($"Pre-check resultat: {registeredSessions.Count}/{validStuSessions.Count} STU-økter allerede registrert");

                return new STUPreCheckResult
                {
                    TotalSessions = validStuSessions.Count,
                    RegisteredSessions = registeredSessions,
                    AllSessionsRegistered = allRegistered
                };
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error during STU pre-check: {ex.Message}");
                return new STUPreCheckResult
                {
                    TotalSessions = 0,
                    RegisteredSessions = new List<string>(),
                    AllSessionsRegistered = false
                };
            }
        }

        /// <summary>
        /// Fetch today's schedule items with registration status
        /// </summary>
        private async Task<List<MonthlyScheduleItem>?> FetchTodayScheduleItemsAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                {
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId))
                {
                    return null;
                }

                var today = DateTime.Now;
                var startDate = today.ToString("yyyyMMdd");
                var endDate = today.ToString("yyyyMMdd");

                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId},startDate={startDate},endDate={endDate}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var scheduleResponse = JsonSerializer.Deserialize<MonthlyScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return scheduleResponse?.Items;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error fetching today's schedule items: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper method to extract time (HH:mm) from datetime string
        /// </summary>
        private string? ExtractTimeFromDateTime(string? dateTimeStr)
        {
            if (string.IsNullOrEmpty(dateTimeStr))
                return null;

            try
            {
                // Try to parse the datetime and extract time
                if (DateTime.TryParse(dateTimeStr, out var dateTime))
                {
                    return dateTime.ToString("HH:mm");
                }
                
                // If direct parsing fails, try to extract time pattern
                var timeMatch = System.Text.RegularExpressions.Regex.Match(dateTimeStr, @"(\d{2}):(\d{2})");
                if (timeMatch.Success)
                {
                    return timeMatch.Value;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return null;
        }

        /// <summary>
        /// Check if two monthly schedule sessions overlap in time
        /// </summary>
        private bool DoSessionsOverlap(MonthlyScheduleItem session1, MonthlyScheduleItem session2)
        {
            try
            {
                var start1 = ParseDateTime(session1.Fradato);
                var end1 = ParseDateTime(session1.Tildato);
                var start2 = ParseDateTime(session2.Fradato);
                var end2 = ParseDateTime(session2.Tildato);

                if (!start1.HasValue || !end1.HasValue || !start2.HasValue || !end2.HasValue)
                    return false;

                // Sessions overlap if one starts before the other ends
                return start1 < end2 && start2 < end1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse datetime string to DateTime
        /// </summary>
        private DateTime? ParseDateTime(string? dateTimeStr)
        {
            if (string.IsNullOrEmpty(dateTimeStr))
                return null;

            if (DateTime.TryParse(dateTimeStr, out var result))
                return result;

            return null;
        }

        /// <summary>
        /// Filter STU sessions to remove those that conflict with regular classes
        /// </summary>
        private List<ScheduleItem> FilterValidStuSessions(List<ScheduleItem> stuSessions)
        {
            return stuSessions
                .Where(session => !HasConflictingClass(session, _cachedScheduleData))
                .ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _loggingService?.LogDebug("[AutomationService] Starting disposal");
                
                // Cancel any running operations
                _cancellationTokenSource?.Cancel();
                
                // Wait for background verification tasks to complete (synchronously)
                try
                {
                    if (_backgroundVerificationTasks.Count > 0)
                    {
                        _loggingService?.LogDebug($"Waiting for {_backgroundVerificationTasks.Count} background verification tasks to complete");
                        var incompleteTasks = _backgroundVerificationTasks.Where(t => !t.IsCompleted).ToArray();
                        if (incompleteTasks.Length > 0)
                        {
                            Task.WaitAll(incompleteTasks, TimeSpan.FromSeconds(5)); // Wait max 5 seconds
                        }
                        _backgroundVerificationTasks.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error waiting for background tasks: {ex.Message}");
                }
                
                // Dispose all tracked resources
                lock (_disposalLock)
                {
                    _disposed = true;
                    
                    foreach (var disposable in _disposables)
                    {
                        try
                        {
                            disposable?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _loggingService?.LogError($"Error disposing resource: {ex.Message}");
                        }
                    }
                    _disposables.Clear();
                }
                
                // Dispose cancellation token source
                try
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing cancellation token source: {ex.Message}");
                }
                
                // Dispose session file lock
                try
                {
                    _sessionFileLock?.Dispose();
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Error disposing session file lock: {ex.Message}");
                }
                
                // Clear references to help GC
                _userParameters = null;
                _cookies?.Clear();
                _cookies = null;
                _cachedScheduleData?.Clear();
                _cachedScheduleData = null;
                
                // Note: _httpClient is shared from HttpClientFactory, so we don't dispose it
                
                _loggingService?.LogDebug("[AutomationService] Disposal completed successfully");
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error disposing AutomationService: {ex.Message}");
            }
        }

        private enum RegistrationWindowStatus
        {
            NotYetOpen,
            Open,
            Closed
        }

        
    }

    /// <summary>
    /// Result of pre-checking which STU sessions are already registered
    /// </summary>
    public class STUPreCheckResult
    {
        public int TotalSessions { get; set; }
        public List<string> RegisteredSessions { get; set; } = new List<string>();
        public bool AllSessionsRegistered { get; set; }
    }
}