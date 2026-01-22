using System;
using System.Collections.Generic;
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
    public class AutomationService : IAutomationService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning;
        private string _currentStatus = "Ready";
        private UserParameters? _userParameters;
        private Dictionary<string, string>? _cookies;
        private List<ScheduleItem>? _cachedScheduleData;
        private bool _disposed = false;

        // Resource tracking for proper disposal
        private readonly List<IDisposable> _disposables = new();
        private readonly object _disposalLock = new object();

        // Constants
        private const int MONITORING_INTERVAL_MS = 30_000; // 30 seconds
        private const int REGISTRATION_WINDOW_MINUTES = Constants.Time.REGISTRATION_WINDOW_MINUTES;

        public event EventHandler<AutomationStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AutomationProgressEventArgs>? ProgressUpdated;
        public event EventHandler<SessionRegisteredEventArgs>? SessionRegistered;

        public bool IsRunning => _isRunning;
        public string CurrentStatus => _currentStatus;

        public AutomationService(ILoggingService loggingService, INotificationService notificationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _httpClient = HttpClientFactory.DefaultClient;
            
            // Track the HttpClient for monitoring (but don't dispose it as it's shared)
            _loggingService.LogDebug("[AutomationService] Initialized with shared HttpClient");
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
                if (!await CheckInternetConnectionAsync())
                {
                    finalStatus = "Ingen internett-tilkobling";
                    return AutomationResult.Failed("No internet connection available");
                }

                // Mark today as started to prevent duplicate auto-starts
                await SchoolTimeChecker.MarkTodayAsStartedAsync();

                // Create and track cancellation token source
                localCancellationSource = new CancellationTokenSource();
                TrackDisposable(localCancellationSource);
                _cancellationTokenSource = localCancellationSource;
                
                var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token).Token;

                _loggingService.LogInfo("Starter automatisering");
                await _notificationService.ShowNotificationAsync(
                    "Automatisering startet",
                    "STU tidsregistrering automatisering kjører nå",
                    NotificationLevel.Success
                );

                // Start the monitoring loop
                var loopResult = await RunMonitoringLoopAsync(combinedToken);

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
                await _notificationService.ShowNotificationAsync(
                    "Automatisering Feilet",
                    $"En uventet feil oppstod: {ex.Message}",
                    NotificationLevel.Error,
                    isHighPriority: true
                );
                finalStatus = "Automatisering feilet";
                return AutomationResult.Failed($"Automation failed: {ex.Message}", ex);
            }
            finally
            {
                _isRunning = false;
                _currentStatus = finalStatus; // Use the tracked final status instead of always "Ready"
                
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
                
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));
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
                return AutomationResult.Failed("Automation is not running");
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                _loggingService.LogInfo("Stopp forespurt - stopper automatisering");
                await _notificationService.ShowNotificationAsync(
                    "Automatisering stoppet", 
                    "Automatisering har blitt stoppet av bruker", 
                    NotificationLevel.Info
                );
                return AutomationResult.Successful("Automation stopped successfully");
            }
            catch (Exception ex)
            {
                return AutomationResult.Failed($"Failed to stop automation: {ex.Message}", ex);
            }
        }

        public async Task<AutomationResult> RefreshAuthenticationAsync()
        {
            try
            {
                _loggingService.LogInfo("Starting re-authentication");
                
                var authService = new AuthenticationService(_notificationService);
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
                var result = await SchoolTimeChecker.ShouldAutoStartAutomationAsync();
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

        private async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                _loggingService.LogDebug("Checking internet connectivity");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Constants.Time.SHORT_TIMEOUT_SECONDS));
                var response = await _httpClient.GetAsync("https://www.google.com", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<MonitoringLoopResult?> RunMonitoringLoopAsync(CancellationToken cancellationToken)
        {
            int cycleCount = 0;

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
                await _notificationService.ShowNotificationAsync(
                    "Ingen STUDIE-økter funnet for i dag",
                    "Det er ingen STU-økter å registrere for i dag. Automatiseringen stopper.",
                    NotificationLevel.Info
                );
                return MonitoringLoopResult.NoSessionsFound;
            }

            var validStuSessions = todaysStuSessions
                .Where(session => !HasConflictingClass(session, _cachedScheduleData))
                .ToList();

            if (validStuSessions.Count == 0)
            {
                _loggingService.LogInfo("Alle STU-økter har konflikter med andre timer - ingen å registrere");
                await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                await _notificationService.ShowNotificationAsync(
                    "Ingen gyldige STU-økter",
                    "Alle STU-økter overlapper med andre klasser. Ingen registreringer vil bli gjort.",
                    NotificationLevel.Warning
                );
                return MonitoringLoopResult.AllSessionsConflict;
            }

            _loggingService.LogInfo($"Etter konflikt-sjekking: {validStuSessions.Count} av {todaysStuSessions.Count} STU-økter er gyldige");

            // Load previously registered sessions from disk
            var registeredSessions = await GetRegisteredSessionsForTodayAsync();
            var registeredSessionKeys = new HashSet<string>(registeredSessions.Keys);
            
            if (registeredSessionKeys.Count > 0)
            {
                _loggingService.LogInfo($"📋 Fant {registeredSessionKeys.Count} allerede registrerte økter for i dag");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    cycleCount++;
                    var currentTime = DateTime.Now.ToString("HH:mm");
                    _loggingService.LogInfo($"Syklus #{cycleCount} - Sjekker STU registreringsvinduer (kl. {currentTime})");

                    ProgressUpdated?.Invoke(this, new AutomationProgressEventArgs(
                        $"Syklus #{cycleCount} - Sjekker registreringsvinduer", cycleCount));

                    bool allSessionsComplete = true;
                    int openWindows = 0;
                    int closedWindows = 0;
                    int notYetOpenWindows = 0;

                    foreach (var stuSession in validStuSessions)
                    {
                        var sessionKey = $"{stuSession.StartKl}-{stuSession.SluttKl}";

                        // Check if already registered (from disk)
                        if (registeredSessionKeys.Contains(sessionKey))
                        {
                            closedWindows++;
                            continue;
                        }

                        var registrationStatus = GetRegistrationWindowStatus(stuSession);

                        switch (registrationStatus)
                        {
                            case RegistrationWindowStatus.Open:
                                openWindows++;
                                allSessionsComplete = false;
                                
                                // Double-check if registered (in case another instance registered it)
                                if (await IsSessionRegisteredAsync(sessionKey))
                                {
                                    _loggingService.LogInfo($"STU økt {sessionKey} er allerede registrert");
                                    registeredSessionKeys.Add(sessionKey);
                                    closedWindows++;
                                    openWindows--;
                                    break;
                                }
                                
                                _loggingService.LogInfo($"Registreringsvindu er ÅPENT for STU økt {stuSession.StartKl}-{stuSession.SluttKl}");

                                try
                                {
                                    var registrationResult = await RegisterAttendanceAsync(stuSession);
                                    if (registrationResult)
                                    {
                                        _loggingService.LogSuccess($"Registrerte oppmøte for {stuSession.StartKl}-{stuSession.SluttKl}!");
                                        await _notificationService.ShowNotificationAsync(
                                            "Registrering vellykket",
                                            $"Registrert for STU {stuSession.StartKl}-{stuSession.SluttKl}",
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
                                    await _notificationService.ShowNotificationAsync(
                                        "Registrering Feilet",
                                        $"Kunne ikke registrere STU {stuSession.StartKl}-{stuSession.SluttKl}: {regEx.Message}",
                                        NotificationLevel.Error
                                    );
                                }
                                break;

                            case RegistrationWindowStatus.NotYetOpen:
                                notYetOpenWindows++;
                                allSessionsComplete = false;
                                break;

                            case RegistrationWindowStatus.Closed:
                                closedWindows++;
                                break;
                        }
                    }

                    if (allSessionsComplete || registeredSessionKeys.Count == validStuSessions.Count)
                    {
                        _loggingService.LogSuccess($"Alle {validStuSessions.Count} gyldige STU-økter er håndtert for i dag!");
                        await SchoolTimeChecker.MarkTodayAsCompletedAsync();

                        if (registeredSessionKeys.Count > 0)
                        {
                            await _notificationService.ShowNotificationAsync(
                                "Alle Studietimer Registrert",
                                $"Alle {validStuSessions.Count} gyldige STU-økter er fullført og registrert!",
                                NotificationLevel.Success
                            );
                        }
                        else
                        {
                            await _notificationService.ShowNotificationAsync(
                                "Ingen studietimer igjen",
                                "Ingen STU-økter gjenstår.",
                                NotificationLevel.Info
                            );
                        }
                        return MonitoringLoopResult.AllComplete;
                    }

                    _loggingService.LogInfo($"Status: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} lukkede/registrerte");

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
                        await _notificationService.ShowNotificationAsync(
                            "Overvåkingsfeil",
                            $"Feil under overvåking: {ex.Message}. Prøver igjen",
                            NotificationLevel.Warning
                        );
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Constants.Time.RETRY_DELAY_SECONDS), cancellationToken);
                }
            }

            return MonitoringLoopResult.Cancelled;
        }

        private async Task MarkSessionAsRegisteredAsync(string sessionKey)
        {
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
        }

        private async Task<Dictionary<string, DateTime>> GetRegisteredSessionsForTodayAsync()
        {
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
                    _loggingService.LogInfo($"🧹 Rydder opp {oldCount} gamle registrering(er)");
                    
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

        private async Task<bool> RegisterAttendanceAsync(ScheduleItem stuTime)
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

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Check for network error in response
                    if (responseContent.Contains("Du må være koblet på skolens nettverk"))
                    {
                        _loggingService.LogError($"NETTVERKSFEIL: Må være tilkoblet skolens nettverk for å registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}");
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

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _loggingService?.LogDebug("[AutomationService] Starting disposal");
                
                // Cancel any running operations
                _cancellationTokenSource?.Cancel();
                
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
}