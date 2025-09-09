using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string Level { get; set; } // INFO, SUCCESS, ERROR, DEBUG
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }

    public class SimpleCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !_isExecuting && _canExecute();

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public partial class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isAutomationRunning;
        private string _statusMessage = "Ready";
        private IWebDriver _webDriver;
        private ObservableCollection<LogEntry> _logEntries;
        private bool _showDetailedLogs = true;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {
            _httpClient = new HttpClient();
            _logEntries = new ObservableCollection<LogEntry>();
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            ClearLogsCommand = new SimpleCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new SimpleCommand(ToggleDetailedLogsAsync);

            LogInfo("Application ready");
        }

        public string Greeting => "AkademiTrack - STU Time Registration";

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAutomationRunning
        {
            get => _isAutomationRunning;
            private set
            {
                if (_isAutomationRunning != value)
                {
                    _isAutomationRunning = value;
                    OnPropertyChanged();
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<LogEntry> LogEntries
        {
            get => _logEntries;
            private set
            {
                _logEntries = value;
                OnPropertyChanged();
            }
        }

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set
            {
                _showDetailedLogs = value;
                OnPropertyChanged();
            }
        }

        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }

        private void LogInfo(string message)
        {
            AddLogEntry(message, "INFO");
        }

        private void LogSuccess(string message)
        {
            AddLogEntry(message, "SUCCESS");
        }

        private void LogError(string message)
        {
            AddLogEntry(message, "ERROR");
        }

        private void LogDebug(string message)
        {
            if (ShowDetailedLogs)
            {
                AddLogEntry(message, "DEBUG");
            }
        }

        private void AddLogEntry(string message, string level)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            };

            if (Dispatcher.UIThread.CheckAccess())
            {
                LogEntries.Add(logEntry);
                StatusMessage = logEntry.FormattedMessage;

                // Keep only last 100 entries to prevent memory issues
                while (LogEntries.Count > 100)
                {
                    LogEntries.RemoveAt(0);
                }
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogEntries.Add(logEntry);
                    StatusMessage = logEntry.FormattedMessage;

                    while (LogEntries.Count > 100)
                    {
                        LogEntries.RemoveAt(0);
                    }
                });
            }
        }

        private async Task ClearLogsAsync()
        {
            LogEntries.Clear();
            LogInfo("Logs cleared");
        }

        private async Task ToggleDetailedLogsAsync()
        {
            ShowDetailedLogs = !ShowDetailedLogs;
            LogInfo($"Detailed logging {(ShowDetailedLogs ? "enabled" : "disabled")}");
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning) return;

            IsAutomationRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                LogInfo("Starting automation...");

                // Step 1: Check if existing cookies work
                LogDebug("Loading existing cookies from file...");
                var cookies = await LoadCookiesAsync();
                bool cookiesValid = false;

                if (cookies != null)
                {
                    LogInfo($"Found {cookies.Count} existing cookies, testing validity...");
                    cookiesValid = await TestCookiesAsync(cookies);

                    if (cookiesValid)
                    {
                        LogSuccess("Existing cookies are valid!");
                    }
                    else
                    {
                        LogInfo("Existing cookies are invalid or expired");
                    }
                }
                else
                {
                    LogInfo("No existing cookies found");
                }

                // Step 2: If cookies don't work, get new ones via browser login
                if (!cookiesValid)
                {
                    LogInfo("Opening browser for fresh login...");
                    cookies = await GetCookiesViaBrowserAsync();

                    if (cookies == null)
                    {
                        LogError("Failed to get cookies from browser login");
                        return;
                    }

                    LogSuccess($"Successfully obtained {cookies.Count} fresh cookies");
                }

                LogSuccess("Authentication complete - starting monitoring loop...");

                // Step 3: Start the monitoring loop
                await RunMonitoringLoopAsync(_cancellationTokenSource.Token, cookies);
            }
            catch (OperationCanceledException)
            {
                LogInfo("Automation stopped by user");
            }
            catch (Exception ex)
            {
                LogError($"Automation error: {ex.Message}");
                if (ShowDetailedLogs)
                {
                    LogDebug($"Stack trace: {ex.StackTrace}");
                }
            }
            finally
            {
                IsAutomationRunning = false;
                _cancellationTokenSource?.Dispose();
                LogInfo("Automation stopped");
            }
        }

        private async Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                LogInfo("Stop requested - stopping automation...");
            }
        }

        private async Task OpenSettingsAsync()
        {
            try
            {
                // Open the folder containing cookies.json for manual editing
                var currentDir = Directory.GetCurrentDirectory();
                var cookiesPath = Path.Combine(currentDir, "cookies.json");

                LogInfo($"Opening settings folder: {currentDir}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer.exe", currentDir);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", currentDir);
                }
                else
                {
                    Process.Start("xdg-open", currentDir);
                }

                LogSuccess("Settings folder opened");
            }
            catch (Exception ex)
            {
                LogError($"Could not open settings folder: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task<Dictionary<string, string>> LoadCookiesAsync()
        {
            try
            {
                if (!File.Exists("cookies.json"))
                {
                    LogDebug("No cookies.json file found");
                    return null;
                }

                var json = await File.ReadAllTextAsync("cookies.json");
                var cookieArray = JsonSerializer.Deserialize<Cookie[]>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug($"Loaded {cookieArray?.Length ?? 0} cookies from file");
                return cookieArray?.ToDictionary(c => c.Name, c => c.Value);
            }
            catch (Exception ex)
            {
                LogError($"Failed to load cookies: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> TestCookiesAsync(Dictionary<string, string> cookies)
        {
            try
            {
                LogDebug("Testing cookies by fetching schedule data...");
                var scheduleData = await GetScheduleDataAsync(cookies);
                bool isValid = scheduleData?.Items != null;

                if (isValid)
                {
                    LogDebug($"Cookie test successful - found {scheduleData.Items.Count} schedule items");
                }
                else
                {
                    LogDebug("Cookie test failed - no schedule data returned");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                LogDebug($"Cookie test failed with exception: {ex.Message}");
                return false;
            }
        }

        private async Task<Dictionary<string, string>> GetCookiesViaBrowserAsync()
        {
            try
            {
                // Setup Chrome options
                var options = new ChromeOptions();
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--start-maximized");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    options.AddArgument("--disable-gpu");
                }

                LogInfo("Initializing Chrome browser...");
                _webDriver = new ChromeDriver(options);

                // Navigate to login page
                LogInfo("Navigating to login page: https://iskole.net/elev/?ojr=login");
                _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                LogInfo("Please complete the login process in the browser");
                LogInfo("Waiting for navigation to: https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");

                // Wait for user to reach the target URL
                var targetReached = await WaitForTargetUrlAsync();

                if (!targetReached)
                {
                    LogError("Timeout - login was not completed within 10 minutes");
                    return null;
                }

                LogSuccess("Login completed successfully!");
                LogInfo("Extracting cookies from browser session...");

                // Extract cookies
                var seleniumCookies = _webDriver.Manage().Cookies.AllCookies;
                var cookieDict = seleniumCookies.ToDictionary(c => c.Name, c => c.Value);

                LogDebug($"Extracted cookies: {string.Join(", ", cookieDict.Keys)}");

                // Save cookies
                await SaveCookiesAsync(seleniumCookies.Select(c => new Cookie { Name = c.Name, Value = c.Value }).ToArray());

                LogSuccess($"Successfully extracted and saved {cookieDict.Count} cookies");
                return cookieDict;
            }
            catch (Exception ex)
            {
                LogError($"Browser login failed: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    LogInfo("Closing browser...");
                    _webDriver?.Quit();
                    _webDriver?.Dispose();
                    _webDriver = null;
                    LogDebug("Browser closed successfully");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error closing browser: {ex.Message}");
                }
            }
        }

        private async Task<bool> WaitForTargetUrlAsync()
        {
            var timeout = DateTime.Now.AddMinutes(10);
            var targetUrl = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan";
            int checkCount = 0;

            while (DateTime.Now < timeout)
            {
                try
                {
                    checkCount++;
                    var currentUrl = _webDriver.Url;

                    if (checkCount % 15 == 0) // Log every 30 seconds (15 * 2 second delay)
                    {
                        LogDebug($"Current URL: {currentUrl}");
                    }

                    if (currentUrl.Contains("isFeideinnlogget=true") && currentUrl.Contains("ojr=timeplan"))
                    {
                        LogDebug($"Target URL reached after {checkCount * 2} seconds");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error checking URL: {ex.Message}");
                }

                await Task.Delay(2000);
            }

            return false;
        }

        private async Task SaveCookiesAsync(Cookie[] cookies)
        {
            try
            {
                var json = JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync("cookies.json", json);
                LogDebug("Cookies saved to cookies.json file");
            }
            catch (Exception ex)
            {
                LogError($"Failed to save cookies: {ex.Message}");
                throw;
            }
        }

        private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken, Dictionary<string, string> cookies)
        {
            int cycleCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    cycleCount++;
                    LogInfo($"Monitoring cycle #{cycleCount} - checking for STU times...");

                    // Get today's schedule
                    LogDebug("Fetching schedule data from server...");
                    var scheduleData = await GetScheduleDataAsync(cookies);

                    if (scheduleData?.Items == null)
                    {
                        LogError("Failed to get schedule data - cookies may be expired");
                        LogInfo("Automation will stop - restart to re-authenticate");
                        break;
                    }

                    LogDebug($"Retrieved {scheduleData.Items.Count} schedule items");

                    // Find STU times for today
                    var today = DateTime.Now.ToString("yyyyMMdd");
                    var stuTimes = scheduleData.Items
                        .Where(item => item.Dato == today && item.KNavn == "STU")
                        .ToList();

                    LogInfo($"Found {stuTimes.Count} STU sessions for today ({DateTime.Now:yyyy-MM-dd})");

                    if (stuTimes.Any())
                    {
                        foreach (var stuTime in stuTimes)
                        {
                            LogDebug($"STU session: {stuTime.StartKl}-{stuTime.SluttKl}, Registration window: {stuTime.TidsromTilstedevaerelse}");
                        }
                    }

                    // Check if all sessions are complete
                    bool allSessionsComplete = true;
                    int openWindows = 0;
                    int closedWindows = 0;

                    // Check each STU time
                    foreach (var stuTime in stuTimes)
                    {
                        var registrationStatus = GetRegistrationWindowStatus(stuTime);

                        switch (registrationStatus)
                        {
                            case RegistrationWindowStatus.Open:
                                openWindows++;
                                allSessionsComplete = false;
                                LogInfo($"⏰ Registration window is OPEN for STU session {stuTime.StartKl}-{stuTime.SluttKl}");
                                LogInfo("Attempting to register attendance...");

                                try
                                {
                                    await RegisterAttendanceAsync(stuTime, cookies);
                                    LogSuccess($"✅ Successfully registered attendance for {stuTime.StartKl}-{stuTime.SluttKl}!");
                                }
                                catch (Exception regEx)
                                {
                                    LogError($"❌ Registration failed: {regEx.Message}");
                                }
                                break;

                            case RegistrationWindowStatus.NotYetOpen:
                                allSessionsComplete = false;
                                var now = DateTime.Now.ToString("HH:mm");
                                LogDebug($"Registration window not open yet (current time: {now}, window: {stuTime.TidsromTilstedevaerelse})");
                                break;

                            case RegistrationWindowStatus.Closed:
                                closedWindows++;
                                var currentTime = DateTime.Now.ToString("HH:mm");
                                LogDebug($"Registration window not open yet (current time: {currentTime}, window: {stuTime.TidsromTilstedevaerelse} = CLOSED)");
                                break;
                        }
                    }

                    // Check if we should stop the automation
                    if (stuTimes.Any() && allSessionsComplete)
                    {
                        LogSuccess($"🎉 All {stuTimes.Count} STU sessions are complete for today!");
                        LogInfo("All registration windows have closed - stopping automation");
                        break;
                    }

                    // If no STU sessions found for today, check if it's late in the day
                    if (!stuTimes.Any())
                    {
                        var currentHour = DateTime.Now.Hour;
                        if (currentHour >= 16) // After 4 PM
                        {
                            LogInfo("No STU sessions found for today and it's after 4 PM - likely no more sessions");
                            LogInfo("Stopping automation for today");
                            break;
                        }
                        else
                        {
                            LogInfo("No STU sessions found yet - continuing to monitor");
                        }
                    }

                    // Log summary of current state
                    if (stuTimes.Any())
                    {
                        LogDebug($"Session status: {openWindows} open, {closedWindows} closed, {stuTimes.Count - openWindows - closedWindows} not yet open");
                    }

                    // Wait 2 minutes before next check
                    LogInfo("Waiting 2 minutes before next check...");
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Monitoring error: {ex.Message}");
                    LogInfo("Waiting 1 minute before retry...");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }
        }

        // Add this enum and method to support the enhanced logic
        private enum RegistrationWindowStatus
        {
            NotYetOpen,
            Open,
            Closed
        }

        private RegistrationWindowStatus GetRegistrationWindowStatus(ScheduleItem stuTime)
        {
            if (stuTime.TidsromTilstedevaerelse == null)
            {
                LogDebug($"No registration time window defined for session {stuTime.StartKl}-{stuTime.SluttKl}");
                return RegistrationWindowStatus.Closed;
            }

            // Parse the registration time window (e.g., "08:15 - 08:30")
            var parts = stuTime.TidsromTilstedevaerelse.Split(" - ");
            if (parts.Length != 2)
            {
                LogDebug($"Invalid time window format: {stuTime.TidsromTilstedevaerelse}");
                return RegistrationWindowStatus.Closed;
            }

            if (!TimeSpan.TryParse(parts[0], out var startTime) ||
                !TimeSpan.TryParse(parts[1], out var endTime))
            {
                LogDebug($"Could not parse time window: {stuTime.TidsromTilstedevaerelse}");
                return RegistrationWindowStatus.Closed;
            }

            var now = DateTime.Now.TimeOfDay;

            if (now < startTime)
            {
                return RegistrationWindowStatus.NotYetOpen;
            }
            else if (now >= startTime && now <= endTime)
            {
                return RegistrationWindowStatus.Open;
            }
            else
            {
                return RegistrationWindowStatus.Closed;
            }
        }

        private bool ShouldRegisterNow(ScheduleItem stuTime)
        {
            if (stuTime.TidsromTilstedevaerelse == null)
            {
                LogDebug($"No registration time window defined for session {stuTime.StartKl}-{stuTime.SluttKl}");
                return false;
            }

            // Parse the registration time window (e.g., "08:15 - 08:30")
            var parts = stuTime.TidsromTilstedevaerelse.Split(" - ");
            if (parts.Length != 2)
            {
                LogDebug($"Invalid time window format: {stuTime.TidsromTilstedevaerelse}");
                return false;
            }

            if (!TimeSpan.TryParse(parts[0], out var startTime) ||
                !TimeSpan.TryParse(parts[1], out var endTime))
            {
                LogDebug($"Could not parse time window: {stuTime.TidsromTilstedevaerelse}");
                return false;
            }

            var now = DateTime.Now.TimeOfDay;
            bool isInWindow = now >= startTime && now <= endTime;

            if (ShowDetailedLogs)
            {
                LogDebug($"Time check: {now:hh\\:mm} vs window {startTime:hh\\:mm}-{endTime:hh\\:mm} = {(isInWindow ? "OPEN" : "CLOSED")}");
            }

            return isInWindow;
        }

        private async Task<ScheduleResponse> GetScheduleDataAsync(Dictionary<string, string> cookies)
        {
            try
            {
                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += "?finder=RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312&onlyData=true&limit=99&offset=0&totalResults=true";

                LogDebug($"Making request to: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add headers
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                // Add cookies
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                LogDebug("Sending HTTP request...");
                var response = await _httpClient.SendAsync(request);

                LogDebug($"Response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    LogError($"HTTP request failed with status {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"Error response: {errorContent}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                LogDebug($"Response received ({json.Length} characters)");

                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug("JSON deserialized successfully");
                return scheduleResponse;
            }
            catch (Exception ex)
            {
                LogError($"Error getting schedule data: {ex.Message}");
                return null;
            }
        }

        private async Task RegisterAttendanceAsync(ScheduleItem stuTime, Dictionary<string, string> cookies)
        {
            try
            {
                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

                var publicIp = await GetPublicIpAsync();
                LogDebug($"Using public IP: {publicIp}");

                var payload = new
                {
                    name = "lagre_oppmote",
                    parameters = new object[]
                    {
                        new { fylkeid = "00" },
                        new { skoleid = "312" },
                        new { planperi = "2025-26" },
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
                LogDebug($"Registration payload: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

                // Add headers
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Origin", "https://iskole.net");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                // Add cookies
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                LogDebug("Sending registration request...");
                var response = await _httpClient.SendAsync(request);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    LogDebug($"Registration response: {responseContent}");
                }
                else
                {
                    LogError($"Registration failed with status {response.StatusCode}");
                    LogDebug($"Error response: {responseContent}");
                    throw new Exception($"Registration failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Registration error: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                LogDebug("Fetching public IP address...");
                var ip = await _httpClient.GetStringAsync("https://api.ipify.org");
                LogDebug($"Public IP: {ip}");
                return ip;
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to get public IP: {ex.Message}, using fallback");
                return "127.0.0.1";
            }
        }

        public void Dispose()
        {
            LogInfo("Disposing resources...");
            _cancellationTokenSource?.Cancel();
            _webDriver?.Quit();
            _webDriver?.Dispose();
            _httpClient?.Dispose();
        }
    }

    public class Cookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem> Items { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string Fag { get; set; }
        public string Stkode { get; set; }
        public string KlTrinn { get; set; }
        public string KlId { get; set; }
        public string KNavn { get; set; }
        public string GruppeNr { get; set; }
        public string Dato { get; set; }
        public string StartKl { get; set; }
        public string SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string Typefravaer { get; set; }
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string TidsromTilstedevaerelse { get; set; }
        public int Timenr { get; set; }
    }
}