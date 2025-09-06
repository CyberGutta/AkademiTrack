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
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }

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
                ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsAutomationRunning = isRunning;
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                });
            }
        }

        private async Task<string> GetChromeVersionAsync()
        {
            try
            {
                // Try registry on Windows first
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
                        return match.Groups[1].Value.Trim();
                }

                // Try Chrome executable paths
                var chromeExecutables = GetChromeExecutablePaths();

                foreach (var chromePath in chromeExecutables.Where(File.Exists))
                {
                    var version = await GetVersionFromExecutable(chromePath);
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Could not detect Chrome version: {ex.Message}");
            }

            return null;
        }

        private static List<string> GetChromeExecutablePaths()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new List<string>
                {
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                };
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new List<string>
                {
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Applications/Chromium.app/Contents/MacOS/Chromium"
                };
            }
            
            return new List<string>
            {
                "/usr/bin/google-chrome",
                "/usr/bin/chromium-browser",
                "/usr/bin/google-chrome-stable"
            };
        }

        private static async Task<string> GetVersionFromExecutable(string chromePath)
        {
            try
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
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> DownloadChromeDriverAsync(string chromeVersion)
        {
            try
            {
                UpdateStatus($"Downloading ChromeDriver for Chrome {chromeVersion}...");

                var driverFileName = GetChromeDriverFileName();
                var driverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, driverFileName);
                var platformFolder = GetPlatformFolder();
                var chromeDriverUrl = GetChromeDriverUrl(chromeVersion, platformFolder);

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var zipBytes = await httpClient.GetByteArrayAsync(chromeDriverUrl);

                var tempZipPath = Path.Combine(Path.GetTempPath(), "chromedriver.zip");
                await File.WriteAllBytesAsync(tempZipPath, zipBytes);

                UpdateStatus("Extracting ChromeDriver...");
                await ExtractChromeDriver(tempZipPath, driverPath);

                UpdateStatus($"ChromeDriver downloaded successfully to {driverPath}!");
                File.Delete(tempZipPath);
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to download ChromeDriver: {ex.Message}");
                return false;
            }
        }

        private static string GetPlatformFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "win64";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var arch = RuntimeInformation.OSArchitecture;
                return arch == Architecture.Arm64 ? "mac-arm64" : "mac-x64";
            }
            
            return "linux64";
        }

        private static string GetChromeDriverUrl(string chromeVersion, string platformFolder)
        {
            var baseUrl = "https://storage.googleapis.com/chrome-for-testing-public";
            var version = chromeVersion.StartsWith("140.") ? "140.0.7339.81" : chromeVersion;
            return $"{baseUrl}/{version}/{platformFolder}/chromedriver-{platformFolder}.zip";
        }

        private static async Task ExtractChromeDriver(string tempZipPath, string driverPath)
        {
            using var archive = ZipFile.OpenRead(tempZipPath);
            var driverEntry = archive.Entries.FirstOrDefault(e => e.Name.StartsWith("chromedriver"));
            
            if (driverEntry == null)
                throw new Exception("chromedriver not found in downloaded zip");

            driverEntry.ExtractToFile(driverPath, overwrite: true);

            // Make executable on Unix systems
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var chmod = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{driverPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                chmod.Start();
                chmod.WaitForExit();
            }
        }

        private static string GetChromeDriverFileName()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "chromedriver.exe" : "chromedriver";
        }

        private async Task SetupChromeDriverAsync()
        {
            try
            {
                UpdateStatus("Setting up ChromeDriver...");

                var chromeVersion = await GetChromeVersionAsync();
                UpdateStatus($"Detected Chrome version: {chromeVersion ?? "Unknown"}");

                var driverFileName = GetChromeDriverFileName();
                var localDriverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, driverFileName);

                // Test local driver first
                if (File.Exists(localDriverPath) && await TestLocalDriver(localDriverPath))
                {
                    UpdateStatus("Local chromedriver is working correctly!");
                    return;
                }

                // Remove incompatible driver
                try { File.Delete(localDriverPath); } catch { }

                // Try to download correct version
                if (!string.IsNullOrEmpty(chromeVersion))
                {
                    var downloadSuccess = await DownloadChromeDriverAsync(chromeVersion);
                    if (downloadSuccess) return;
                }

                // Try WebDriverManager as fallback
                try
                {
                    UpdateStatus("Attempting automatic ChromeDriver download via WebDriverManager...");
                    var driverManager = new DriverManager();
                    var chromeConfig = new ChromeConfig();
                    driverManager.SetUpDriver(chromeConfig);
                    UpdateStatus("ChromeDriver downloaded successfully via WebDriverManager");
                    return;
                }
                catch (Exception ex)
                {
                    UpdateStatus($"WebDriverManager failed: {ex.Message}");
                }

                // Provide manual instructions
                var platformInstructions = GetPlatformSpecificInstructions(chromeVersion);
                UpdateStatus(platformInstructions);
                throw new Exception(platformInstructions);
            }
            catch (Exception ex)
            {
                UpdateStatus($"ChromeDriver setup completely failed: {ex.Message}");
                throw new Exception($"ChromeDriver setup failed: {ex.Message}", ex);
            }
        }

        private async Task<bool> TestLocalDriver(string localDriverPath)
        {
            try
            {
                UpdateStatus("Testing local chromedriver compatibility...");
                
                var testService = ChromeDriverService.CreateDefaultService(
                    AppDomain.CurrentDomain.BaseDirectory, 
                    GetChromeDriverFileName());
                    
                testService.HideCommandPromptWindow = true;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    testService.Port = GetAvailablePort();

                var testOptions = new ChromeOptions();
                testOptions.AddArguments("--headless", "--no-sandbox", "--disable-dev-shm-usage");
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    testOptions.AddArguments("--disable-gpu", "--remote-debugging-port=0");
                }

                using var testDriver = new ChromeDriver(testService, testOptions, TimeSpan.FromSeconds(30));
                testDriver.Navigate().GoToUrl("about:blank");
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Local chromedriver failed test: {ex.Message}");
                return false;
            }
        }

        private string GetPlatformSpecificInstructions(string chromeVersion)
        {
            var driverFileName = GetChromeDriverFileName();
            var currentFolder = AppDomain.CurrentDomain.BaseDirectory;
            var version = chromeVersion ?? "140.0.7339.81";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64";
                return $@"ChromeDriver setup failed. Manual setup required for macOS:

CHROME VERSION: {version}
ARCHITECTURE: {arch}

STEPS:
1. Download from: https://storage.googleapis.com/chrome-for-testing-public/140.0.7339.81/{arch}/chromedriver-{arch}.zip
2. Extract chromedriver to: {currentFolder}
3. Run: chmod +x ""{Path.Combine(currentFolder, "chromedriver")}""
4. Run: xattr -d com.apple.quarantine ""{Path.Combine(currentFolder, "chromedriver")}""
5. Restart application";
            }

            return $@"ChromeDriver setup failed. Manual setup required:

