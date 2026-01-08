using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Common;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    public class AutomationService : IAutomationService
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

        public event EventHandler<AutomationStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AutomationProgressEventArgs>? ProgressUpdated;

        public bool IsRunning => _isRunning;
        public string CurrentStatus => _currentStatus;

        public AutomationService(ILoggingService loggingService, INotificationService notificationService)
        {
            _loggingService = loggingService;
            _notificationService = notificationService;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<AutomationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                return AutomationResult.Failed("Automation is already running");
            }

            try
            {
                _isRunning = true;
                _currentStatus = "Starting automation...";
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(true, _currentStatus));

                // Load credentials and parameters
                var loadResult = await LoadCredentialsAsync();
                if (!loadResult.Success)
                {
                    return loadResult;
                }

                // Check internet connection
                if (!await CheckInternetConnectionAsync())
                {
                    return AutomationResult.Failed("No internet connection available");
                }

                // Mark today as started to prevent duplicate auto-starts
                await SchoolTimeChecker.MarkTodayAsStartedAsync();

                _cancellationTokenSource = new CancellationTokenSource();
                var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token).Token;

                _loggingService.LogInfo("Starter automatisering...");
                await _notificationService.ShowNotificationAsync(
                    "Automatisering startet", 
                    "STU tidsregistrering automatisering kjører nå", 
                    NotificationLevel.Success
                );

                // Start the monitoring loop
                await RunMonitoringLoopAsync(combinedToken);

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
                return AutomationResult.Failed($"Automation failed: {ex.Message}", ex);
            }
            finally
            {
                _isRunning = false;
                _currentStatus = "Ready";
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                StatusChanged?.Invoke(this, new AutomationStatusChangedEventArgs(false, _currentStatus));
            }
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
                _loggingService.LogInfo("Stopp forespurt - stopper automatisering...");
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
                _loggingService.LogInfo("🔐 Starter re-autentisering...");
                
                var authService = new AuthenticationService(_notificationService);
                var authResult = await authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Cookies.Count > 0)
                {
                    _cookies = authResult.Cookies;
                    
                    if (authResult.Parameters != null && authResult.Parameters.IsComplete)
                    {
                        _userParameters = authResult.Parameters;
                    }

                    _loggingService.LogSuccess($"✓ Autentisering fullført! Fikk {authResult.Cookies.Count} cookies");
                    return AutomationResult.Successful("Authentication refreshed successfully");
                }
                else
                {
                    return AutomationResult.Failed("Authentication failed");
                }
            }
            catch (Exception ex)
            {
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
                // Load cookies
                _cookies = await SecureCredentialStorage.LoadCookiesAsync();
                
                if (_cookies == null || _cookies.Count == 0)
                {
                    _loggingService.LogInfo("Ingen cookies funnet - autentiserer på nytt...");
                    var authResult = await RefreshAuthenticationAsync();
                    if (!authResult.Success)
                    {
                        return AutomationResult.Failed("Failed to authenticate");
                    }
                }

                // Load user parameters (you might need to implement this)
                // For now, we'll assume they're loaded during authentication
                if (_userParameters == null || !_userParameters.IsComplete)
                {
                    return AutomationResult.Failed("Missing user parameters");
                }

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
                _loggingService.LogDebug("Checking internet connectivity...");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.GetAsync("https://www.google.com", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
        {
            int cycleCount = 0;

            _loggingService.LogInfo("Henter timeplandata for hele dagen...");
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
                return;
            }

            // Filter out conflicting sessions
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
                return;
            }

            _loggingService.LogInfo($"Etter konflikt-sjekking: {validStuSessions.Count} av {todaysStuSessions.Count} STU-økter er gyldige");

            var registeredSessions = new HashSet<string>();

            // Main monitoring loop
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

                        if (registeredSessions.Contains(sessionKey))
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
                                        registeredSessions.Add(sessionKey);
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

                    if (allSessionsComplete || registeredSessions.Count == validStuSessions.Count)
                    {
                        _loggingService.LogSuccess($"Alle {validStuSessions.Count} gyldige STU-økter er håndtert for i dag!");
                        await SchoolTimeChecker.MarkTodayAsCompletedAsync();
                        
                        if (registeredSessions.Count > 0)
                        {
                            await _notificationService.ShowNotificationAsync(
                                "Alle Studietimer Registrert",
                                $"Alle {validStuSessions.Count} gyldige STU-økter er fullført og registrert!",
                                NotificationLevel.Success
                            );
                        }
                        break;
                    }

                    _loggingService.LogInfo($"Status: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} lukkede/registrerte");

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Overvåkingsfeil: {ex.Message}");
                    
                    // Only notify on monitoring errors every 5 minutes to avoid spam
                    if (cycleCount % 10 == 0)
                    {
                        await _notificationService.ShowNotificationAsync(
                            "Overvåkingsfeil",
                            $"Feil under overvåking: {ex.Message}. Prøver igjen...",
                            NotificationLevel.Warning
                        );
                    }
                    
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        private async Task<List<ScheduleItem>?> GetFullDayScheduleDataAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&offset=0&totalResults=true";

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
                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return scheduleResponse?.Items;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error getting schedule data: {ex.Message}");
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

        public void Dispose()
        {
            _cancellationTokenSource?.Dispose();
            _httpClient?.Dispose();
        }

        private enum RegistrationWindowStatus
        {
            NotYetOpen,
            Open,
            Closed
        }
    }
}