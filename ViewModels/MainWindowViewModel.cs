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

        public bool CanExecute(object parameter)
        {
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
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }

                // Chrome executable paths for different platforms
                var chromeExecutables = new List<string>();
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    chromeExecutables.AddRange(new[]
                    {
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    chromeExecutables.AddRange(new[]
                    {
                        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                        "/Applications/Chromium.app/Contents/MacOS/Chromium"
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    chromeExecutables.AddRange(new[]
                    {
                        "/usr/bin/google-chrome",
                        "/usr/bin/chromium-browser",
                        "/usr/bin/google-chrome-stable"
                    });
                }

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

        private async Task<bool> DownloadChromeDriverAsync(string chromeVersion)
        {
            try
            {
                UpdateStatus($"Downloading ChromeDriver for Chrome {chromeVersion}...");

                var driverFileName = GetChromeDriverFileName();
                var driverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, driverFileName);

                // Determine the correct ChromeDriver URL based on platform and Chrome version
                string chromeDriverUrl;
                string platformFolder;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    platformFolder = "win64";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Check if it's Apple Silicon or Intel Mac
                    var arch = RuntimeInformation.OSArchitecture;
                    platformFolder = arch == Architecture.Arm64 ? "mac-arm64" : "mac-x64";
                }
                else
                {
                    platformFolder = "linux64";
                }

                // For Chrome 140+, use the new Chrome for Testing API
                if (chromeVersion.StartsWith("140."))
                {
                    chromeDriverUrl = $"https://storage.googleapis.com/chrome-for-testing-public/140.0.7339.81/{platformFolder}/chromedriver-{platformFolder}.zip";
                }
                else
                {
                    // For older versions, try the legacy approach
                    chromeDriverUrl = $"https://storage.googleapis.com/chrome-for-testing-public/{chromeVersion}/{platformFolder}/chromedriver-{platformFolder}.zip";
                }

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                UpdateStatus("Downloading ChromeDriver zip file...");
                var zipBytes = await httpClient.GetByteArrayAsync(chromeDriverUrl);

                var tempZipPath = Path.Combine(Path.GetTempPath(), "chromedriver.zip");
                await File.WriteAllBytesAsync(tempZipPath, zipBytes);

                UpdateStatus("Extracting ChromeDriver...");

                // Extract the chromedriver from the zip
                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    var driverEntry = archive.Entries.FirstOrDefault(e => e.Name.StartsWith("chromedriver"));
                    if (driverEntry != null)
                    {
                        driverEntry.ExtractToFile(driverPath, overwrite: true);
                        
                        // On Unix systems, make the file executable
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
                        
                        UpdateStatus($"ChromeDriver downloaded and extracted successfully to {driverPath}!");

                        // Clean up temp file
                        File.Delete(tempZipPath);
                        return true;
                    }
                    else
                    {
                        throw new Exception("chromedriver not found in downloaded zip");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to download ChromeDriver: {ex.Message}");
                return false;
            }
        }

        private string GetChromeDriverFileName()
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

                // First, check if local chromedriver exists and is working
                if (File.Exists(localDriverPath))
                {
                    UpdateStatus("Found local chromedriver, testing compatibility...");

                    try
                    {
                        // Test the local driver
                        var testService = ChromeDriverService.CreateDefaultService(AppDomain.CurrentDomain.BaseDirectory, driverFileName);
                        testService.HideCommandPromptWindow = true;
                        
                        // macOS specific: Set a custom port to avoid conflicts
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            testService.Port = GetAvailablePort();
                        }

                        var testOptions = new ChromeOptions();
                        testOptions.AddArgument("--headless");
                        testOptions.AddArgument("--no-sandbox");
                        testOptions.AddArgument("--disable-dev-shm-usage");
                        
                        // macOS specific arguments
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            testOptions.AddArgument("--disable-gpu");
                            testOptions.AddArgument("--remote-debugging-port=0");
                        }

                        using (var testDriver = new ChromeDriver(testService, testOptions, TimeSpan.FromSeconds(30)))
                        {
                            testDriver.Navigate().GoToUrl("about:blank");
                            UpdateStatus("Local chromedriver is working correctly!");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Local chromedriver failed test: {ex.Message}");
                        UpdateStatus("Will attempt to download correct version...");

                        // Delete the incompatible driver
                        try
                        {
                            File.Delete(localDriverPath);
                        }
                        catch { }
                    }
                }

                // Try to download the correct ChromeDriver version
                if (!string.IsNullOrEmpty(chromeVersion))
                {
                    var downloadSuccess = await DownloadChromeDriverAsync(chromeVersion);
                    if (downloadSuccess)
                    {
                        return;
                    }
                }

                // Try WebDriverManager as fallback
                Exception webDriverManagerException = null;
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
                    webDriverManagerException = ex;
                    UpdateStatus($"WebDriverManager failed: {ex.Message}");
                }

                // If everything failed, provide manual instructions
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

        private string GetPlatformSpecificInstructions(string chromeVersion)
        {
            var majorVersion = !string.IsNullOrEmpty(chromeVersion) ? chromeVersion.Split('.')[0] : "140";
            var driverFileName = GetChromeDriverFileName();
            var currentFolder = AppDomain.CurrentDomain.BaseDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64";
                return $@"ChromeDriver setup failed. Manual setup required for macOS:

CHROME VERSION DETECTED: {chromeVersion ?? "Could not detect"}
ARCHITECTURE: {arch}

MANUAL SETUP STEPS FOR macOS:
1. Go to https://googlechromelabs.github.io/chrome-for-testing/
2. Find ChromeDriver version {chromeVersion ?? "140.0.7339.81"} for {arch}
3. Download the {arch} version
4. Extract chromedriver from the zip file
5. Place chromedriver (no .exe extension) in this application's folder:
   {currentFolder}
6. Open Terminal and run: chmod +x ""{Path.Combine(currentFolder, "chromedriver")}""
7. Run the application with: xattr -d com.apple.quarantine ""{Path.Combine(currentFolder, "chromedriver")}""
8. Restart the application and try again

DOWNLOAD LINK:
https://storage.googleapis.com/chrome-for-testing-public/140.0.7339.81/{arch}/chromedriver-{arch}.zip

CURRENT FOLDER: {currentFolder}
EXPECTED FILE: {driverFileName}

TROUBLESHOOTING:
- If you get 'cannot be opened because the developer cannot be verified', run:
  xattr -d com.apple.quarantine ""{Path.Combine(currentFolder, "chromedriver")}""
- Make sure chromedriver has execute permissions: chmod +x chromedriver";
            }
            else
            {
                return $@"ChromeDriver setup failed. Manual setup required:

CHROME VERSION DETECTED: {chromeVersion ?? "Could not detect"}
MAJOR VERSION: {majorVersion}

MANUAL SETUP STEPS:
1. Go to https://googlechromelabs.github.io/chrome-for-testing/
2. Find ChromeDriver version {chromeVersion ?? "140.0.7339.81"} (matches your Chrome exactly)
3. Download the correct platform version
4. Extract {driverFileName} from the zip file
5. Place {driverFileName} in this application's folder:
   {currentFolder}
6. Restart the application and try again

CURRENT FOLDER: {currentFolder}
EXPECTED FILE: {driverFileName}";
            }
        }

        private int GetAvailablePort()
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

                // Create Chrome service with proper configuration
                ChromeDriverService chromeDriverService;
                var driverFileName = GetChromeDriverFileName();
                var localDriverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, driverFileName);

                if (File.Exists(localDriverPath))
                {
                    chromeDriverService = ChromeDriverService.CreateDefaultService(AppDomain.CurrentDomain.BaseDirectory, driverFileName);
                }
                else
                {
                    chromeDriverService = ChromeDriverService.CreateDefaultService();
                }

                chromeDriverService.HideCommandPromptWindow = true;
                chromeDriverService.SuppressInitialDiagnosticInformation = true;
                
                // macOS specific: Use a custom port to avoid conflicts
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    chromeDriverService.Port = GetAvailablePort();
                    UpdateStatus($"Using port {chromeDriverService.Port} for ChromeDriver on macOS");
                }

                // Enhanced Chrome options with platform-specific settings
                var chromeOptions = new ChromeOptions();

                // Essential stability options
                chromeOptions.AddArgument("--no-sandbox");
                chromeOptions.AddArgument("--disable-dev-shm-usage");
                chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");
                chromeOptions.AddArgument("--log-level=3");
                chromeOptions.AddArgument("--disable-extensions");
                chromeOptions.AddArgument("--no-first-run");
                chromeOptions.AddArgument("--disable-default-apps");

                // Platform-specific options
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS specific options
                    chromeOptions.AddArgument("--disable-gpu");
                    chromeOptions.AddArgument("--remote-debugging-port=0"); // Let Chrome choose the port
                    chromeOptions.AddArgument("--disable-web-security");
                    chromeOptions.AddArgument("--allow-running-insecure-content");
                    chromeOptions.AddArgument("--disable-features=VizDisplayCompositor,MacV2GPUSandbox");
                }
                else
                {
                    chromeOptions.AddArgument("--disable-gpu");
                }

                // CRITICAL: Add this to prevent immediate closure
                chromeOptions.AddArgument("--disable-background-timer-throttling");
                chromeOptions.AddArgument("--disable-backgrounding-occluded-windows");
                chromeOptions.AddArgument("--disable-renderer-backgrounding");

                // Chrome version specific stability options
                chromeOptions.AddArgument("--disable-search-engine-choice-screen");

                // Remove automation detection
                chromeOptions.AddExcludedArgument("enable-automation");
                chromeOptions.AddAdditionalOption("useAutomationExtension", false);

                // Set window properties
                chromeOptions.AddArgument("--start-maximized");

                // Updated user agent for Chrome 140
                chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");

                try
                {
                    UpdateStatus("Creating Chrome WebDriver instance...");
                    UpdateStatus("If browser closes immediately, this indicates ChromeDriver compatibility issues.");

                    // Create WebDriver with extended timeout
                    _webDriver = new ChromeDriver(chromeDriverService, chromeOptions, TimeSpan.FromSeconds(120));

                    // Immediately check if browser is responsive
                    UpdateStatus("Testing browser responsiveness...");
                    var title = _webDriver.Title; // This will throw if browser closed immediately

                    UpdateStatus("Chrome browser opened successfully!");

                    // Set timeouts
                    _webDriver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(15);
                    _webDriver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(45);

                    UpdateStatus("Navigating to login page...");
                    _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                    // Wait longer for page to load
                    await Task.Delay(8000);

                    // Check if navigation was successful
                    var currentUrl = _webDriver.Url;
                    if (currentUrl.Contains("login") || currentUrl.Contains("iskole.net"))
                    {
                        UpdateStatus("Login page loaded successfully!");
                        UpdateStatus("=== INSTRUCTIONS ===");
                        UpdateStatus("1. Complete the login process in the browser window");
                        UpdateStatus("2. After successful login, navigate to:");
                        UpdateStatus("   https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");
                        UpdateStatus("3. The system will automatically detect when you reach this page");
                        UpdateStatus("4. DO NOT close the browser window!");
                        UpdateStatus("==================");

                        // Wait for user to complete login and navigate to the correct page
                        var loginSuccessful = await WaitForSpecificUrlAsync();

                        if (loginSuccessful)
                        {
                            UpdateStatus("SUCCESS: Login detected and user is on the correct page!");
                            await Task.Delay(3000);

                            UpdateStatus("Extracting authentication cookies...");
                            var cookies = _webDriver.Manage().Cookies.AllCookies.ToList();

                            if (cookies.Any())
                            {
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
                                    UpdateStatus("Please ensure you are fully logged in and try again.");
                                }
                            }
                            else
                            {
                                UpdateStatus("ERROR: No cookies found in browser session.");
                                UpdateStatus("Please ensure you are properly logged in.");
                            }
                        }
                        else
                        {
                            UpdateStatus("TIMEOUT: User did not reach the required page within the time limit.");
                            UpdateStatus("Please try again and make sure to navigate to the timetable page.");
                        }
                    }
                    else
                    {
                        throw new Exception($"Failed to navigate to login page. Current URL: {currentUrl}");
                    }
                }
                catch (WebDriverException webEx) when (webEx.Message.Contains("chrome not reachable"))
                {
                    UpdateStatus("ERROR: Chrome browser was closed unexpectedly during startup.");
                    UpdateStatus("TROUBLESHOOTING STEPS:");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        UpdateStatus("1. Check if Chrome is installed in /Applications/Google Chrome.app");
                        UpdateStatus("2. Try running: xattr -d com.apple.quarantine chromedriver");
                        UpdateStatus("3. Make sure chromedriver has execute permissions: chmod +x chromedriver");
                        UpdateStatus("4. Close all Chrome instances and try again");
                    }
                    else
                    {
                        UpdateStatus("1. Ensure Chrome browser is installed and updated");
                        UpdateStatus("2. Close all Chrome instances and try again");
                        UpdateStatus("3. Run application as administrator if needed");
                        UpdateStatus("4. Check antivirus isn't blocking ChromeDriver");
                    }
                    throw new Exception("Browser closed during startup - see troubleshooting steps above");
                }
                catch (WebDriverException webEx) when (webEx.Message.Contains("session not created"))
                {
                    var chromeVersion = await GetChromeVersionAsync();
                    UpdateStatus("ERROR: Could not create Chrome session - ChromeDriver version mismatch.");
                    UpdateStatus($"Your Chrome version: {chromeVersion ?? "unknown"}");
                    UpdateStatus("SOLUTION:");
                    UpdateStatus(GetPlatformSpecificInstructions(chromeVersion));
                    throw new Exception($"ChromeDriver version mismatch with Chrome {chromeVersion}");
                }
                catch (WebDriverException webEx) when (webEx.Message.Contains("unknown error: DevToolsActivePort"))
                {
                    UpdateStatus("ERROR: Chrome failed to start properly.");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        UpdateStatus("SOLUTION for macOS:");
                        UpdateStatus("1. Close all Chrome instances: pkill -f Chrome");
                        UpdateStatus("2. Clear Chrome user data if needed");
                        UpdateStatus("3. Try running chromedriver manually to test: ./chromedriver --version");
                    }
                    else
                    {
                        UpdateStatus("SOLUTION: Close all Chrome instances and try again.");
                        UpdateStatus("If problem persists, restart your computer.");
                    }
                    throw new Exception("Chrome startup failure - close all Chrome instances and retry");
                }
                catch (WebDriverException webEx)
                {
                    UpdateStatus($"WebDriver error: {webEx.Message}");
                    UpdateStatus("This usually indicates a ChromeDriver or Chrome browser issue.");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        UpdateStatus("macOS: Try running 'xattr -d com.apple.quarantine chromedriver' and 'chmod +x chromedriver'");
                    }
                    throw new Exception($"WebDriver error: {webEx.Message}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Unexpected error during browser setup: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Cookie extraction failed: {ex.Message}");
                throw;
            }
            finally
            {
                // Keep browser open longer for user to see results
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
                    // Don't rethrow here - this is cleanup
                }
            }
        }

        private async Task<bool> WaitForSpecificUrlAsync()
        {
            var maxWaitTime = TimeSpan.FromMinutes(15); // Longer timeout for user to complete login
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

                    // Check if user has reached the target URL exactly
                    if (currentUrl.Contains("isFeideinnlogget=true") && currentUrl.Contains("ojr=timeplan"))
                    {
                        UpdateStatus("SUCCESS! User has reached the timetable page.");
                        return true;
                    }

                    // Also accept if they're on a related logged-in page
                    if (currentUrl.Contains("isFeideinnlogget=true"))
                    {
                        UpdateStatus("User is logged in but not on timetable page yet.");
                        UpdateStatus("Please navigate to the timetable (timeplan) page to continue.");
                    }
                    else if (!currentUrl.Contains("login"))
                    {
                        UpdateStatus("User appears to be past login page...");
                    }
                    else
                    {
                        UpdateStatus("User is still on login page...");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error checking URL: {ex.Message}");
                }

                await Task.Delay(checkInterval);
            }

            UpdateStatus("Timeout reached. User did not reach the required page in time.");
            return false;
        }

        private async Task SaveCookiesToFileAsync(IList<OpenQA.Selenium.Cookie> seleniumCookies)
        {
            try
            {
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

                var cookieString = string.Join("; ", seleniumCookies.Select(c => $"{c.Name}={c.Value}"));
                await File.WriteAllTextAsync("cookies.txt", cookieString);

                var summaryLines = new List<string>
                {
                    $"Cookie extraction completed at: {DateTime.Now}",
                    $"Total cookies extracted: {seleniumCookies.Count}",
                    "",
                    "Cookie names:"
                };
                summaryLines.AddRange(seleniumCookies.Select(c => $"  - {c.Name} = {c.Value.Substring(0, Math.Min(20, c.Value.Length))}..."));

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

                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var invalidCookies = requiredCookies.Where(rc => !cookieDict.ContainsKey(rc) ||
                    cookieDict[rc].StartsWith("YOUR_") ||
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                if (invalidCookies.Any())
                {
                    return null;
                }

                return cookieDict;
            }
            catch
            {
                return null;
            }
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning)
                return;

            UpdateAutomationState(true);
            UpdateStatus("Initializing automation...");

            try
            {
                UpdateStatus("[1/3] Checking authentication cookies...");
                var existingCookies = await LoadExistingCookiesAsync();

                if (existingCookies == null)
                {
                    UpdateStatus("[1/3] No valid cookies found. Starting browser login process...");

                    try
                    {
                        await LoginAndExtractCookiesAsync();

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

                UpdateStatus("[2/3] Validating authentication with server...");
                try
                {
                    var testSchedule = await GetScheduleDataAsync(CancellationToken.None);
                    if (testSchedule?.Items == null)
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

                    UpdateStatus("[2/4] Fetching schedule data from server...");
                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    if (scheduleData?.Items == null || !scheduleData.Items.Any())
                    {
                        UpdateStatus("[2/4] No schedule data found or empty response from server");
                        UpdateStatus("STOPPING AUTOMATION: No classes found in schedule - nothing to monitor");
                        UpdateAutomationState(false);
                        return;
                    }

                    UpdateStatus($"[2/4] SUCCESS: Found {scheduleData.Items.Count} schedule items");

                    var now = DateTime.Now;
                    var today = now.Date;
                    var relevantClasses = scheduleData.Items
                        .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                      date >= today.AddDays(-1))
                        .ToList();

                    if (!relevantClasses.Any())
                    {
                        UpdateStatus("[2/4] No current or future classes found in schedule data");
                        UpdateStatus("STOPPING AUTOMATION: No classes to monitor - all found classes are from the past");
                        UpdateAutomationState(false);
                        return;
                    }

                    var actionableClasses = relevantClasses
                        .Where(item => DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                                      TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var time) &&
                                      date.Add(time) > now.AddMinutes(-45))
                        .ToList();

                    if (!actionableClasses.Any())
                    {
                        UpdateStatus($"[2/4] Found {relevantClasses.Count} classes but none are actionable");
                        UpdateStatus("STOPPING AUTOMATION: No classes within attendance window (45min before to 15min after class time)");

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

                    var nextActionable = GetNextActionableClass(scheduleData.Items, now);
                    if (nextActionable != null)
                    {
                        UpdateStatus($"[3/4] Next action: {nextActionable}");
                    }

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

                using var clientHandler = new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                };

                using var httpClient = new HttpClient(clientHandler);

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Sec-Ch-Ua-Platform", "\"macOS\"");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Sec-Ch-Ua", "\"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"140\"");
                
                // Use appropriate User-Agent for the platform
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
                }
                else
                {
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
                }
                
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
            
            // Platform-specific headers
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                request.Headers.Add("Sec-Ch-Ua-Platform", "\"macOS\"");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
            }
            else
            {
                request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.7339.81 Safari/537.36");
            }
            
            request.Headers.Add("Accept-Language", "nb-NO,nb;q=0.9");
            request.Headers.Add("Sec-Ch-Ua", "\"Chromium\";v=\"140\", \"Not;A=Brand\";v=\"99\"");
            request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
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