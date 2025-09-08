using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    private readonly bool _disableWhenExecuting;

    public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null, bool disableWhenExecuting = true)
    {
        _execute = execute;
        _canExecute = canExecute ?? (() => true);
        _disableWhenExecuting = disableWhenExecuting;
    }

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter) => 
        _disableWhenExecuting ? (!_isExecuting && _canExecute()) : _canExecute();

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

            StartAutomationCommand = new SimpleCommand(StartAutomationAsync, () => !IsAutomationRunning);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync, () => IsAutomationRunning);
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
                // Fix: Cast and call RaiseCanExecuteChanged properly
                (StartAutomationCommand as SimpleCommand)?.RaiseCanExecuteChanged();
                (StopAutomationCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsAutomationRunning = isRunning;
                    (StartAutomationCommand as SimpleCommand)?.RaiseCanExecuteChanged();
                    (StopAutomationCommand as SimpleCommand)?.RaiseCanExecuteChanged();
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
            UpdateStatus("Stop button clicked - cancelling automation...");
            
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                UpdateStatus("Cancellation requested...");
            }
            
            // Force update the automation state
            UpdateAutomationState(false);
            UpdateStatus("Automation stopped by user");
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
            Debug.WriteLine("=== STUDIETID AUTOMATION STARTED ===", "AUTOMATION");
            Debug.WriteLine($"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "AUTOMATION");

            UpdateStatus("Starting studietid automation loop...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Debug.WriteLine("--- NEW AUTOMATION CYCLE ---", "CYCLE");
                    Debug.WriteLine($"Cycle started at: {DateTime.Now:HH:mm:ss}", "CYCLE");

                    UpdateStatus("Fetching schedule data...");
                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    if (scheduleData?.Items?.Any() != true)
                    {
                        Debug.WriteLine("No schedule data found in response", "SCHEDULE");
                        UpdateStatus("No schedule data found. Checking again in 30 minutes...");
                        await DelayWithCountdown(TimeSpan.FromMinutes(30), "schedule refresh", cancellationToken);
                        continue;
                    }

                    Debug.WriteLine($"Total schedule items received: {scheduleData.Items.Count}", "SCHEDULE");

                    // Filter for studietid (STU) classes only
                    var studietidClasses = scheduleData.Items
                        .Where(item => item.Fag != null && item.Fag.Contains("STU"))
                        .ToList();

                    Debug.WriteLine($"Filtered STU classes: {studietidClasses.Count}", "FILTER");

                    if (!studietidClasses.Any())
                    {
                        Debug.WriteLine("No STU classes found - stopping automation", "AUTOMATION");
                        UpdateStatus("No studietid (STU) classes found in today's schedule.");
                        UpdateStatus("AUTOMATION COMPLETE: No studietid classes to register today.");
                        UpdateAutomationState(false);
                        return;
                    }

                    var now = DateTime.Now;

                    // Process studietid classes
                    await ProcessStudietidClasses(studietidClasses, now, cancellationToken);

                    // Check completion status
                    var completionStatus = CheckStudietidCompletion(studietidClasses, now);
                    if (completionStatus.IsComplete)
                    {
                        UpdateStatus(completionStatus.Message);
                        UpdateAutomationState(false);
                        return;
                    }

                    // Determine next wait time
                    var waitTime = DetermineStudietidWaitTime(studietidClasses, now, out string reason);
                    UpdateStatus(reason);
                    await DelayWithCountdown(waitTime, "next STU check", cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Studietid automation cancelled by user", "CANCELLED");
                    UpdateStatus("Studietid automation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ERROR in automation loop: {ex.Message}", "ERROR");
                    UpdateStatus($"Error in studietid automation: {ex.Message}");
                    await DelayWithCountdown(TimeSpan.FromMinutes(5), "error recovery", cancellationToken);
                }
            }
        }

        private async Task ProcessStudietidClasses(List<ScheduleItem> studietidClasses, DateTime now, CancellationToken cancellationToken)
        {
            Debug.WriteLine("=== PROCESSING STU CLASSES ===", "PROCESS");

            foreach (var stuClass in studietidClasses)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var classStatus = GetStudietidClassStatus(stuClass, now);
                Debug.WriteLine($"STU Class: {stuClass.Fag} at {stuClass.StartKl} - Status: {classStatus.Status}", "PROCESS");

                if (classStatus.Status == StudietidStatus.ReadyToRegister)
                {
                    Debug.WriteLine($"REGISTRATION WINDOW OPEN: {stuClass.Fag} at {stuClass.StartKl}", "REGISTER");
                    UpdateStatus($"Registering for studietid: {stuClass.Fag} at {stuClass.StartKl}");

                    try
                    {
                        await MarkAttendanceAsync(stuClass, cancellationToken);
                        Debug.WriteLine($"✅ SUCCESS: Registered for {stuClass.Fag}", "SUCCESS");
                        UpdateStatus($"✓ Successfully registered for studietid: {stuClass.Fag}");

                        // Wait for system to update
                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ FAILED to register {stuClass.Fag}: {ex.Message}", "ERROR");
                        UpdateStatus($"✗ Failed to register for studietid {stuClass.Fag}: {ex.Message}");
                    }
                }
            }
        }

        private (StudietidStatus Status, string Message, DateTime? ClassTime) GetStudietidClassStatus(ScheduleItem stuClass, DateTime now)
        {
            // Check if already registered
            if (stuClass.ElevForerTilstedevaerelse == 1 || stuClass.Typefravaer == "M")
            {
                return (StudietidStatus.AlreadyRegistered, "Already registered", null);
            }

            // Parse class time
            if (!DateTime.TryParseExact(stuClass.Dato, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var classDate) ||
                !TimeSpan.TryParseExact(stuClass.StartKl, "HHmm", CultureInfo.InvariantCulture, out var startTime))
            {
                Debug.WriteLine($"Failed to parse time for {stuClass.Fag}: Date={stuClass.Dato}, Time={stuClass.StartKl}", "ERROR");
                return (StudietidStatus.ParseError, "Could not parse date/time", null);
            }

            var classDateTime = classDate.Add(startTime);
            var timeDiff = classDateTime - now;

            // Registration window: 15 minutes before to 15 minutes after class starts
            if (timeDiff.TotalMinutes > 15)
            {
                return (StudietidStatus.Upcoming, $"Upcoming in {timeDiff.TotalMinutes:F0} minutes", classDateTime);
            }
            else if (timeDiff.TotalMinutes >= -15 && timeDiff.TotalMinutes <= 15)
            {
                return (StudietidStatus.ReadyToRegister, "Registration window open", classDateTime);
            }
            else
            {
                return (StudietidStatus.Missed, "Registration window closed", classDateTime);
            }
        }

        private (bool IsComplete, string Message) CheckStudietidCompletion(List<ScheduleItem> studietidClasses, DateTime now)
        {
            var analysis = AnalyzeStudietidClasses(studietidClasses, now);

            Debug.WriteLine($"STU Analysis - Total: {analysis.Total}, Registered: {analysis.AlreadyRegistered}, Current: {analysis.Current}, Upcoming: {analysis.Upcoming}, Missed: {analysis.Past}", "ANALYSIS");

            // All classes are registered
            if (analysis.AlreadyRegistered == analysis.Total)
            {
                return (true, "SUCCESS: All studietid classes for today have been registered!");
            }

            // No more classes can be registered (all are either registered or missed)
            if (analysis.Current == 0 && analysis.Upcoming == 0)
            {
                return (true, "AUTOMATION COMPLETE: No more studietid classes can be registered today.");
            }

            return (false, $"Continuing automation - {analysis.AlreadyRegistered}/{analysis.Total} registered, {analysis.Current} ready, {analysis.Upcoming} upcoming");
        }

        private TimeSpan DetermineStudietidWaitTime(List<ScheduleItem> studietidClasses, DateTime now, out string reason)
        {
            var nextClass = studietidClasses
                .Select(stuClass => GetStudietidClassStatus(stuClass, now))
                .Where(status => status.Status == StudietidStatus.Upcoming && status.ClassTime.HasValue)
                .OrderBy(status => status.ClassTime.Value)
                .FirstOrDefault();

            if (nextClass.ClassTime == null)
            {
                reason = "No upcoming studietid classes - checking for schedule updates in 30 minutes";
                return TimeSpan.FromMinutes(30);
            }

            var timeUntilClass = nextClass.ClassTime.Value - now;
            var timeUntilWindow = nextClass.ClassTime.Value.AddMinutes(-15) - now;

            if (timeUntilWindow.TotalMinutes <= 2)
            {
                reason = $"Next studietid class registration opens in {timeUntilWindow.TotalMinutes:F0} minutes - checking in 1 minute";
                return TimeSpan.FromMinutes(1);
            }
            else if (timeUntilWindow.TotalMinutes <= 10)
            {
                reason = $"Next studietid registration window opens in {timeUntilWindow.TotalMinutes:F0} minutes - checking in 2 minutes";
                return TimeSpan.FromMinutes(2);
            }
            else if (timeUntilWindow.TotalMinutes <= 30)
            {
                reason = $"Next studietid class in {timeUntilWindow.TotalMinutes:F0} minutes - checking in 5 minutes";
                return TimeSpan.FromMinutes(5);
            }
            else
            {
                reason = $"Next studietid class in {timeUntilWindow.TotalHours:F1} hours - checking in 15 minutes";
                return TimeSpan.FromMinutes(15);
            }
        }

        // Add this enum to your class
        private enum StudietidStatus
        {
            AlreadyRegistered,
            Upcoming,
            ReadyToRegister,
            Missed,
            ParseError
        }

        private (int Total, int AlreadyRegistered, int Upcoming, int Current, int Past) AnalyzeStudietidClasses(List<ScheduleItem> studietidClasses, DateTime now)
        {
            var total = studietidClasses.Count;
            var alreadyRegistered = 0;
            var upcoming = 0;
            var current = 0;
            var past = 0;

            foreach (var stuClass in studietidClasses)
            {
                var status = GetStudietidClassStatus(stuClass, now);

                switch (status.Status)
                {
                    case StudietidStatus.AlreadyRegistered:
                        alreadyRegistered++;
                        break;
                    case StudietidStatus.Upcoming:
                        upcoming++;
                        break;
                    case StudietidStatus.ReadyToRegister:
                        current++;
                        break;
                    case StudietidStatus.Missed:
                    case StudietidStatus.ParseError:
                        past++;
                        break;
                }
            }

            return (total, alreadyRegistered, upcoming, current, past);
        }

        // Helper method to check if a studietid class is already registered
        private bool IsStudietidAlreadyRegistered(ScheduleItem stuClass)
        {
            // A class is considered registered if:
            // 1. ElevForerTilstedevaerelse is 1 (student has registered attendance)
            // 2. OR Typefravaer is "M" (marked as present)
            return stuClass.ElevForerTilstedevaerelse == 1 || stuClass.Typefravaer == "M";
        }

        private ScheduleItem FindNextStudietidClassToRegister(List<ScheduleItem> studietidClasses, DateTime now)
        {
            return studietidClasses
                .Where(stuClass =>
                {
                    // Skip if already registered
                    if (IsStudietidAlreadyRegistered(stuClass))
                        return false;

                    // Check if within registration window
                    if (DateTime.TryParseExact(stuClass.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                        TimeSpan.TryParseExact(stuClass.StartKl, "HHmm", null, out var startTime))
                    {
                        var classDateTime = date.Add(startTime);
                        var timeDiff = classDateTime - now;

                        // Can register from 15 minutes before until 15 minutes after class starts
                        return timeDiff.TotalMinutes >= -15;
                    }

                    return false;
                })
                .OrderBy(stuClass => DateTime.ParseExact(stuClass.Dato, "yyyyMMdd", null)
                    .Add(TimeSpan.ParseExact(stuClass.StartKl, "HHmm", null)))
                .FirstOrDefault();
        }

        // ADD this new method to find the next class needing attendance:
        private dynamic GetNextClassNeedingAttendance(List<ScheduleItem> items)
        {
            var now = DateTime.Now;
            
            UpdateStatus($"DEBUG: Checking {items.Count} schedule items for classes needing attendance...");
            
            // First, let's see what we have in the schedule
            foreach (var item in items.Take(5)) // Show first 5 for debugging
            {
                if (DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var debugDate) &&
                    TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var debugTime))
                {
                    var debugDateTime = debugDate.Add(debugTime);
                    UpdateStatus($"DEBUG: {item.Fag} at {item.StartKl} on {debugDate:yyyy-MM-dd}, ElevForerTilstedevaerelse={item.ElevForerTilstedevaerelse}, DateTime={debugDateTime}");
                }
            }
            
            var result = items
                .Where(item => 
                {
                    // Parse date and time
                    if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) ||
                        !TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                    {
                        UpdateStatus($"DEBUG: Failed to parse date/time for {item.Fag}");
                        return false;
                    }

                    var classDateTime = date.Add(startTime);
                    
                    // Only consider classes that are upcoming (not more than 15 minutes in the past)
                    var isUpcoming = classDateTime >= now.AddMinutes(-15);
                    
                    // Try multiple ways to detect if attendance is needed
                    var needsAttendance = item.ElevForerTilstedevaerelse == 0 || 
                                        string.IsNullOrEmpty(item.Typefravaer) ||
                                        item.Typefravaer == "" ||
                                        item.Typefravaer == null;
                    
                    var shouldInclude = isUpcoming && needsAttendance;
                    
                    UpdateStatus($"DEBUG: {item.Fag} - Upcoming: {isUpcoming}, NeedsAttendance: {needsAttendance}, Include: {shouldInclude}");
                    
                    return shouldInclude;
                })
                .Select(item => new
                {
                    Item = item,
                    DateTime = DateTime.ParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None)
                        .Add(TimeSpan.ParseExact(item.StartKl, "HHmm", null))
                })
                .OrderBy(x => x.DateTime)
                .FirstOrDefault();
                
            if (result == null)
            {
                UpdateStatus("DEBUG: No classes found needing attendance");
                // Let's also check if we have ANY upcoming classes at all
                var anyUpcoming = items.Any(item => 
                    DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                    TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime) &&
                    date.Add(startTime) >= now.AddMinutes(-15));
                UpdateStatus($"DEBUG: Any upcoming classes at all: {anyUpcoming}");
            }
            else
            {
                UpdateStatus($"DEBUG: Found next class: {result.Item.Fag} at {result.DateTime}");
            }
            
            return result;
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
            Debug.WriteLine("=== STARTING API REQUEST ===", "API");

            try
            {
                Debug.WriteLine("Loading cookies from file...", "API");
                var cookies = await LoadCookiesFromFileAsync();
                if (!cookies?.Any() == true)
                {
                    Debug.WriteLine("ERROR: No cookies available", "API");
                    throw new Exception("Failed to load cookies. Use 'Login & Extract' to get fresh cookies.");
                }

                Debug.WriteLine($"Loaded {cookies.Count} cookies:", "API");
                foreach (var cookie in cookies)
                {
                    var truncatedValue = cookie.Value.Length > 20 ? cookie.Value.Substring(0, 20) + "..." : cookie.Value;
                    Debug.WriteLine($"  {cookie.Key} = {truncatedValue}", "API");
                }

                var url = BuildApiUrl();
                Debug.WriteLine($"API URL: {url}", "API");

                using var httpClient = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                });

                Debug.WriteLine("Creating HTTP request...", "API");
                var request = CreateApiRequest(url, cookies);

                // Log all request headers
                Debug.WriteLine("REQUEST HEADERS:", "API");
                foreach (var header in request.Headers)
                {
                    Debug.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}", "API");
                }

                Debug.WriteLine("Sending HTTP request to server...", "API");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var response = await httpClient.SendAsync(request, cancellationToken);

                stopwatch.Stop();
                Debug.WriteLine($"HTTP request completed in {stopwatch.ElapsedMilliseconds}ms", "API");

                return await ProcessApiResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CRITICAL ERROR in GetScheduleDataAsync: {ex.Message}", "ERROR");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}", "ERROR");
                throw;
            }
        }

        private void LogDebug(string message, string category = "DEBUG")
        {
            try
            {
                var logPath = "debug_log.txt";
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] [{category}] {message}\n";
                File.AppendAllText(logPath, logEntry);

                // Also show in status for important messages
                if (category == "ERROR" || category == "SUCCESS" || category == "AUTOMATION")
                {
                    UpdateStatus($"[{category}] {message}");
                }
            }
            catch
            {
                // Ignore logging errors
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
            Debug.WriteLine($"Server response: HTTP {(int)response.StatusCode} {response.StatusCode}", "API");

            // Log response headers
            Debug.WriteLine("RESPONSE HEADERS:", "API");
            foreach (var header in response.Headers)
            {
                Debug.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}", "API");
            }

            // Also log content headers if they exist
            if (response.Content?.Headers != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    Debug.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}", "API");
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"ERROR RESPONSE BODY: {errorContent}", "ERROR");

                var errorMessage = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Authentication failed - cookies may be expired",
                    System.Net.HttpStatusCode.Forbidden => "Access forbidden - insufficient permissions or expired session",
                    _ => $"Server returned error {(int)response.StatusCode}: {response.StatusCode}"
                };

                Debug.WriteLine($"API ERROR: {errorMessage}", "ERROR");
                throw new Exception($"{errorMessage}. Response: {errorContent}");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"Raw response length: {jsonString.Length} characters", "API");

            // Log much more of the response - up to 5000 characters for complete visibility
            var preview = jsonString.Length > 5000 ? jsonString.Substring(0, 5000) + "..." : jsonString;
            Debug.WriteLine($"Response preview: {preview}", "API");

            // Also break it into chunks if it's very long to avoid console truncation
            if (jsonString.Length > 2000)
            {
                Debug.WriteLine("=== FULL JSON RESPONSE IN CHUNKS ===", "API");
                const int chunkSize = 2000;
                for (int i = 0; i < jsonString.Length; i += chunkSize)
                {
                    int length = Math.Min(chunkSize, jsonString.Length - i);
                    string chunk = jsonString.Substring(i, length);
                    Debug.WriteLine($"CHUNK {(i / chunkSize) + 1}: {chunk}", "API");
                }
                Debug.WriteLine("=== END OF CHUNKED RESPONSE ===", "API");
            }

            // Also log the full response to a file for complete debugging
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var debugFileName = $"api_response_{timestamp}.json";
                await File.WriteAllTextAsync(debugFileName, jsonString);
                Debug.WriteLine($"Full response saved to: {debugFileName}", "API");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not save full response to file: {ex.Message}", "API");
            }

            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Debug.WriteLine("ERROR: Server returned empty response", "ERROR");
                return new ScheduleResponse { Items = new List<ScheduleItem>() };
            }

            try
            {
                Debug.WriteLine("Parsing JSON response...", "API");

                // First, let's try to parse as a generic JsonDocument to see the structure
                using (JsonDocument document = JsonDocument.Parse(jsonString))
                {
                    Debug.WriteLine("JSON structure analysis:", "API");
                    Debug.WriteLine($"  Root element kind: {document.RootElement.ValueKind}", "API");

                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        Debug.WriteLine("  Root properties:", "API");
                        foreach (var property in document.RootElement.EnumerateObject().Take(10)) // First 10 properties
                        {
                            Debug.WriteLine($"    {property.Name}: {property.Value.ValueKind}", "API");
                        }
                    }
                }

                // Now parse into our object
                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var itemCount = scheduleResponse?.Items?.Count ?? 0;
                Debug.WriteLine($"Successfully parsed {itemCount} schedule items", "API");

                // Log detailed information about each schedule item
                if (scheduleResponse?.Items != null)
                {
                    Debug.WriteLine("SCHEDULE ITEMS DETAILS:", "SCHEDULE");
                    for (int i = 0; i < scheduleResponse.Items.Count; i++)
                    {
                        var item = scheduleResponse.Items[i];
                        Debug.WriteLine($"  [{i}] ID: {item.Id}, Fag: {item.Fag}, Date: {item.Dato}, Time: {item.StartKl}-{item.SluttKl}", "SCHEDULE");
                        Debug.WriteLine($"      ElevForerTilstedevaerelse: {item.ElevForerTilstedevaerelse}, Typefravaer: '{item.Typefravaer}'", "SCHEDULE");
                        Debug.WriteLine($"      Stkode: {item.Stkode}, KlTrinn: {item.KlTrinn}, KlId: {item.KlId}, KNavn: {item.KNavn}", "SCHEDULE");
                        Debug.WriteLine($"      GruppeNr: {item.GruppeNr}, Timenr: {item.Timenr}", "SCHEDULE");
                        Debug.WriteLine($"      UndervisningPaagaar: {item.UndervisningPaagaar}, Kollisjon: {item.Kollisjon}", "SCHEDULE");
                        Debug.WriteLine($"      TidsromTilstedevaerelse: '{item.TidsromTilstedevaerelse}'", "SCHEDULE");
                    }

                    Debug.WriteLine($"SCHEDULE: Total schedule items received: {scheduleResponse.Items.Count}", "SCHEDULE");
                }

                return scheduleResponse ?? new ScheduleResponse { Items = new List<ScheduleItem>() };
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON parsing failed: {ex.Message}", "ERROR");
                Debug.WriteLine($"JSON parsing failed at path: {ex.Path}", "ERROR");
                Debug.WriteLine($"JSON parsing failed at position: {ex.BytePositionInLine}", "ERROR");

                // Save the problematic JSON to a file for debugging
                try
                {
                    var errorFileName = $"json_parse_error_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    await File.WriteAllTextAsync(errorFileName, jsonString);
                    Debug.WriteLine($"Problematic JSON saved to: {errorFileName}", "ERROR");
                }
                catch (Exception fileEx)
                {
                    Debug.WriteLine($"Could not save problematic JSON: {fileEx.Message}", "ERROR");
                }

                // Try to extract partial data if possible
                try
                {
                    using JsonDocument document = JsonDocument.Parse(jsonString);
                    if (document.RootElement.TryGetProperty("items", out JsonElement itemsElement) ||
                        document.RootElement.TryGetProperty("Items", out itemsElement))
                    {
                        Debug.WriteLine($"Found items array with {itemsElement.GetArrayLength()} elements", "API");

                        // Try to parse individual items to see which one is problematic
                        var itemsList = new List<ScheduleItem>();
                        int successCount = 0;
                        int errorCount = 0;

                        foreach (var itemElement in itemsElement.EnumerateArray())
                        {
                            try
                            {
                                var item = JsonSerializer.Deserialize<ScheduleItem>(itemElement.GetRawText(), new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                });

                                if (item != null)
                                {
                                    itemsList.Add(item);
                                    successCount++;
                                }
                            }
                            catch (Exception itemEx)
                            {
                                errorCount++;
                                Debug.WriteLine($"Failed to parse individual item: {itemEx.Message}", "ERROR");
                                Debug.WriteLine($"Problematic item JSON: {itemElement.GetRawText()}", "ERROR");
                            }
                        }

                        Debug.WriteLine($"Partial parsing result: {successCount} successful, {errorCount} failed", "API");

                        if (itemsList.Count > 0)
                        {
                            return new ScheduleResponse { Items = itemsList };
                        }
                    }
                }
                catch (Exception partialEx)
                {
                    Debug.WriteLine($"Partial parsing also failed: {partialEx.Message}", "ERROR");
                }

                throw new JsonException($"Failed to parse JSON response: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error during response processing: {ex.Message}", "ERROR");
                Debug.WriteLine($"Exception type: {ex.GetType().Name}", "ERROR");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}", "ERROR");
                throw;
            }
        }


        private async Task MarkAttendanceAsync(ScheduleItem item, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"=== STARTING ATTENDANCE REGISTRATION FOR {item.Fag} ===", "ATTENDANCE");

            try
            {
                Debug.WriteLine("Loading cookies for attendance request...", "ATTENDANCE");
                var cookies = await LoadCookiesFromFileAsync();
                if (cookies == null)
                {
                    Debug.WriteLine("ERROR: Failed to load cookies for attendance", "ERROR");
                    throw new Exception("Failed to load cookies");
                }

                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var authCookie = cookies.GetValueOrDefault("_WL_AUTHCOOKIE_JSESSIONID", "");
                var oracleRoute = cookies.GetValueOrDefault("X-Oracle-BMC-LBS-Route", "");

                Debug.WriteLine($"Using JSESSIONID: {(jsessionId.Length > 20 ? jsessionId.Substring(0, 20) + "..." : jsessionId)}", "ATTENDANCE");
                Debug.WriteLine($"Using Auth Cookie: {(authCookie.Length > 20 ? authCookie.Substring(0, 20) + "..." : authCookie)}", "ATTENDANCE");

                Debug.WriteLine("Getting public IP address...", "ATTENDANCE");
                var ip = await GetPublicIpAsync();
                Debug.WriteLine($"Public IP: {ip}", "ATTENDANCE");

                // Create payload exactly like Python script
                Debug.WriteLine("Creating attendance payload...", "ATTENDANCE");
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
                new { ip = ip }
                    }
                };

                var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                Debug.WriteLine($"Attendance payload: {payloadJson}", "ATTENDANCE");

                // Build URL
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                Debug.WriteLine($"Attendance URL: {url}", "ATTENDANCE");

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.oracle.adf.action+json");

                // Create request
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

                // Add headers exactly like Python
                var jsessionIdClean = jsessionId.Split('!')[0];
                var headers = new Dictionary<string, string>
                {
                    ["Host"] = "iskole.net",
                    ["Cookie"] = $"X-Oracle-BMC-LBS-Route={oracleRoute}; JSESSIONID={jsessionIdClean}; _WL_AUTHCOOKIE_JSESSIONID={authCookie}",
                    ["Sec-Ch-Ua-Platform"] = "\"macOS\"",
                    ["Accept-Language"] = "nb-NO,nb;q=0.9",
                    ["Sec-Ch-Ua"] = "\"Chromium\";v=\"139\", \"Not;A=Brand\";v=\"99\"",
                    ["Sec-Ch-Ua-Mobile"] = "?0",
                    ["X-Requested-With"] = "XMLHttpRequest",
                    ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36",
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

                Debug.WriteLine("ATTENDANCE REQUEST HEADERS:", "ATTENDANCE");
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                    Debug.WriteLine($"  {header.Key}: {header.Value}", "ATTENDANCE");
                }

                Debug.WriteLine("Sending attendance registration request...", "ATTENDANCE");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var response = await _httpClient.SendAsync(request, cancellationToken);

                stopwatch.Stop();
                Debug.WriteLine($"Attendance request completed in {stopwatch.ElapsedMilliseconds}ms", "ATTENDANCE");

                var responseText = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Attendance response status: {response.StatusCode}", "ATTENDANCE");
                Debug.WriteLine($"Attendance response body: {responseText}", "ATTENDANCE");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"ATTENDANCE FAILED: HTTP {response.StatusCode}", "ERROR");
                    throw new Exception($"Server returned {response.StatusCode}: {responseText}");
                }

                Debug.WriteLine($"ATTENDANCE SUCCESS: {item.Fag} registered successfully!", "SUCCESS");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ATTENDANCE ERROR for {item.Fag}: {ex.Message}", "ERROR");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}", "ERROR");
                throw;
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
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync("https://api.ipify.org");
                    return response.Trim();
                }
            }
            catch
            {
                return "0.0.0.0"; // Fallback
            }
        }

        private async Task<bool> RegisterForStudietid(ScheduleItem stuClass)
        {
            try
            {
                Debug.WriteLine($"REGISTRATION: Attempting to register for {stuClass.Fag} at {stuClass.StartKl}", "REGISTRATION");

                // Load cookies
                var cookies = await LoadCookiesFromFileAsync();
                if (cookies == null)
                {
                    Debug.WriteLine("REGISTRATION: Failed to load cookies", "ERROR");
                    return false;
                }

                // Extract cookie values
                string jsessionid = cookies.GetValueOrDefault("JSESSIONID", "");
                string wlAuthCookie = cookies.GetValueOrDefault("_WL_AUTHCOOKIE_JSESSIONID", "");
                string oracleRoute = cookies.GetValueOrDefault("X-Oracle-BMC-LBS-Route", "");

                if (string.IsNullOrEmpty(jsessionid) || string.IsNullOrEmpty(wlAuthCookie))
                {
                    Debug.WriteLine("REGISTRATION: Missing required cookies", "ERROR");
                    return false;
                }

                // Get public IP
                string publicIp = await GetPublicIpAsync();
                Debug.WriteLine($"REGISTRATION: Using IP: {publicIp}", "REGISTRATION");

                // Prepare payload exactly like Python script
                var parameters = new object[]
                {
            new { fylkeid = "00" },
            new { skoleid = "312" },
            new { planperi = "2025-26" },
            new { ansidato = stuClass.Dato },
            new { stkode = stuClass.Stkode },
            new { kl_trinn = stuClass.KlTrinn },
            new { kl_id = stuClass.KlId },
            new { k_navn = stuClass.KNavn },
            new { gruppe_nr = stuClass.GruppeNr },
            new { timenr = stuClass.Timenr },
            new { fravaerstype = "M" }, // M for present/møtt
            new { ip = publicIp }
                };

                var payload = new
                {
                    name = "lagre_oppmote",
                    parameters = parameters
                };

                // Build URL
                string url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}";
                Debug.WriteLine($"REGISTRATION: URL: {url}", "REGISTRATION");

                // Create HTTP request
                using var httpClient = new HttpClient();
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.oracle.adf.action+json");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

                // Add headers exactly like Python
                string jsessionidClean = jsessionid.Split('!')[0];
                var headers = new Dictionary<string, string>
                {
                    ["Host"] = "iskole.net",
                    ["Cookie"] = $"X-Oracle-BMC-LBS-Route={oracleRoute}; JSESSIONID={jsessionidClean}; _WL_AUTHCOOKIE_JSESSIONID={wlAuthCookie}",
                    ["Sec-Ch-Ua-Platform"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "\"macOS\"" : "\"Windows\"",
                    ["Accept-Language"] = "nb-NO,nb;q=0.9",
                    ["Sec-Ch-Ua"] = "\"Chromium\";v=\"140\", \"Not;A=Brand\";v=\"99\"",
                    ["Sec-Ch-Ua-Mobile"] = "?0",
                    ["X-Requested-With"] = "XMLHttpRequest",
                    ["User-Agent"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36"
                        : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36",
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

                Debug.WriteLine("REGISTRATION: Sending POST request to register for studietid...", "REGISTRATION");

                // Send POST request
                var response = await httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"REGISTRATION: Response Status: {response.StatusCode}", "REGISTRATION");
                Debug.WriteLine($"REGISTRATION: Response: {responseText}", "REGISTRATION");

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"REGISTRATION: ✅ Successfully registered for {stuClass.Fag} at {stuClass.StartKl}", "SUCCESS");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"REGISTRATION: ❌ Failed to register for {stuClass.Fag} at {stuClass.StartKl}", "ERROR");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REGISTRATION: Exception during registration: {ex.Message}", "ERROR");
                return false;
            }
        }

        private Dictionary<string, string> LoadCookies()
        {
            try
            {
                string cookiesPath = "cookies.json"; // Adjust path as needed
                if (!File.Exists(cookiesPath))
                {
                    Console.WriteLine($"REGISTRATION: Cookie file not found: {cookiesPath}");
                    return null;
                }

                var cookiesJson = File.ReadAllText(cookiesPath);
                var cookiesArray = System.Text.Json.JsonSerializer.Deserialize<JsonElement[]>(cookiesJson);

                var cookies = new Dictionary<string, string>();
                foreach (var cookie in cookiesArray)
                {
                    if (cookie.TryGetProperty("name", out var name) && cookie.TryGetProperty("value", out var value))
                    {
                        cookies[name.GetString()] = value.GetString();
                    }
                }

                return cookies;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"REGISTRATION: Error loading cookies: {ex.Message}");
                return null;
            }
        }

        private async Task AnalyzeAndProcessStudietid(List<ScheduleItem> studietidClasses)
        {
            Debug.WriteLine("ANALYSIS: === STU CLASS ANALYSIS ===", "ANALYSIS");
            Debug.WriteLine($"ANALYSIS: Total STU classes: {studietidClasses.Count}", "ANALYSIS");

            DateTime currentTime = DateTime.Now;
            Debug.WriteLine($"ANALYSIS: Current time for analysis: {currentTime:HH:mm:ss}", "ANALYSIS");

            int alreadyRegistered = 0;
            int upcoming = 0;
            int readyToRegister = 0;
            int missed = 0;

            foreach (var stuClass in studietidClasses)
            {
                // Parse class time
                if (!DateTime.TryParseExact(stuClass.Dato, "yyyyMMdd", null, DateTimeStyles.None, out DateTime classDate) ||
                    !TimeSpan.TryParseExact(stuClass.StartKl, "HHmm", null, out TimeSpan startTime))
                {
                    Debug.WriteLine($"ANALYSIS: Could not parse time for class {stuClass.Fag}", "ANALYSIS");
                    continue;
                }

                DateTime classStartTime = classDate.Add(startTime);
                DateTime classEndTime = classStartTime.AddMinutes(45); // Assuming 45 min classes
                var timeDiff = classStartTime - currentTime;

                // Check if already registered (either ElevForerTilstedevaerelse = 1 OR Typefravaer = "M")
                if (stuClass.ElevForerTilstedevaerelse == 1 || stuClass.Typefravaer == "M")
                {
                    alreadyRegistered++;
                    Debug.WriteLine($"ANALYSIS: ✅ {stuClass.Fag} at {stuClass.StartKl} - Already registered", "ANALYSIS");
                    continue;
                }

                // Check timing - registration window is 15 minutes before to 15 minutes after class starts
                if (timeDiff.TotalMinutes > 15)
                {
                    upcoming++;
                    Debug.WriteLine($"ANALYSIS: ⏳ {stuClass.Fag} at {stuClass.StartKl} - Upcoming (in {timeDiff.TotalMinutes:F0} minutes)", "ANALYSIS");
                }
                else if (timeDiff.TotalMinutes >= -15 && timeDiff.TotalMinutes <= 15)
                {
                    readyToRegister++;
                    Debug.WriteLine($"ANALYSIS: 🎯 {stuClass.Fag} at {stuClass.StartKl} - READY TO REGISTER!", "ANALYSIS");

                    // REGISTER NOW!
                    try
                    {
                        bool success = await RegisterForStudietid(stuClass);
                        if (success)
                        {
                            Debug.WriteLine($"ANALYSIS: ✅ Registration successful for {stuClass.Fag}", "SUCCESS");
                            UpdateStatus($"✓ Successfully registered for studietid: {stuClass.Fag}");
                        }
                        else
                        {
                            Debug.WriteLine($"ANALYSIS: ❌ Registration failed for {stuClass.Fag}", "ERROR");
                            UpdateStatus($"✗ Failed to register for studietid: {stuClass.Fag}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ANALYSIS: ❌ Registration exception for {stuClass.Fag}: {ex.Message}", "ERROR");
                        UpdateStatus($"✗ Registration error for {stuClass.Fag}: {ex.Message}");
                    }
                }
                else
                {
                    missed++;
                    Debug.WriteLine($"ANALYSIS: ❌ {stuClass.Fag} at {stuClass.StartKl} - Missed (registration window closed)", "ANALYSIS");
                }
            }

            Debug.WriteLine($"ANALYSIS: Already registered: {alreadyRegistered}", "ANALYSIS");
            Debug.WriteLine($"ANALYSIS: Upcoming (future): {upcoming}", "ANALYSIS");
            Debug.WriteLine($"ANALYSIS: Current (ready to register): {readyToRegister}", "ANALYSIS");
            Debug.WriteLine($"ANALYSIS: Past (missed): {missed}", "ANALYSIS");

            if (readyToRegister == 0 && upcoming == 0 && alreadyRegistered == studietidClasses.Count)
            {
                Debug.WriteLine("SUCCESS: All STU classes are registered - automation complete!", "SUCCESS");
                UpdateStatus("SUCCESS: All studietid classes are now registered!");
            }
            else if (upcoming > 0)
            {
                Debug.WriteLine($"WAITING: {upcoming} STU classes upcoming - will register when time arrives", "WAIT");
                UpdateStatus($"Waiting for {upcoming} upcoming studietid classes to reach registration window");
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
                
                // Check if within attendance window AND attendance not already registered
                var withinTimeWindow = timeDiff.TotalMinutes >= -45 && timeDiff.TotalMinutes <= 15;
                var attendanceNeeded = item.ElevForerTilstedevaerelse == 0; // 0 = not registered
                
                return withinTimeWindow && attendanceNeeded;
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
    
    // Change this from string to int or use JsonConverter
    public int Timenr { get; set; }  // Changed from string to int
    
    // Alternative approach using JsonConverter if you need it as string:
    // [JsonConverter(typeof(JsonStringToIntConverter))]
    // public string Timenr { get; set; }
}
}