CHROME VERSION: {version}

STEPS:
1. Download from: https://googlechromelabs.github.io/chrome-for-testing/
2. Find ChromeDriver version {version}
3. Extract {driverFileName} to: {currentFolder}
4. Restart application";
        }

        private static int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task LoginAndExtractCookiesAsync()
        {
            try
            {
                UpdateStatus("Setting up Chrome WebDriver...");
                await SetupChromeDriverAsync();

                var chromeDriverService = CreateChromeDriverService();
                var chromeOptions = CreateChromeOptions();

                try
                {
                    UpdateStatus("Creating Chrome WebDriver instance...");
                    _webDriver = new ChromeDriver(chromeDriverService, chromeOptions, TimeSpan.FromSeconds(120));

                    // Test browser responsiveness
                    UpdateStatus("Testing browser responsiveness...");
                    var title = _webDriver.Title;

                    UpdateStatus("Chrome browser opened successfully!");
                    ConfigureWebDriverTimeouts();

                    UpdateStatus("Navigating to login page...");
                    _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");
                    await Task.Delay(8000);

                    var currentUrl = _webDriver.Url;
                    if (!currentUrl.Contains("login") && !currentUrl.Contains("iskole.net"))
                    {
                        throw new Exception($"Failed to navigate to login page. Current URL: {currentUrl}");
                    }

                    ShowLoginInstructions();

                    var loginSuccessful = await WaitForSpecificUrlAsync();
                    if (loginSuccessful)
                    {
                        await ExtractAndSaveCookies();
                    }
                    else
                    {
                        UpdateStatus("TIMEOUT: User did not reach the required page within the time limit.");
                    }
                }
                catch (WebDriverException webEx)
                {
                    HandleWebDriverException(webEx);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Cookie extraction failed: {ex.Message}");
                throw;
            }
            finally
            {
                await CleanupWebDriver();
            }
        }

        private ChromeDriverService CreateChromeDriverService()
        {
            var driverFileName = GetChromeDriverFileName();
            var localDriverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, driverFileName);

            var service = File.Exists(localDriverPath)
                ? ChromeDriverService.CreateDefaultService(AppDomain.CurrentDomain.BaseDirectory, driverFileName)
                : ChromeDriverService.CreateDefaultService();

            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                service.Port = GetAvailablePort();
                UpdateStatus($"Using port {service.Port} for ChromeDriver on macOS");
            }

            return service;
        }

        private static ChromeOptions CreateChromeOptions()
        {
            var options = new ChromeOptions();

            // Essential stability options
            options.AddArguments(
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--disable-blink-features=AutomationControlled",
                "--log-level=3",
                "--disable-extensions",
                "--no-first-run",
                "--disable-default-apps",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-search-engine-choice-screen",
                "--start-maximized"
            );

            // Platform-specific options
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                options.AddArguments(
                    "--disable-gpu",
                    "--remote-debugging-port=0",
                    "--disable-web-security",
                    "--allow-running-insecure-content",
                    "--disable-features=VizDisplayCompositor,MacV2GPUSandbox"
                );
                options.AddArgument("--user-agent=Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
            }
            else
            {
                options.AddArgument("--disable-gpu");
                options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
            }

            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);

            return options;
        }

        private void ConfigureWebDriverTimeouts()
        {
            _webDriver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(15);
            _webDriver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(45);
        }

        private void ShowLoginInstructions()
        {
            UpdateStatus("Login page loaded successfully!");
            UpdateStatus("=== INSTRUCTIONS ===");
            UpdateStatus("1. Complete the login process in the browser window");
            UpdateStatus("2. After successful login, navigate to:");
            UpdateStatus("   https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");
            UpdateStatus("3. The system will automatically detect when you reach this page");
            UpdateStatus("4. DO NOT close the browser window!");
            UpdateStatus("==================");
        }

        private void HandleWebDriverException(WebDriverException webEx)
        {
            if (webEx.Message.Contains("chrome not reachable"))
            {
                UpdateStatus("ERROR: Chrome browser was closed unexpectedly during startup.");
                UpdateStatus("TROUBLESHOOTING STEPS:");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    UpdateStatus("1. Try running: xattr -d com.apple.quarantine chromedriver");
                    UpdateStatus("2. Make sure chromedriver has execute permissions: chmod +x chromedriver");
                }
                else
                {
                    UpdateStatus("1. Ensure Chrome browser is installed and updated");
                    UpdateStatus("2. Close all Chrome instances and try again");
                }
                throw new Exception("Browser closed during startup - see troubleshooting steps above");
            }

            if (webEx.Message.Contains("session not created"))
            {
                UpdateStatus("ERROR: Could not create Chrome session - ChromeDriver version mismatch.");
                throw new Exception($"ChromeDriver version mismatch");
            }

            UpdateStatus($"WebDriver error: {webEx.Message}");
            throw new Exception($"WebDriver error: {webEx.Message}");
        }

        private async Task CleanupWebDriver()
        {
            try
            {
                if (_webDriver != null)
                {
                    UpdateStatus("Keeping browser open for 10 seconds...");
                    await Task.Delay(10000);
                    UpdateStatus("Closing browser...");
                    _webDriver.Quit();
                    _webDriver.Dispose();
                    _webDriver = null;
                    UpdateStatus("Browser closed. Process completed.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error while closing browser: {ex.Message}");
            }
        }

        private async Task ExtractAndSaveCookies()
        {
            UpdateStatus("SUCCESS: Login detected and user is on the correct page!");
            await Task.Delay(3000);

            UpdateStatus("Extracting authentication cookies...");
            var cookies = _webDriver.Manage().Cookies.AllCookies.ToList();

            if (!cookies.Any())
            {
                UpdateStatus("ERROR: No cookies found in browser session.");
                return;
            }

            await SaveCookiesToFileAsync(cookies);
            UpdateStatus($"Extracted {cookies.Count} cookies successfully!");

            var cookieDict = cookies.ToDictionary(c => c.Name, c => c.Value);
            var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
            var foundCookies = requiredCookies.Where(rc => cookieDict.ContainsKey(rc)).ToArray();

            if (foundCookies.Length > 0)
            {
                UpdateStatus($"SUCCESS: Found required cookies: {string.Join(", ", foundCookies)}");
                UpdateStatus("Cookie extraction completed! You can now use the automation.");
            }
            else
            {
                UpdateStatus("WARNING: Required authentication cookies not found.");
            }
        }

        private async Task<bool> WaitForSpecificUrlAsync()
        {
            var maxWaitTime = TimeSpan.FromMinutes(15);
            var checkInterval = TimeSpan.FromSeconds(2);
            var startTime = DateTime.Now;
            var targetUrl = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan";

            UpdateStatus($"Waiting for user to reach: {targetUrl}");
            UpdateStatus("Timeout: 15 minutes");

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    var currentUrl = _webDriver.Url;
                    var remainingTime = maxWaitTime - (DateTime.Now - startTime);

                    UpdateStatus($"Current URL: {currentUrl}");
                    UpdateStatus($"Time remaining: {remainingTime.Minutes}:{remainingTime.Seconds:00}");

                    if (currentUrl.Contains("isFeideinnlogget=true") && currentUrl.Contains("ojr=timeplan"))
                    {
                        UpdateStatus("SUCCESS! User has reached the timetable page.");
                        return true;
                    }

                    if (currentUrl.Contains("isFeideinnlogget=true"))
                    {
                        UpdateStatus("User is logged in but not on timetable page yet.");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error checking URL: {ex.Message}");
                }

                await Task.Delay(checkInterval);
            }

            return false;
        }

        private async Task SaveCookiesToFileAsync(IList<OpenQA.Selenium.Cookie> seleniumCookies)
        {
            try
            {
                var cookieList = seleniumCookies.Select(c => new Cookie(c.Name, c.Value)).ToArray();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var jsonString = JsonSerializer.Serialize(cookieList, jsonOptions);
                await File.WriteAllTextAsync("cookies.json", jsonString);

                var cookieString = string.Join("; ", seleniumCookies.Select(c => $"{c.Name}={c.Value}"));
                await File.WriteAllTextAsync("cookies.txt", cookieString);

                var summaryLines = new List<string>
                {
                    $"Cookie extraction completed at: {DateTime.Now}",
                    $"Total cookies extracted: {seleniumCookies.Count}",
                    "",
                    "Cookie names:"
                };
                summaryLines.AddRange(seleniumCookies.Select(c => 
                    $"  - {c.Name} = {c.Value.Substring(0, Math.Min(20, c.Value.Length))}..."));

                await File.WriteAllLinesAsync("cookie_summary.txt", summaryLines);
                UpdateStatus($"Cookies saved successfully to: {Path.GetFullPath("cookies.json")}");
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
                if (!File.Exists(cookiesPath)) return null;

                var jsonString = await File.ReadAllTextAsync(cookiesPath);
                if (string.IsNullOrWhiteSpace(jsonString)) return null;

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (cookiesArray?.Any() != true) return null;

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);
                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                
                var invalidCookies = requiredCookies.Where(rc => 
                    !cookieDict.ContainsKey(rc) || 
                    cookieDict[rc].StartsWith("YOUR_") || 
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                return invalidCookies.Any() ? null : cookieDict;
            }
            catch
            {
                return null;
            }
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning) return;

            UpdateAutomationState(true);
            UpdateStatus("Initializing automation...");

            try
            {
                // Check and validate authentication
                await ValidateAuthenticationAsync();

                // Start automation loop
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

        private async Task ValidateAuthenticationAsync()
        {
            UpdateStatus("[1/3] Checking authentication cookies...");
            var existingCookies = await LoadExistingCookiesAsync();

            if (existingCookies == null)
            {
                UpdateStatus("[1/3] No valid cookies found. Starting browser login process...");
                await LoginAndExtractCookiesAsync();
                
                var newCookies = await LoadExistingCookiesAsync();
                if (newCookies == null)
                {
                    UpdateStatus("FAILED: Cookie extraction unsuccessful. Cannot start automation without authentication.");
                    UpdateAutomationState(false);
                    return;
                }
            }

            UpdateStatus("[1/3] Valid authentication cookies found!");
            UpdateStatus("[2/3] Validating authentication with server...");

            try
            {
                var testSchedule = await GetScheduleDataAsync(CancellationToken.None);
                if (testSchedule?.Items == null)
                {
                    await RefreshAuthenticationAsync();
                }
                else
                {
                    UpdateStatus("[2/3] Authentication validated successfully!");
                }
            }
            catch (Exception)
            {
                await RefreshAuthenticationAsync();
            }
        }

        private async Task RefreshAuthenticationAsync()
        {
            UpdateStatus("[2/3] Authentication test failed. Refreshing cookies...");
            await LoginAndExtractCookiesAsync();
            
            var refreshedCookies = await LoadExistingCookiesAsync();
            if (refreshedCookies == null)
            {
                UpdateStatus("FAILED: Unable to refresh authentication. Automation cannot start.");
                UpdateAutomationState(false);
                return;
            }

            var testSchedule = await GetScheduleDataAsync(CancellationToken.None);
            if (testSchedule?.Items == null)
            {
                UpdateStatus("FAILED: Authentication still invalid after refresh.");
                UpdateAutomationState(false);
                return;
            }

            UpdateStatus("[2/3] Authentication refreshed successfully!");
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
                var settingsWindow = new AkademiTrack.Views.SettingsWindow();
                await settingsWindow.ShowDialog(Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null);
                UpdateStatus("Settings window opened");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening settings: {ex.Message}");
            }
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

                    var scheduleData = await ProcessScheduleDataAsync(cancellationToken);
                    if (scheduleData == null) return; // Automation stopped

                    await ProcessAttendanceAsync(scheduleData, cancellationToken);

                    var waitTime = DetermineWaitTime(scheduleData.Items, DateTime.Now, out string waitReason);
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
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Network retry", cancellationToken);
                }
                catch (JsonException jsonEx)
                {
                    UpdateStatus($"JSON PARSE ERROR: {jsonEx.Message}");
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Parse retry", cancellationToken);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"UNEXPECTED ERROR: {ex.Message}");
                    await DelayWithCountdown(TimeSpan.FromSeconds(30), "Error retry", cancellationToken);
                }
            }
        }

        private async Task<ScheduleResponse> ProcessScheduleDataAsync(CancellationToken cancellationToken)
        {
            UpdateStatus("[1/4] Loading authentication cookies...");
            var cookies = await LoadCookiesFromFileAsync();
            if (!ValidateCookies(cookies)) return null;

            UpdateStatus("[2/4] Fetching schedule data from server...");
            var scheduleData = await GetScheduleDataAsync(cancellationToken);

            if (scheduleData?.Items?.Any() != true)
            {
                UpdateStatus("[2/4] No schedule data found");
                UpdateStatus("STOPPING AUTOMATION: No classes found in schedule");
                UpdateAutomationState(false);
                return null;
            }

            UpdateStatus($"[2/4] SUCCESS: Found {scheduleData.Items.Count} schedule items");
            return scheduleData;
        }

        private bool ValidateCookies(Dictionary<string, string> cookies)
        {
            if (cookies?.Any() != true)
            {
                UpdateStatus("ERROR: No valid cookies found. Use 'Login & Extract' to get fresh cookies.");
                UpdateStatus("STOPPING AUTOMATION: No authentication");
                UpdateAutomationState(false);
                return false;
            }

            var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
            var foundCookies = requiredCookies.Where(rc => cookies.ContainsKey(rc)).ToArray();
            UpdateStatus($"[1/4] Cookies loaded: {foundCookies.Length}/{requiredCookies.Length} required cookies found");
            return true;
        }

        private async Task ProcessAttendanceAsync(ScheduleResponse scheduleData, CancellationToken cancellationToken)
        {
            var now = DateTime.Now;
            var actionableClasses = GetActionableClasses(scheduleData.Items, now);

            if (!actionableClasses.Any())
            {
                UpdateStatus("STOPPING AUTOMATION: No classes within attendance window");
                ShowUpcomingClasses(scheduleData.Items, now);
                UpdateAutomationState(false);
                return;
            }

            ShowClassStatusSummary(actionableClasses, now);

            UpdateStatus("[3/4] Processing attendance requirements...");
            var attendanceMarked = 0;
            var attendanceSkipped = 0;
            var attendanceErrors = 0;

            foreach (var item in scheduleData.Items)
            {
                if (cancellationToken.IsCancellationRequested) break;

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
            ShowNextActionableClass(scheduleData.Items, now);
        }

        private static List<ScheduleItem> GetActionableClasses(List<ScheduleItem> items, DateTime now)
        {
            return items
                .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                              TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                              date.Add(time) > now.AddMinutes(-45))
                .ToList();
        }

        private void ShowUpcomingClasses(List<ScheduleItem> items, DateTime now)
        {
            var futureClasses = items
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
            }
        }

        private void ShowClassStatusSummary(List<ScheduleItem> actionableClasses, DateTime now)
        {
            var currentClasses = new List<string>();
            var upcomingClasses = new List<string>();
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
                UpdateStatus($"  CURRENT/SOON: {string.Join(", ", currentClasses)}");
            if (upcomingClasses.Any())
                UpdateStatus($"  UPCOMING: {string.Join(", ", upcomingClasses.Take(2))}");
            if (pastClasses.Any())
                UpdateStatus($"  PAST: {pastClasses.Count} completed classes");
        }

        private void ShowNextActionableClass(List<ScheduleItem> items, DateTime now)
        {
            var nextActionable = GetNextActionableClass(items, now);
            if (nextActionable != null)
            {
                UpdateStatus($"[3/4] Next action: {nextActionable}");
            }
        }

        private async Task DelayWithCountdown(TimeSpan delay, string actionName, CancellationToken cancellationToken)
        {
            var endTime = DateTime.Now.Add(delay);
            var updateInterval = TimeSpan.FromSeconds(delay.TotalSeconds > 60 ? 30 : 10);

            while (DateTime.Now < endTime && !cancellationToken.IsCancellationRequested)
            {
                var remaining = endTime - DateTime.Now;
                var message = remaining.TotalSeconds > 60 
                    ? $"Timer: {actionName} in {remaining.Minutes}m {remaining.Seconds}s (at {endTime:HH:mm:ss})"
                    : $"Timer: {actionName} in {remaining.Seconds}s";

                UpdateStatus(message);

                var waitTime = remaining < updateInterval ? remaining : updateInterval;
                await Task.Delay(waitTime, cancellationToken);
            }
        }

        private string GetTimeInfo(ScheduleItem item, DateTime now)
        {
            try
            {
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate) ||
                    !TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    return "Invalid date/time";
                }

                var classDateTime = itemDate.Add(startTime);
                var timeDiff = classDateTime - now;

                return Math.Abs(timeDiff.TotalMinutes) < 15 ? "NOW" :
                       timeDiff.TotalMinutes < 0 ? $"{Math.Abs(timeDiff.TotalMinutes):F0} minutes ago" :
                       timeDiff.TotalHours < 24 ? $"in {timeDiff.TotalHours:F0}h {timeDiff.Minutes}m" :
                       $"in {timeDiff.Days}d";
            }
            catch
            {
                return "Time error";
            }
        }

        private TimeSpan DetermineWaitTime(List<ScheduleItem> items, DateTime now, out string reason)
        {
            try
            {
                var nextClass = GetNextScheduledClass(items, now);
                if (nextClass == null)
                {
                    reason = "REASON FOR 5MIN WAIT: No upcoming classes - checking for new schedule data";
                    return TimeSpan.FromMinutes(5);
                }

                var timeUntilClass = nextClass.DateTime - now;
                var timeUntilWindow = nextClass.DateTime.AddMinutes(-15) - now;

                return timeUntilWindow.TotalMinutes switch
                {
                    <= 2 => SetReason(out reason, $"REASON FOR 2MIN WAIT: {nextClass.Item.Fag} attendance window opens in {timeUntilWindow.TotalMinutes:F0} minutes", TimeSpan.FromMinutes(2)),
                    <= 10 => SetReason(out reason, $"REASON FOR 3MIN WAIT: {nextClass.Item.Fag} at {nextClass.Item.StartKl} - attendance opens in {timeUntilWindow.TotalMinutes:F0} minutes", TimeSpan.FromMinutes(3)),
                    <= 30 => SetReason(out reason, $"REASON FOR 5MIN WAIT: {nextClass.Item.Fag} at {nextClass.Item.StartKl} - attendance opens in {timeUntilWindow.TotalMinutes:F0} minutes", TimeSpan.FromMinutes(5)),
                    <= 120 => SetReason(out reason, $"REASON FOR 15MIN WAIT: Next class ({nextClass.Item.Fag}) is in {timeUntilWindow.TotalHours:F1} hours", TimeSpan.FromMinutes(15)),
                    _ => SetReason(out reason, $"REASON FOR 30MIN WAIT: Next class ({nextClass.Item.Fag}) is {timeUntilWindow.TotalHours:F1} hours away", TimeSpan.FromMinutes(30))
                };
            }
            catch (Exception ex)
            {
                reason = $"REASON FOR 5MIN WAIT: Error calculating wait time ({ex.Message})";
                return TimeSpan.FromMinutes(5);
            }
        }

        private static TimeSpan SetReason(out string reason, string message, TimeSpan time)
        {
            reason = message;
            return time;
        }

        private static dynamic GetNextScheduledClass(List<ScheduleItem> items, DateTime now)
        {
            return items
                .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                              TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                              date.Add(time) > now.AddMinutes(-45))
                .Select(item => new
                {
                    Item = item,
                    DateTime = DateTime.ParseExact(item.Dato, "yyyyMMdd", null).Add(TimeSpan.ParseExact(item.StartKl, "HHmm", null))
                })
                .OrderBy(x => x.DateTime)
                .FirstOrDefault();
        }

        private string GetNextActionableClass(List<ScheduleItem> items, DateTime now)
        {
            try
            {
                var nextClass = GetNextScheduledClass(items, now);
                return nextClass != null 
                    ? $"{nextClass.Item.Fag} at {nextClass.Item.StartKl} ({GetTimeInfo(nextClass.Item, now)})"
                    : "No upcoming classes in attendance window";
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
                var cookies = await LoadCookiesFromFileAsync();
                if (!cookies?.Any() == true)
                {
                    throw new Exception("Failed to load cookies. Use 'Login & Extract' to get fresh cookies.");
                }

                var url = BuildApiUrl();
                UpdateStatus($"API URL: {url}");

                using var httpClient = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                });

                var request = CreateApiRequest(url, cookies);
                var response = await httpClient.SendAsync(request, cancellationToken);

                return await ProcessApiResponse(response);
            }
            catch (Exception ex) when (!(ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException))
            {
                UpdateStatus($"Unexpected error in GetScheduleDataAsync: {ex.Message}");
                throw new Exception($"Error in GetScheduleDataAsync: {ex.Message}", ex);
            }
        }

        private static string BuildApiUrl()
        {
            var baseUrl = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote";
            var finder = "RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312";
            return $"{baseUrl}?finder={Uri.EscapeDataString(finder)}&onlyData=true&limit=99&offset=0&totalResults=true";
        }

        private static HttpRequestMessage CreateApiRequest(string url, Dictionary<string, string> cookies)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            var headers = new Dictionary<string, string>
            {
                ["Host"] = "iskole.net",
                ["Sec-Ch-Ua-Platform"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "\"macOS\"" : "\"Windows\"",
                ["Accept-Language"] = "no-NB",
                ["Accept"] = "application/json, text/javascript, */*; q=0.01",
                ["Sec-Ch-Ua"] = "\"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"140\"",
                ["User-Agent"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) 
                    ? "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36"
                    : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36",
                ["Sec-Ch-Ua-Mobile"] = "?0",
                ["Sec-Fetch-Site"] = "same-origin",
                ["Sec-Fetch-Mode"] = "cors",
                ["Sec-Fetch-Dest"] = "empty",
                ["Referer"] = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar",
                ["Priority"] = "u=4, i",
                ["Connection"] = "keep-alive",
                ["Cookie"] = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"))
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            return request;
        }

        private async Task<ScheduleResponse> ProcessApiResponse(HttpResponseMessage response)
        {
            UpdateStatus($"Server response: HTTP {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorMessage = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Authentication failed - cookies may be expired",
                    System.Net.HttpStatusCode.Forbidden => "Access forbidden - insufficient permissions or expired session",
                    _ => $"Server returned error {(int)response.StatusCode}: {response.StatusCode}"
                };
                throw new Exception($"{errorMessage}. Response: {errorContent}");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            UpdateStatus($"Raw response length: {jsonString.Length} characters");

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
                throw;
            }
        }

        private async Task MarkAttendanceAsync(ScheduleItem item, CancellationToken cancellationToken)
        {
            var cookies = await LoadCookiesFromFileAsync();
            if (cookies == null) throw new Exception("Failed to load cookies");

            var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
            var authCookie = cookies.GetValueOrDefault("_WL_AUTHCOOKIE_JSESSIONID", "");
            var oracleRoute = cookies.GetValueOrDefault("X-Oracle-BMC-LBS-Route", "");

            var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

            var payload = await CreateAttendancePayload(item);
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.oracle.adf.action+json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            AddAttendanceHeaders(request, jsessionId, authCookie, oracleRoute);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to mark attendance. Status: {response.StatusCode}, Content: {errorContent}");
            }
        }

        private async Task<object> CreateAttendancePayload(ScheduleItem item)
        {
            return new
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
        }

        private static void AddAttendanceHeaders(HttpRequestMessage request, string jsessionId, string authCookie, string oracleRoute)
        {
            var jsessionIdClean = jsessionId.Split('!')[0];
            var headers = new Dictionary<string, string>
            {
                ["Host"] = "iskole.net",
                ["Cookie"] = $"X-Oracle-BMC-LBS-Route={oracleRoute}; JSESSIONID={jsessionIdClean}; _WL_AUTHCOOKIE_JSESSIONID={authCookie}",
                ["Sec-Ch-Ua-Platform"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "\"macOS\"" : "\"Windows\"",
                ["User-Agent"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36"
                    : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36",
                ["Accept-Language"] = "nb-NO,nb;q=0.9",
                ["Sec-Ch-Ua"] = "\"Chromium\";v=\"140\", \"Not;A=Brand\";v=\"99\"",
                ["Sec-Ch-Ua-Mobile"] = "?0",
                ["X-Requested-With"] = "XMLHttpRequest",
                ["Accept"] = "application/json, text/javascript, */*; q=0.01",
                ["Origin"] = "https://iskole.net",
                ["Sec-Fetch-Site"] = "same-origin",
                ["Sec-Fetch-Mode"] = "cors",
                ["Sec-Fetch-Dest"] = "empty",
                ["Referer"] = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["Priority"] = "u=4, i",
                ["Connection"] = "keep-alive"
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
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
                    throw new FileNotFoundException($"Created cookies.json template. Please use 'Login & Extract' to get real cookies.");
                }

                var jsonString = await File.ReadAllTextAsync(cookiesPath);
                if (string.IsNullOrWhiteSpace(jsonString))
                    throw new Exception("cookies.json file is empty");

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (cookiesArray?.Any() != true)
                    throw new Exception("No cookies found in cookies.json");

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);
                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var invalidCookies = requiredCookies.Where(rc => 
                    !cookieDict.ContainsKey(rc) || 
                    cookieDict[rc].StartsWith("YOUR_") || 
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                if (invalidCookies.Any())
                {
                    throw new Exception($"Invalid cookies detected: {string.Join(", ", invalidCookies)}. Please use 'Login & Extract' to get fresh cookies.");
                }

                return cookieDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Cookie error: {ex.Message}", ex);
            }
        }

        private static async Task CreateSampleCookiesFileAsync(string cookiesPath)
        {
            var sampleCookies = new Cookie[]
            {
                new Cookie("JSESSIONID", "YOUR_JSESSIONID_HERE"),
                new Cookie("_WL_AUTHCOOKIE_JSESSIONID", "YOUR_WL_AUTHCOOKIE_JSESSIONID_HERE"),
                new Cookie("X-Oracle-BMC-LBS-Route", "YOUR_ORACLE_BMC_LBS_ROUTE_HERE")
            };

            var jsonString = JsonSerializer.Serialize(sampleCookies, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
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

        private static bool ShouldMarkAttendance(ScheduleItem item)
        {
            try
            {
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate) ||
                    !TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    return false;
                }

                var classDateTime = itemDate.Add(startTime);
                var timeDiff = classDateTime - DateTime.Now;
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
        public Cookie(string name, string value)
        {
            Name = name;
            Value = value;
        }

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
        public string Timenr { get; set; }
    }
}