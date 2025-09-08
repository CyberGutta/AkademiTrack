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

                    foreach (var stuClass in studietidClasses)
                    {
                        Debug.WriteLine($"STU Class: {stuClass.Fag} at {stuClass.StartKl}, Registered: {stuClass.ElevForerTilstedevaerelse}, Type: {stuClass.Typefravaer}", "FILTER");
                    }

                    if (!studietidClasses.Any())
                    {
                        Debug.WriteLine("No STU classes found - stopping automation", "AUTOMATION");
                        UpdateStatus("No studietid (STU) classes found in today's schedule.");
                        UpdateStatus("AUTOMATION COMPLETE: No studietid classes to register today.");
                        UpdateAutomationState(false);
                        return;
                    }

                    var now = DateTime.Now;
                    Debug.WriteLine($"Current time for analysis: {now:HH:mm:ss}", "ANALYSIS");

                    // Analyze studietid classes
                    var studietidAnalysis = AnalyzeStudietidClasses(studietidClasses, now);

                    Debug.WriteLine("=== STU CLASS ANALYSIS ===", "ANALYSIS");
                    Debug.WriteLine($"Total STU classes: {studietidAnalysis.Total}", "ANALYSIS");
                    Debug.WriteLine($"Already registered: {studietidAnalysis.AlreadyRegistered}", "ANALYSIS");
                    Debug.WriteLine($"Upcoming (future): {studietidAnalysis.Upcoming}", "ANALYSIS");
                    Debug.WriteLine($"Current (ready to register): {studietidAnalysis.Current}", "ANALYSIS");
                    Debug.WriteLine($"Past (missed): {studietidAnalysis.Past}", "ANALYSIS");

                    UpdateStatus($"STU Analysis: {studietidAnalysis.AlreadyRegistered} registered, {studietidAnalysis.Current} ready, {studietidAnalysis.Upcoming} upcoming");

                    // Check if all studietid classes are already registered
                    if (studietidAnalysis.AlreadyRegistered == studietidAnalysis.Total)
                    {
                        Debug.WriteLine("All STU classes are registered - automation complete!", "SUCCESS");
                        UpdateStatus("SUCCESS: All studietid classes for today have been registered!");
                        UpdateStatus("AUTOMATION COMPLETE: All studietid registrations done for today.");
                        UpdateAutomationState(false);
                        return;
                    }

                    // Check if there are any studietid classes that can still be registered
                    if (studietidAnalysis.Current == 0 && studietidAnalysis.Upcoming == 0)
                    {
                        Debug.WriteLine("No more STU classes can be registered (all past deadline)", "AUTOMATION");
                        UpdateStatus("All remaining studietid classes have passed their registration window.");
                        UpdateStatus("AUTOMATION COMPLETE: No more studietid classes can be registered today.");
                        UpdateAutomationState(false);
                        return;
                    }

                    // Find the next studietid class that needs registration
                    var nextStudietidClass = FindNextStudietidClassToRegister(studietidClasses, now);

                    if (nextStudietidClass == null)
                    {
                        Debug.WriteLine("No STU classes currently need registration", "WAIT");
                        UpdateStatus("No studietid classes currently need registration. Checking again in 5 minutes...");
                        await DelayWithCountdown(TimeSpan.FromMinutes(5), "next STU check", cancellationToken);
                        continue;
                    }

                    var classDateTime = DateTime.ParseExact(nextStudietidClass.Dato, "yyyyMMdd", null)
                        .Add(TimeSpan.ParseExact(nextStudietidClass.StartKl, "HHmm", null));
                    var timeDiff = classDateTime - now;

                    Debug.WriteLine($"Next STU class: {nextStudietidClass.Fag} at {nextStudietidClass.StartKl}", "NEXT");
                    Debug.WriteLine($"Class starts in {timeDiff.TotalMinutes:F1} minutes", "NEXT");

                    UpdateStatus($"Next studietid: {nextStudietidClass.Fag} at {nextStudietidClass.StartKl} (in {timeDiff.TotalMinutes:F0} minutes)");

                    // If class is more than 15 minutes away, wait until 15 minutes before
                    if (timeDiff.TotalMinutes > 15)
                    {
                        var waitTime = timeDiff.Subtract(TimeSpan.FromMinutes(15));
                        Debug.WriteLine($"Class is {timeDiff.TotalMinutes:F0} minutes away - waiting {waitTime.TotalMinutes:F0} minutes", "WAIT");
                        UpdateStatus($"Waiting {waitTime.TotalMinutes:F0} minutes until studietid registration window opens...");
                        await DelayWithCountdown(waitTime, $"STU registration window for {nextStudietidClass.Fag}", cancellationToken);
                    }
                    // If class is within registration window (-15 to +15 minutes from start time)
                    else if (timeDiff.TotalMinutes >= -15 && timeDiff.TotalMinutes <= 15)
                    {
                        Debug.WriteLine($"STU registration window is OPEN for {nextStudietidClass.Fag}!", "REGISTER");
                        UpdateStatus($"Studietid registration window is open for {nextStudietidClass.Fag}!");

                        try
                        {
                            await MarkAttendanceAsync(nextStudietidClass, cancellationToken);

                            Debug.WriteLine($"SUCCESS: Studietid attendance registered for {nextStudietidClass.Fag}", "SUCCESS");
                            UpdateStatus($"✓ SUCCESS: Studietid attendance registered for {nextStudietidClass.Fag}");

                            Debug.WriteLine("Waiting 2 minutes for system to update...", "WAIT");
                            await DelayWithCountdown(TimeSpan.FromMinutes(2), "system update", cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"FAILED to register {nextStudietidClass.Fag}: {ex.Message}", "ERROR");
                            UpdateStatus($"✗ FAILED: Could not register studietid attendance for {nextStudietidClass.Fag}: {ex.Message}");

                            Debug.WriteLine("Waiting 1 minute before retry...", "WAIT");
                            await DelayWithCountdown(TimeSpan.FromMinutes(1), "retry", cancellationToken);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"STU class {nextStudietidClass.Fag} registration window has passed", "MISSED");
                        UpdateStatus($"Registration window passed for {nextStudietidClass.Fag}, checking for next studietid class...");
                        await DelayWithCountdown(TimeSpan.FromMinutes(1), "next STU check", cancellationToken);
                    }
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
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}", "ERROR");
                    UpdateStatus($"Error in studietid automation: {ex.Message}");
                    Debug.WriteLine("Waiting 5 minutes before retry...", "WAIT");
                    await DelayWithCountdown(TimeSpan.FromMinutes(5), "error recovery", cancellationToken);
                }
            }
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
                // Check if already registered
                if (IsStudietidAlreadyRegistered(stuClass))
                {
                    alreadyRegistered++;
                    continue;
                }

                if (DateTime.TryParseExact(stuClass.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                    TimeSpan.TryParseExact(stuClass.StartKl, "HHmm", null, out var startTime))
                {
                    var classDateTime = date.Add(startTime);
                    var timeDiff = classDateTime - now;

                    if (timeDiff.TotalMinutes > 15)
                        upcoming++;
                    else if (timeDiff.TotalMinutes >= -15)
                        current++;
                    else
                        past++;
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

            // Log first 500 characters of response for debugging
            var preview = jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString;
            Debug.WriteLine($"Response preview: {preview}", "API");

            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Debug.WriteLine("ERROR: Server returned empty response", "ERROR");
                return new ScheduleResponse { Items = new List<ScheduleItem>() };
            }

            try
            {
                Debug.WriteLine("Parsing JSON response...", "API");
                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var itemCount = scheduleResponse?.Items?.Count ?? 0;
                Debug.WriteLine($"Successfully parsed {itemCount} schedule items", "API");

                // Log each schedule item for debugging
                if (scheduleResponse?.Items != null)
                {
                    Debug.WriteLine("SCHEDULE ITEMS DETAILS:", "SCHEDULE");
                    foreach (var item in scheduleResponse.Items)
                    {
                        Debug.WriteLine($"  ID: {item.Id}, Fag: {item.Fag}, Date: {item.Dato}, Time: {item.StartKl}-{item.SluttKl}", "SCHEDULE");
                        Debug.WriteLine($"    ElevForerTilstedevaerelse: {item.ElevForerTilstedevaerelse}, Typefravaer: {item.Typefravaer}", "SCHEDULE");
                    }
                }

                return scheduleResponse ?? new ScheduleResponse { Items = new List<ScheduleItem>() };
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON parsing failed: {ex.Message}", "ERROR");
                Debug.WriteLine($"Raw JSON that failed: {jsonString}", "ERROR");
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