using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager;
using System.Text.RegularExpressions;

namespace AkademiTrack.ViewModels
{
    public class SimpleCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

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

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public partial class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isAutomationRunning;
        private string _statusMessage;
        private IWebDriver _webDriver;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {
            _httpClient = new HttpClient();
            _statusMessage = "Ready to start automation";

            // Initialize commands
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync, () => !IsAutomationRunning);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync, () => IsAutomationRunning);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            GetCookiesCommand = new SimpleCommand(GetFreshCookiesAsync);
            LoginAndExtractCommand = new SimpleCommand(LoginAndExtractCookiesAsync);
        }

        public string Greeting { get; } = "AkademiTrack Automation System";

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

                    // Update command states
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        // Commands - All declared here
        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand GetCookiesCommand { get; }
        public ICommand LoginAndExtractCommand { get; }

        // Thread-safe UI update methods
        private void UpdateStatus(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                StatusMessage = message;
            }
            else
            {
                Dispatcher.UIThread.Post(() => StatusMessage = message);
            }
        }

        private void UpdateAutomationState(bool isRunning)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                IsAutomationRunning = isRunning;
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsAutomationRunning = isRunning);
            }
        }

        private async Task<string> GetChromeVersionAsync()
        {
            try
            {
                // Try to get Chrome version from registry on Windows
                if (OperatingSystem.IsWindows())
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "reg",
                            Arguments = @"query ""HKEY_CURRENT_USER\Software\Google\Chrome\BLBeacon"" /v version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    process.WaitForExit();

                    var match = Regex.Match(output, @"version\s+REG_SZ\s+(.+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }

                // Fallback: Try to execute Chrome with --version
                var chromeExecutables = new[]
                {
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/usr/bin/google-chrome",
                    "/usr/bin/chromium-browser"
                };

                foreach (var chromePath in chromeExecutables)
                {
                    if (File.Exists(chromePath))
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = chromePath,
                                Arguments = "--version",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        var output = await process.StandardOutput.ReadToEndAsync();
                        process.WaitForExit();

                        var match = Regex.Match(output, @"(\d+\.\d+\.\d+\.\d+)");
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Could not detect Chrome version: {ex.Message}");
            }

            return null;
        }

        private async Task SetupChromeDriverAsync()
        {
            try
            {
                UpdateStatus("Setting up ChromeDriver...");

                // Simple approach - just use the latest available ChromeDriver
                // This usually works well as Chrome auto-updates and ChromeDriver is backward compatible
                try
                {
                    var driverManager = new DriverManager();
                    var chromeConfig = new ChromeConfig();
                    driverManager.SetUpDriver(chromeConfig);
                    UpdateStatus("ChromeDriver setup successful");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"WebDriverManager failed: {ex.Message}");

                    // Alternative approach: Check if chromedriver.exe exists in the application directory
                    var localDriverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chromedriver.exe");
                    if (File.Exists(localDriverPath))
                    {
                        UpdateStatus("Using local chromedriver.exe");
                        return;
                    }

                    // If all else fails, provide clear instructions
                    var chromeVersion = await GetChromeVersionAsync();
                    var majorVersion = !string.IsNullOrEmpty(chromeVersion) ? chromeVersion.Split('.')[0] : "latest";

                    throw new Exception(
                        $"ChromeDriver setup failed. Manual setup required:\n\n" +
                        $"1. Go to https://chromedriver.chromium.org/downloads\n" +
                        $"2. Download ChromeDriver for Chrome {majorVersion}\n" +
                        $"3. Extract chromedriver.exe to your application folder\n" +
                        $"4. Try again\n\n" +
                        $"Your Chrome version: {chromeVersion ?? "unknown"}\n" +
                        $"Error: {ex.Message}"
                    );
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"ChromeDriver setup failed: {ex.Message}", ex);
            }
        }

        

        private async Task LoginAndExtractCookiesAsync()
        {
            try
            {
                UpdateStatus("Setting up Chrome WebDriver...");

                // Setup ChromeDriver - simplified approach
                await SetupChromeDriverAsync();

                // Set up Chrome options
                var chromeOptions = new ChromeOptions();
                chromeOptions.AddArgument("--no-sandbox");
                chromeOptions.AddArgument("--disable-dev-shm-usage");
                chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");
                chromeOptions.AddExcludedArgument("enable-automation");
                chromeOptions.AddAdditionalOption("useAutomationExtension", false);

                // Add user agent to appear more like a regular browser
                chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

                try
                {
                    // Create WebDriver instance
                    _webDriver = new ChromeDriver(chromeOptions);

                    // Execute script to hide webdriver property
                    var script = "Object.defineProperty(navigator, 'webdriver', {get: () => undefined})";
                    ((IJavaScriptExecutor)_webDriver).ExecuteScript(script);

                    UpdateStatus("Browser started successfully. Opening login page...");

                    // Navigate to login page
                    _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                    // Wait a moment for page to load
                    await Task.Delay(2000);

                    UpdateStatus("Login page loaded. Please complete the login process in the browser...\n" +
                               "After successful login, the system will automatically detect and extract cookies.\n" +
                               "Do not close the browser window.");

                    // Wait for login to complete by checking for specific elements or URL changes
                    var loginSuccessful = await WaitForLoginCompletionAsync();

                    if (loginSuccessful)
                    {
                        // Navigate to the required page to ensure session is active
                        UpdateStatus("Login detected! Navigating to attendance page...");
                        _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");
                        await Task.Delay(3000); // Give it more time to load

                        UpdateStatus("Extracting cookies from browser session...");

                        // Extract cookies
                        var cookies = _webDriver.Manage().Cookies.AllCookies.ToList();

                        if (cookies.Any())
                        {
                            await SaveCookiesToFileAsync(cookies);
                            UpdateStatus($"Successfully extracted and saved {cookies.Count} cookies to cookies.json");

                            // Verify we have the required cookies
                            var cookieDict = cookies.ToDictionary(c => c.Name, c => c.Value);
                            var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                            var foundCookies = requiredCookies.Where(rc => cookieDict.ContainsKey(rc)).ToArray();

                            if (foundCookies.Length > 0)
                            {
                                UpdateStatus($"SUCCESS: Required cookies found: {string.Join(", ", foundCookies)}\n" +
                                           "Cookie extraction completed successfully! You can now start the automation.");
                            }
                            else
                            {
                                UpdateStatus("WARNING: Required cookies (JSESSIONID, _WL_AUTHCOOKIE_JSESSIONID) not found.\n" +
                                           "Please ensure you are fully logged in and try again.");
                            }
                        }
                        else
                        {
                            UpdateStatus("No cookies found. Please ensure you are properly logged in and try again.");
                        }
                    }
                    else
                    {
                        UpdateStatus("Login timeout or failed. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("session not created") || ex.Message.Contains("version"))
                    {
                        UpdateStatus($"ChromeDriver version mismatch error: {ex.Message}\n\n" +
                                   "Solutions:\n" +
                                   "1. Update Google Chrome to the latest version\n" +
                                   "2. Or download the correct ChromeDriver manually from https://chromedriver.chromium.org/\n" +
                                   "3. Place chromedriver.exe in your application folder");
                    }
                    else
                    {
                        UpdateStatus($"WebDriver error: {ex.Message}");
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error during cookie extraction: {ex.Message}");
            }
            finally
            {
                // Clean up WebDriver
                try
                {
                    if (_webDriver != null)
                    {
                        await Task.Delay(2000); // Give user a moment to see the result
                        UpdateStatus("Closing browser...");
                        _webDriver.Quit();
                        _webDriver.Dispose();
                        _webDriver = null;
                        UpdateStatus("Browser closed. Cookie extraction process completed.");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error closing browser: {ex.Message}");
                }
            }
        }

        private async Task<bool> WaitForLoginCompletionAsync()
        {
            // Wait for login completion by checking for URL change or specific elements
            var maxWaitTime = TimeSpan.FromMinutes(10); // Maximum wait time
            var checkInterval = TimeSpan.FromSeconds(3);
            var startTime = DateTime.Now;

            UpdateStatus("Waiting for login completion... (timeout: 10 minutes)");

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    var currentUrl = _webDriver.Url;
                    UpdateStatus($"Checking login status... Current URL: {currentUrl.Substring(0, Math.Min(50, currentUrl.Length))}...");

                    // Check if we're on a page that indicates successful login
                    if (currentUrl.Contains("isFeideinnlogget=true") ||
                        (currentUrl.Contains("/elev/") && !currentUrl.Contains("login")))
                    {
                        UpdateStatus("Login successful! URL indicates logged in state.");
                        return true;
                    }

                    // Check for elements that indicate we're logged in
                    try
                    {
                        // Look for common elements that appear after login
                        var possibleSelectors = new[]
                        {
                            "[data-logged-in]",
                            ".user-menu",
                            ".logout",
                            "a[href*='logout']",
                            ".nav-user",
                            ".user-info",
                            "[href*='fravar']",
                            "[href*='timeplan']"
                        };

                        foreach (var selector in possibleSelectors)
                        {
                            var elements = _webDriver.FindElements(By.CssSelector(selector));
                            if (elements.Any())
                            {
                                UpdateStatus($"Login detected! Found element: {selector}");
                                return true;
                            }
                        }

                        // Check for absence of login form
                        var loginElements = _webDriver.FindElements(By.CssSelector("input[type='password'], .login-form, #login"));
                        if (!loginElements.Any() && !currentUrl.Contains("login"))
                        {
                            UpdateStatus("Login appears successful - no login form detected.");
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore element search errors and continue
                    }

                    var remainingTime = maxWaitTime - (DateTime.Now - startTime);
                    UpdateStatus($"Still waiting for login... {remainingTime.Minutes}:{remainingTime.Seconds:00} remaining");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Checking login status... Error: {ex.Message}");
                }

                await Task.Delay(checkInterval);
            }

            // If we reach here, timeout occurred
            UpdateStatus("Login timeout reached. Attempting cookie extraction anyway...");
            return false; // Changed to false to indicate timeout
        }

        private async Task SaveCookiesToFileAsync(IList<OpenQA.Selenium.Cookie> seleniumCookies)
        {
            try
            {
                // Convert Selenium cookies to our Cookie format
                var cookieList = seleniumCookies.Select(c => new Cookie
                {
                    Name = c.Name,
                    Value = c.Value
                }).ToArray();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var jsonString = JsonSerializer.Serialize(cookieList, jsonOptions);
                await File.WriteAllTextAsync("cookies.json", jsonString);

                // Also save as cookie string format for debugging
                var cookieString = string.Join("; ", seleniumCookies.Select(c => $"{c.Name}={c.Value}"));
                await File.WriteAllTextAsync("cookies.txt", cookieString);

                // Create a summary file with cookie info
                var summaryLines = new List<string>
                {
                    $"Cookie extraction completed at: {DateTime.Now}",
                    $"Total cookies extracted: {seleniumCookies.Count}",
                    "",
                    "Cookie names:"
                };
                summaryLines.AddRange(seleniumCookies.Select(c => $"  - {c.Name} = {c.Value.Substring(0, Math.Min(20, c.Value.Length))}..."));

                await File.WriteAllLinesAsync("cookie_summary.txt", summaryLines);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save cookies: {ex.Message}", ex);
            }
        }

        private async Task<Dictionary<string, string>> LoadExistingCookiesAsync()
        {
            try
            {
                const string cookiesPath = "cookies.json";

                if (!File.Exists(cookiesPath))
                {
                    return null;
                }

                var jsonString = await File.ReadAllTextAsync(cookiesPath);
                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    return null;
                }

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (cookiesArray == null || !cookiesArray.Any())
                {
                    return null;
                }

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);

                // Validate required cookies
                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var invalidCookies = requiredCookies.Where(rc => !cookieDict.ContainsKey(rc) ||
                    cookieDict[rc].StartsWith("YOUR_") ||
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                if (invalidCookies.Any())
                {
                    return null; // Invalid cookies
                }

                return cookieDict;
            }
            catch
            {
                return null;
            }
        }

        private async Task GetFreshCookiesAsync()
        {
            // Check if we already have valid cookies
            var existingCookies = await LoadExistingCookiesAsync();

            if (existingCookies != null)
            {
                UpdateStatus("Valid cookies already exist. Use 'Login & Extract' to get fresh cookies if needed.");
                return;
            }

            await LoginAndExtractCookiesAsync();
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning)
                return;

            _cancellationTokenSource = new CancellationTokenSource();

            UpdateAutomationState(true);
            UpdateStatus("Starting automation...");

            try
            {
                await Task.Run(() => RunAutomationLoop(_cancellationTokenSource.Token));
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Automation stopped by user");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Automation error: {ex.Message}");
            }
            finally
            {
                UpdateAutomationState(false);
            }
        }

        private async Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                UpdateStatus("Stopping automation...");
            }
        }

        private async Task OpenSettingsAsync()
        {
            const string cookiesPath = "cookies.json";

            if (!File.Exists(cookiesPath))
            {
                await CreateSampleCookiesFileAsync(cookiesPath);
                UpdateStatus($"Created sample cookies.json at: {Path.GetFullPath(cookiesPath)}");
            }
            else
            {
                UpdateStatus($"Opening cookies file location: {Path.GetFullPath(cookiesPath)}");
            }

            try
            {
                var fullPath = Path.GetFullPath(cookiesPath);
                var directory = Path.GetDirectoryName(fullPath);

                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", directory);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", directory);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", directory);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Could not open folder: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task RunAutomationLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UpdateStatus("Fetching schedule data...");

                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    if (scheduleData?.Items != null && scheduleData.Items.Any())
                    {
                        UpdateStatus($"Found {scheduleData.Items.Count} schedule items");

                        foreach (var item in scheduleData.Items)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            if (ShouldMarkAttendance(item))
                            {
                                UpdateStatus($"Marking attendance for {item.Fag} at {item.StartKl}");
                                await MarkAttendanceAsync(item, cancellationToken);
                                UpdateStatus($"Successfully marked attendance for {item.Fag}");
                                await Task.Delay(2000, cancellationToken);
                            }
                        }

                        UpdateStatus("Completed processing all schedule items");
                    }
                    else
                    {
                        UpdateStatus("No schedule data found");
                    }

                    UpdateStatus("Waiting 5 minutes for next check...");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Automation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        private async Task<ScheduleResponse> GetScheduleDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                var cookies = await LoadCookiesFromFileAsync();
                if (cookies == null || !cookies.Any())
                {
                    throw new Exception("Failed to load cookies from cookies.json. Use 'Login & Extract' button to get fresh cookies.");
                }

                var startDate = DateTime.Now.ToString("yyyyMMdd");
                var endDate = DateTime.Now.AddDays(7).ToString("yyyyMMdd");

                var baseUrl = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev";
                var finder = $"RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312,startDate={startDate},endDate={endDate}";
                var url = $"{baseUrl}?finder={Uri.EscapeDataString(finder)}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Sec-Ch-Ua", "\"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"140\"");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
                request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
                request.Headers.Add("Sec-Fetch-Mode", "cors");
                request.Headers.Add("Sec-Fetch-Dest", "empty");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                request.Headers.Add("Priority", "u=1, i");
                request.Headers.Add("Connection", "keep-alive");

                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return scheduleResponse;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to fetch schedule data. Status: {response.StatusCode}, Response: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in GetScheduleDataAsync: {ex.Message}", ex);
            }
        }

        private async Task MarkAttendanceAsync(ScheduleItem item, CancellationToken cancellationToken)
        {
            var cookies = await LoadCookiesFromFileAsync();
            if (cookies == null)
            {
                throw new Exception("Failed to load cookies");
            }

            var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
            var authCookie = cookies.GetValueOrDefault("_WL_AUTHCOOKIE_JSESSIONID", "");
            var oracleRoute = cookies.GetValueOrDefault("X-Oracle-BMC-LBS-Route", "");

            var jsessionIdClean = jsessionId.Split('!')[0];
            var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

            var payload = new
            {
                name = "lagre_oppmote",
                parameters = new object[]
                {
                    new { fylkeid = "00" },
                    new { skoleid = "312" },
                    new { planperi = "2025-26" },
                    new { ansidato = item.Dato },
                    new { stkode = item.Stkode },
                    new { kl_trinn = item.KlTrinn },
                    new { kl_id = item.KlId },
                    new { k_navn = item.KNavn },
                    new { gruppe_nr = item.GruppeNr },
                    new { timenr = item.Timenr },
                    new { fravaerstype = "M" },
                    new { ip = await GetPublicIpAsync() }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            request.Headers.Add("Host", "iskole.net");
            request.Headers.Add("Cookie", $"X-Oracle-BMC-LBS-Route={oracleRoute}; JSESSIONID={jsessionIdClean}; _WL_AUTHCOOKIE_JSESSIONID={authCookie}");
            request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            request.Headers.Add("Accept-Language", "nb-NO,nb;q=0.9");
            request.Headers.Add("Sec-Ch-Ua", "\"Chromium\";v=\"140\", \"Not;A=Brand\";v=\"99\"");
            request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            request.Headers.Add("Origin", "https://iskole.net");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");
            request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            request.Headers.Add("Priority", "u=4, i");
            request.Headers.Add("Connection", "keep-alive");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to mark attendance. Status: {response.StatusCode}, Content: {errorContent}");
            }
        }

        private async Task<Dictionary<string, string>> LoadCookiesFromFileAsync()
        {
            try
            {
                const string cookiesPath = "cookies.json";

                if (!File.Exists(cookiesPath))
                {
                    await CreateSampleCookiesFileAsync(cookiesPath);
                    throw new FileNotFoundException($"Created cookies.json template at: {Path.GetFullPath(cookiesPath)}. Please use 'Login & Extract' button to get real cookies.");
                }

                var jsonString = await File.ReadAllTextAsync(cookiesPath);

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    throw new Exception("cookies.json file is empty");
                }

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (cookiesArray == null || !cookiesArray.Any())
                {
                    throw new Exception("No cookies found in cookies.json");
                }

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);

                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var invalidCookies = requiredCookies.Where(rc => !cookieDict.ContainsKey(rc) ||
                    cookieDict[rc].StartsWith("YOUR_") ||
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                if (invalidCookies.Any())
                {
                    throw new Exception($"Invalid cookies detected: {string.Join(", ", invalidCookies)}. Please use 'Login & Extract' button to get fresh cookies.");
                }

                return cookieDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Cookie error: {ex.Message}", ex);
            }
        }

        private async Task CreateSampleCookiesFileAsync(string cookiesPath)
        {
            var sampleCookies = new Cookie[]
            {
                new Cookie { Name = "JSESSIONID", Value = "YOUR_JSESSIONID_HERE" },
                new Cookie { Name = "_WL_AUTHCOOKIE_JSESSIONID", Value = "YOUR_WL_AUTHCOOKIE_JSESSIONID_HERE" },
                new Cookie { Name = "X-Oracle-BMC-LBS-Route", Value = "YOUR_ORACLE_BMC_LBS_ROUTE_HERE" }
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var jsonString = JsonSerializer.Serialize(sampleCookies, jsonOptions);
            await File.WriteAllTextAsync(cookiesPath, jsonString);
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://api.ipify.org");
                return response.Trim();
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        private bool ShouldMarkAttendance(ScheduleItem item)
        {
            try
            {
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate))
                {
                    return false;
                }

                if (!TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    return false;
                }

                var classDateTime = itemDate.Add(startTime);
                var now = DateTime.Now;
                var timeDiff = classDateTime - now;

                return timeDiff.TotalMinutes >= -45 && timeDiff.TotalMinutes <= 15;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
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
        public long Timenr { get; set; }
        public string StartKl { get; set; }
        public string SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string Typefravaer { get; set; }
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string TidsromTilstedevaerelse { get; set; }
    }
}