using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

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
            _canExecute = canExecute ?? (() => true); // Default to always enabled
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            // Always return true - let the UI binding handle the enabled state
            return !_isExecuting && _canExecute();
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
            _isAutomationRunning = false;

            // Initialize commands - simplified interface
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
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

                // Force command state updates
                ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsAutomationRunning = isRunning;

                    // Force command state updates
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                });
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

            UpdateAutomationState(true);
            UpdateStatus("Initializing automation...");

            try
            {
                // Step 1: Check if we have valid cookies
                UpdateStatus("[1/3] Checking authentication cookies...");
                var existingCookies = await LoadExistingCookiesAsync();

                if (existingCookies == null)
                {
                    UpdateStatus("[1/3] No valid cookies found. Starting browser login process...");

                    // Automatically extract cookies through browser login
                    try
                    {
                        await LoginAndExtractCookiesAsync();

                        // Verify cookies were extracted successfully
                        var newCookies = await LoadExistingCookiesAsync();
                        if (newCookies == null)
                        {
                            UpdateStatus("FAILED: Cookie extraction unsuccessful. Cannot start automation without authentication.");
                            UpdateAutomationState(false);
                            return;
                        }

                        UpdateStatus("[1/3] SUCCESS: Authentication cookies obtained!");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"FAILED: Cookie extraction failed - {ex.Message}");
                        UpdateStatus("Cannot start automation without valid authentication cookies.");
                        UpdateAutomationState(false);
                        return;
                    }
                }
                else
                {
                    UpdateStatus("[1/3] Valid authentication cookies found!");
                }

                // Step 2: Test cookie validity with a quick API call
                UpdateStatus("[2/3] Validating authentication with server...");
                try
                {
                    var testSchedule = await GetScheduleDataAsync(CancellationToken.None);
                    if (testSchedule?.Items == null)
                    {
                        UpdateStatus("[2/3] Authentication test failed. Refreshing cookies...");

                        // Cookies might be expired, try to get fresh ones
                        await LoginAndExtractCookiesAsync();
                        var refreshedCookies = await LoadExistingCookiesAsync();

                        if (refreshedCookies == null)
                        {
                            UpdateStatus("FAILED: Unable to refresh authentication. Automation cannot start.");
                            UpdateAutomationState(false);
                            return;
                        }

                        // Test again with fresh cookies
                        testSchedule = await GetScheduleDataAsync(CancellationToken.None);
                        if (testSchedule?.Items == null)
                        {
                            UpdateStatus("FAILED: Authentication still invalid after refresh. Please check your login credentials.");
                            UpdateAutomationState(false);
                            return;
                        }
                    }

                    UpdateStatus("[2/3] Authentication validated successfully!");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"[2/3] Authentication validation failed: {ex.Message}");
                    UpdateStatus("Attempting to refresh authentication...");

                    try
                    {
                        await LoginAndExtractCookiesAsync();
                        UpdateStatus("[2/3] Authentication refreshed. Proceeding with automation...");
                    }
                    catch (Exception refreshEx)
                    {
                        UpdateStatus($"FAILED: Cannot refresh authentication - {refreshEx.Message}");
                        UpdateAutomationState(false);
                        return;
                    }
                }

                // Step 3: Start the main automation loop
                UpdateStatus("[3/3] Starting automated attendance monitoring...");
                _cancellationTokenSource = new CancellationTokenSource();

                await Task.Run(() => RunAutomationLoop(_cancellationTokenSource.Token));
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Automation stopped by user");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Automation startup error: {ex.Message}");
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
            try
            {
                // Create and show the settings window
                var settingsWindow = new AkademiTrack.Views.SettingsWindow();

                // If you want it to be modal (blocks interaction with main window)
                await settingsWindow.ShowDialog(Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null);

                // If you want it to be non-modal (both windows can be used simultaneously)
                // settingsWindow.Show();

                UpdateStatus("Settings window opened");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening settings: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task RunAutomationLoop(CancellationToken cancellationToken)
        {
            var loopCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    loopCount++;
                    UpdateStatus($"[Loop #{loopCount}] Starting automation cycle at {DateTime.Now:HH:mm:ss}");

                    // Load cookies first
                    UpdateStatus("[1/4] Loading authentication cookies...");
                    var cookies = await LoadCookiesFromFileAsync();

                    if (cookies == null || !cookies.Any())
                    {
                        UpdateStatus("ERROR: No valid cookies found. Use 'Login & Extract' to get fresh cookies.");
                        UpdateStatus("STOPPING AUTOMATION: No authentication - cannot proceed without cookies");
                        UpdateAutomationState(false);
                        return;
                    }

                    var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                    var foundCookies = requiredCookies.Where(rc => cookies.ContainsKey(rc)).ToArray();
                    UpdateStatus($"[1/4] Cookies loaded: {foundCookies.Length}/{requiredCookies.Length} required cookies found");

                    // Fetch schedule data
                    UpdateStatus("[2/4] Fetching schedule data from server...");
                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    // Check if schedule data is null or has no items
                    if (scheduleData?.Items == null || !scheduleData.Items.Any())
                    {
                        UpdateStatus("[2/4] No schedule data found or empty response from server");
                        UpdateStatus("STOPPING AUTOMATION: No classes found in schedule - nothing to monitor");
                        UpdateAutomationState(false);
                        return;
                    }

                    UpdateStatus($"[2/4] SUCCESS: Found {scheduleData.Items.Count} schedule items");

                    // Check if there are any relevant classes (today or future)
                    var now = DateTime.Now;
                    var today = now.Date;
                    var relevantClasses = scheduleData.Items
                        .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                      date >= today.AddDays(-1)) // Include yesterday's classes that might still be in attendance window
                        .ToList();

                    if (!relevantClasses.Any())
                    {
                        UpdateStatus("[2/4] No current or future classes found in schedule data");
                        UpdateStatus("STOPPING AUTOMATION: No classes to monitor - all found classes are from the past");
                        UpdateAutomationState(false);
                        return;
                    }

                    // Check for classes that are actually actionable (within attendance window)
                    var actionableClasses = relevantClasses
                        .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                      TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                                      date.Add(time) > now.AddMinutes(-45)) // Still within attendance window
                        .ToList();

                    if (!actionableClasses.Any())
                    {
                        UpdateStatus($"[2/4] Found {relevantClasses.Count} classes but none are actionable");
                        UpdateStatus("STOPPING AUTOMATION: No classes within attendance window (45min before to 15min after class time)");

                        // Show when the next classes are
                        var futureClasses = relevantClasses
                            .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                          TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                                          date.Add(time) > now)
                            .OrderBy(item => DateTime.ParseExact(item.Dato, "yyyyMMdd", null).Add(TimeSpan.ParseExact(item.StartKl, "HHmm", null)))
                            .Take(3)
                            .ToList();

                        if (futureClasses.Any())
                        {
                            UpdateStatus("Next upcoming classes:");
                            foreach (var cls in futureClasses)
                            {
                                var timeInfo = GetTimeInfo(cls, now);
                                UpdateStatus($"  - {cls.Fag} at {cls.StartKl} ({timeInfo})");
                            }
                            UpdateStatus("Start the automation closer to class time for attendance marking.");
                        }

                        UpdateAutomationState(false);
                        return;
                    }

                    // Show details of actionable classes
                    var upcomingClasses = new List<string>();
                    var currentClasses = new List<string>();
                    var pastClasses = new List<string>();

                    foreach (var item in actionableClasses)
                    {
                        var timeInfo = GetTimeInfo(item, now);
                        var displayText = $"{item.Fag} at {item.StartKl} ({timeInfo})";

                        if (timeInfo.Contains("minutes ago"))
                            pastClasses.Add(displayText);
                        else if (timeInfo.Contains("NOW") || timeInfo.Contains("in "))
                            currentClasses.Add(displayText);
                        else
                            upcomingClasses.Add(displayText);
                    }

                    UpdateStatus($"[2/4] Found {actionableClasses.Count} actionable classes:");
                    if (currentClasses.Any())
                    {
                        UpdateStatus($"  CURRENT/SOON: {string.Join(", ", currentClasses)}");
                    }
                    if (upcomingClasses.Any())
                    {
                        UpdateStatus($"  UPCOMING: {string.Join(", ", upcomingClasses.Take(2))}");
                    }
                    if (pastClasses.Any())
                    {
                        UpdateStatus($"  PAST: {pastClasses.Count} completed classes");
                    }

                    // Process attendance
                    UpdateStatus("[3/4] Processing attendance requirements...");
                    var attendanceMarked = 0;
                    var attendanceSkipped = 0;
                    var attendanceErrors = 0;

                    foreach (var item in scheduleData.Items)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        if (ShouldMarkAttendance(item))
                        {
                            try
                            {
                                UpdateStatus($"[3/4] Marking attendance for {item.Fag} at {item.StartKl}...");
                                await MarkAttendanceAsync(item, cancellationToken);
                                attendanceMarked++;
                                UpdateStatus($"[3/4] SUCCESS: Attendance marked for {item.Fag}");
                                await Task.Delay(2000, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                attendanceErrors++;
                                UpdateStatus($"[3/4] ERROR marking attendance for {item.Fag}: {ex.Message}");
                            }
                        }
                        else
                        {
                            attendanceSkipped++;
                        }
                    }

                    UpdateStatus($"[3/4] Summary: {attendanceMarked} marked | {attendanceSkipped} skipped | {attendanceErrors} errors");

                    // Analyze what to do next
                    var nextActionable = GetNextActionableClass(scheduleData.Items, now);
                    if (nextActionable != null)
                    {
                        UpdateStatus($"[3/4] Next action: {nextActionable}");
                    }

                    // Determine wait strategy based on what's happening
                    var waitTime = DetermineWaitTime(scheduleData.Items, now, attendanceMarked, out string waitReason);
                    UpdateStatus($"[4/4] {waitReason}");
                    await DelayWithCountdown(waitTime, "Next check", cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Automation cancelled by user");
                    throw;
                }
                catch (HttpRequestException httpEx)
                {
                    UpdateStatus($"NETWORK ERROR: {httpEx.Message}");
                    UpdateStatus("REASON FOR 30SEC WAIT: Network connection failed - quick retry");
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Network retry", cancellationToken);
                }
                catch (JsonException jsonEx)
                {
                    UpdateStatus($"JSON PARSE ERROR: {jsonEx.Message}");
                    UpdateStatus("REASON FOR 30SEC WAIT: Server returned invalid data - quick retry");
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Parse retry", cancellationToken);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"UNEXPECTED ERROR: {ex.Message}");
                    UpdateStatus("REASON FOR 30SEC WAIT: Unknown error occurred - quick retry");
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Error retry", cancellationToken);
                }
            }
        }

        private async Task DelayWithCountdown(TimeSpan delay, string actionName, CancellationToken cancellationToken)
        {
            var endTime = DateTime.Now.Add(delay);
            var updateInterval = TimeSpan.FromSeconds(delay.TotalSeconds > 60 ? 30 : 10);

            while (DateTime.Now < endTime && !cancellationToken.IsCancellationRequested)
            {
                var remaining = endTime - DateTime.Now;

                if (remaining.TotalSeconds > 60)
                {
                    UpdateStatus($"Timer: {actionName} in {remaining.Minutes}m {remaining.Seconds}s (at {endTime:HH:mm:ss})");
                }
                else
                {
                    UpdateStatus($"Timer: {actionName} in {remaining.Seconds}s");
                }

                var waitTime = remaining < updateInterval ? remaining : updateInterval;
                await Task.Delay(waitTime, cancellationToken);
            }
        }

        private string GetTimeInfo(ScheduleItem item, DateTime now)
        {
            try
            {
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate))
                {
                    return "Invalid date";
                }

                if (!TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    return "Invalid time";
                }

                var classDateTime = itemDate.Add(startTime);
                var timeDiff = classDateTime - now;

                if (Math.Abs(timeDiff.TotalMinutes) < 15)
                {
                    return "NOW";
                }
                else if (timeDiff.TotalMinutes < 0)
                {
                    return $"{Math.Abs(timeDiff.TotalMinutes):F0} minutes ago";
                }
                else if (timeDiff.TotalHours < 24)
                {
                    return $"in {timeDiff.TotalHours:F0}h {timeDiff.Minutes}m";
                }
                else
                {
                    return $"in {timeDiff.Days}d";
                }
            }
            catch
            {
                return "Time error";
            }
        }

        private TimeSpan DetermineWaitTime(List<ScheduleItem> items, DateTime now, int attendanceMarked, out string reason)
        {
            try
            {
                var upcomingClasses = items
                    .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                  TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time))
                    .Select(item => new
                    {
                        Item = item,
                        DateTime = DateTime.ParseExact(item.Dato, "yyyyMMdd", null).Add(TimeSpan.ParseExact(item.StartKl, "HHmm", null))
                    })
                    .Where(x => x.DateTime > now.AddMinutes(-45))
                    .OrderBy(x => x.DateTime)
                    .ToList();

                if (!upcomingClasses.Any())
                {
                    reason = "REASON FOR 5MIN WAIT: No upcoming classes in attendance window - checking for new schedule data";
                    return TimeSpan.FromMinutes(5);
                }

                var nextClass = upcomingClasses.First();
                var timeUntilClass = nextClass.DateTime - now;
                var attendanceWindowStart = nextClass.DateTime.AddMinutes(-15);
                var timeUntilWindow = attendanceWindowStart - now;

                if (timeUntilWindow.TotalMinutes <= 2)
                {
                    reason = $"REASON FOR 2MIN WAIT: {nextClass.Item.Fag} attendance window opens in {timeUntilWindow.TotalMinutes:F0} minutes - checking frequently";
                    return TimeSpan.FromMinutes(2);
                }
                else if (timeUntilWindow.TotalMinutes <= 10)
                {
                    reason = $"REASON FOR 3MIN WAIT: {nextClass.Item.Fag} at {nextClass.Item.StartKl} - attendance window opens in {timeUntilWindow.TotalMinutes:F0} minutes";
                    return TimeSpan.FromMinutes(3);
                }
                else if (timeUntilWindow.TotalMinutes <= 30)
                {
                    reason = $"REASON FOR 5MIN WAIT: {nextClass.Item.Fag} at {nextClass.Item.StartKl} - attendance opens in {timeUntilWindow.TotalMinutes:F0} minutes";
                    return TimeSpan.FromMinutes(5);
                }
                else if (timeUntilWindow.TotalHours <= 2)
                {
                    reason = $"REASON FOR 15MIN WAIT: Next class ({nextClass.Item.Fag}) is in {timeUntilWindow.TotalHours:F1} hours - no immediate action needed";
                    return TimeSpan.FromMinutes(15);
                }
                else
                {
                    reason = $"REASON FOR 30MIN WAIT: Next class ({nextClass.Item.Fag}) is {timeUntilWindow.TotalHours:F1} hours away - long interval check";
                    return TimeSpan.FromMinutes(30);
                }
            }
            catch (Exception ex)
            {
                reason = $"REASON FOR 5MIN WAIT: Error calculating optimal wait time ({ex.Message}) - using default interval";
                return TimeSpan.FromMinutes(5);
            }
        }

        private string GetNextActionableClass(List<ScheduleItem> items, DateTime now)
        {
            try
            {
                var nextClass = items
                    .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                  TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                                  date.Add(time) > now.AddMinutes(-45))
                    .OrderBy(item => DateTime.ParseExact(item.Dato, "yyyyMMdd", null).Add(TimeSpan.ParseExact(item.StartKl, "HHmm", null)))
                    .FirstOrDefault();

                if (nextClass != null)
                {
                    var timeInfo = GetTimeInfo(nextClass, now);
                    return $"{nextClass.Fag} at {nextClass.StartKl} ({timeInfo})";
                }

                return "No upcoming classes in attendance window";
            }
            catch
            {
                return "Error calculating next class";
            }
        }

        private async Task<ScheduleResponse> GetScheduleDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                UpdateStatus("Loading cookies for API request...");
                var cookies = await LoadCookiesFromFileAsync();
                if (cookies == null || !cookies.Any())
                {
                    throw new Exception("Failed to load cookies from cookies.json. Use 'Login & Extract' button to get fresh cookies.");
                }

                UpdateStatus($"Building API request URL...");
                var baseUrl = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote";
                var finder = $"RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312";
                var url = $"{baseUrl}?finder={Uri.EscapeDataString(finder)}&onlyData=true&limit=99&offset=0&totalResults=true";

                UpdateStatus($"API URL: {url}");

                // Create HttpClient with automatic decompression
                using var clientHandler = new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                };

                using var httpClient = new HttpClient(clientHandler);

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
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");
                request.Headers.Add("Priority", "u=4, i");
                request.Headers.Add("Connection", "keep-alive");

                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                UpdateStatus($"Sending GET request to server...");
                var response = await httpClient.SendAsync(request, cancellationToken);

                UpdateStatus($"Server response: HTTP {(int)response.StatusCode} {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();

                    UpdateStatus($"Raw response length: {jsonString.Length} characters");
                    UpdateStatus($"Response starts with: {jsonString.Substring(0, Math.Min(100, jsonString.Length))}");

                    if (string.IsNullOrWhiteSpace(jsonString))
                    {
                        UpdateStatus("ERROR: Server returned empty response");
                        return new ScheduleResponse { Items = new List<ScheduleItem>() };
                    }

                    try
                    {
                        var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(jsonString, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        UpdateStatus($"Parsed {scheduleResponse?.Items?.Count ?? 0} schedule items successfully");
                        return scheduleResponse ?? new ScheduleResponse { Items = new List<ScheduleItem>() };
                    }
                    catch (JsonException ex)
                    {
                        UpdateStatus($"JSON parsing failed: {ex.Message}");
                        UpdateStatus($"Problematic JSON: {jsonString}");
                        throw;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    UpdateStatus($"Server error response: {errorContent}");

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new Exception("Authentication failed - cookies may be expired. Use 'Login & Extract' to get fresh cookies.");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        throw new Exception("Access forbidden - insufficient permissions or expired session.");
                    }
                    else
                    {
                        throw new Exception($"Server returned error {(int)response.StatusCode}: {response.StatusCode}. Response: {errorContent}");
                    }
                }
            }
            catch (HttpRequestException httpEx)
            {
                UpdateStatus($"Network error: {httpEx.Message}");
                throw new Exception($"Network error connecting to server: {httpEx.Message}", httpEx);
            }
            catch (TaskCanceledException)
            {
                UpdateStatus("Request timed out");
                throw new Exception("Request timed out - server may be slow or unreachable");
            }
            catch (JsonException ex)
            {
                UpdateStatus($"JSON error: {ex.Message}");
                throw new Exception($"Failed to parse JSON response: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Unexpected error in GetScheduleDataAsync: {ex.Message}");
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