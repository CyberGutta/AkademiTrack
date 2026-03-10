using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;

namespace AkademiTrack.Services
{
    public class ChromeDriverManager
    {
        private static readonly object _lock = new object();
        private static bool _cacheInitialized = false;
        
        // Hierarchical error codes for precise debugging
        // Format: CD-[Stage].[Step].[SubStep]
        
        // Stage 1: Initial Setup
        public const string ERROR_CODE_SETUP_START = "CD-1.0";
        public const string ERROR_CODE_SETUP_EXCEPTION = "CD-1.1";
        
        // Stage 2: Test ChromeDriver
        public const string ERROR_CODE_TEST_START = "CD-2.0";
        public const string ERROR_CODE_TEST_OPTIONS_CREATION = "CD-2.1";
        public const string ERROR_CODE_TEST_SERVICE_CREATION = "CD-2.2";
        public const string ERROR_CODE_TEST_SERVICE_PATH_INVALID = "CD-2.2.1";
        public const string ERROR_CODE_TEST_SERVICE_EXECUTABLE_MISSING = "CD-2.2.2";
        public const string ERROR_CODE_TEST_DRIVER_CREATION = "CD-2.3";
        public const string ERROR_CODE_TEST_DRIVER_TIMEOUT = "CD-2.3.1";
        public const string ERROR_CODE_TEST_DRIVER_NOT_FOUND = "CD-2.3.2";
        public const string ERROR_CODE_TEST_DRIVER_PERMISSION_DENIED = "CD-2.3.3";
        public const string ERROR_CODE_TEST_DRIVER_INCOMPATIBLE = "CD-2.3.4";
        public const string ERROR_CODE_TEST_NAVIGATION = "CD-2.4";
        public const string ERROR_CODE_TEST_NAVIGATION_TIMEOUT = "CD-2.4.1";
        public const string ERROR_CODE_TEST_UNEXPECTED = "CD-2.9";
        
        // Stage 3: Fallback Setup
        public const string ERROR_CODE_FALLBACK_START = "CD-3.0";
        public const string ERROR_CODE_FALLBACK_WEBDRIVER_MANAGER = "CD-3.1";
        public const string ERROR_CODE_FALLBACK_RETEST_FAILED = "CD-3.2";
        public const string ERROR_CODE_FALLBACK_EXCEPTION = "CD-3.9";
        
        // Stage 4: Complete Failure
        public const string ERROR_CODE_COMPLETE_FAILURE = "CD-4.0";
        
        public static string? LastErrorCode { get; private set; }
        public static string? LastErrorMessage { get; private set; }
        public static string? LastErrorDetails { get; private set; }
        public static Exception? LastException { get; private set; }
        
        // Track the FIRST error that occurred
        private static string? FirstErrorCode { get; set; }
        private static string? FirstErrorMessage { get; set; }
        private static Exception? FirstException { get; set; }
        
        private static void SetError(string code, string message, Exception? ex = null, string? additionalDetails = null)
        {
            // Save the FIRST error that occurred
            if (FirstErrorCode == null)
            {
                FirstErrorCode = code;
                FirstErrorMessage = message;
                FirstException = ex;
            }
            
            LastErrorCode = code;
            LastErrorMessage = message;
            LastException = ex;
            
            var details = "";
            if (ex != null)
            {
                details += $"Exception Type: {ex.GetType().FullName}\n";
                details += $"Exception Message: {ex.Message}\n";
                if (ex.InnerException != null)
                {
                    details += $"Inner Exception: {ex.InnerException.GetType().FullName}\n";
                    details += $"Inner Message: {ex.InnerException.Message}\n";
                }
                details += $"Stack Trace:\n{ex.StackTrace}\n";
            }
            
            if (!string.IsNullOrEmpty(additionalDetails))
            {
                details += $"\nAdditional Info:\n{additionalDetails}";
            }
            
            LastErrorDetails = details;
            
            Debug.WriteLine($"[ChromeDriverManager] Error [{code}]: {message}");
            if (!string.IsNullOrEmpty(details))
            {
                Debug.WriteLine($"[ChromeDriverManager] Details:\n{details}");
            }
        }
        
        
        private static void EnsureCacheInitialized()
        {
            if (_cacheInitialized)
                return;
                
            lock (_lock)
            {
                if (_cacheInitialized)
                    return;
                    
                try
                {
                    // Use platform-appropriate cache location
                    string cacheBasePath;
                    
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // macOS: Use ~/Library/Application Support/AkademiTrack/selenium-cache
                        cacheBasePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "Library",
                            "Application Support",
                            "AkademiTrack",
                            "selenium-cache"
                        );
                    }
                    else
                    {
                        // Windows/Linux: Use AppData/AkademiTrack/selenium-cache
                        cacheBasePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "AkademiTrack",
                            "selenium-cache"
                        );
                    }
                    
                    // Create the cache directory if it doesn't exist
                    Directory.CreateDirectory(cacheBasePath);
                    
                    // Set the environment variable for Selenium Manager
                    Environment.SetEnvironmentVariable("SE_CACHE_PATH", cacheBasePath);
                    
                    Debug.WriteLine($"[ChromeDriverManager] Initialized Selenium cache at: {cacheBasePath}");
                    _cacheInitialized = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChromeDriverManager] Warning: Could not initialize cache location: {ex.Message}");
                    // Don't fail - Selenium will use default location
                }
            }
        }
        
        public static string GetFullErrorReport()
        {
            var report = "=== ChromeDriver Error Report ===\n\n";
            
            // Show the FIRST error (root cause)
            if (FirstErrorCode != null)
            {
                report += $"Root Cause Error Code: {FirstErrorCode}\n";
                report += $"Root Cause Message: {FirstErrorMessage ?? "No message"}\n\n";
            }
            
            // Show the LAST error (final state)
            report += $"Final Error Code: {LastErrorCode ?? "UNKNOWN"}\n";
            report += $"Final Error Message: {LastErrorMessage ?? "No error message"}\n\n";
            
            if (FirstException != null || LastException != null)
            {
                report += "=== Exception Details ===\n";
                
                var exToShow = FirstException ?? LastException;
                report += $"Type: {exToShow!.GetType().FullName}\n";
                report += $"Message: {exToShow.Message}\n";
                
                if (exToShow.InnerException != null)
                {
                    report += $"\nInner Exception Type: {exToShow.InnerException.GetType().FullName}\n";
                    report += $"Inner Exception Message: {exToShow.InnerException.Message}\n";
                }
                
                report += $"\nStack Trace:\n{exToShow.StackTrace}\n";
            }
            
            if (!string.IsNullOrEmpty(LastErrorDetails))
            {
                report += "\n=== Technical Details ===\n";
                report += LastErrorDetails;
            }
            
            report += "\n=== System Information ===\n";
            report += $"OS: {RuntimeInformation.OSDescription}\n";
            report += $"Architecture: {RuntimeInformation.OSArchitecture}\n";
            report += $"Framework: {RuntimeInformation.FrameworkDescription}\n";
            report += $"Chrome Version: {GetChromeVersion() ?? "Not found"}\n";
            
            return report;
        }
        
        // Allow AuthenticationService to set errors too
        public static void SetAuthenticationError(string code, string message, Exception? ex = null)
        {
            SetError(code, message, ex);
        }
        
        /// <summary>
        /// Ensures ChromeDriver is available and returns the path to the driver executable.
        /// Uses Selenium Manager (built into Selenium 4.6+) for automatic driver management.
        /// Falls back to WebDriverManager.Net if needed.
        /// </summary>
        public static async Task<bool> EnsureChromeDriverInstalledAsync(IProgress<string>? progress = null)
        {
            try
            {
                // Initialize cache location FIRST before any ChromeDriver operations
                EnsureCacheInitialized();
                
                Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_SETUP_START}] Starting ChromeDriver setup...");
                progress?.Report("Checking ChromeDriver availability...");
                
                // Clear previous errors
                LastErrorCode = null;
                LastErrorMessage = null;
                LastErrorDetails = null;
                LastException = null;
                FirstErrorCode = null;
                FirstErrorMessage = null;
                FirstException = null;
                
                // First, try to create a ChromeDriver instance to test if everything works
                // Selenium Manager (built into Selenium 4.6+) will automatically download the driver if needed
                if (await TestChromeDriverAsync())
                {
                    Debug.WriteLine("[ChromeDriverManager] ChromeDriver is working properly");
                    progress?.Report("ChromeDriver ready");
                    return true;
                }
                
                Debug.WriteLine($"[ChromeDriverManager] ChromeDriver test failed with error: {LastErrorCode}");
                Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_FALLBACK_START}] Attempting fallback setup...");
                progress?.Report("Setting up ChromeDriver...");
                
                // Fallback: Use WebDriverManager.Net for explicit driver management
                var setupResult = await SetupChromeDriverWithWebDriverManagerAsync(progress);
                
                if (!setupResult)
                {
                    // Don't overwrite the original error - just add context
                    var originalError = FirstErrorCode ?? LastErrorCode;
                    var originalMessage = FirstErrorMessage ?? LastErrorMessage;
                    
                    Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_COMPLETE_FAILURE}] All attempts failed. Root cause: {originalError}");
                }
                
                return setupResult;
            }
            catch (Exception ex)
            {
                SetError(ERROR_CODE_SETUP_EXCEPTION, 
                       "Unexpected exception during ChromeDriver setup",
                       ex);
                progress?.Report($"Setup error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test if ChromeDriver is working by creating a headless instance
        /// </summary>
        private static async Task<bool> TestChromeDriverAsync()
        {
            return await Task.Run(() =>
            {
                ChromeDriverService? service = null;
                ChromeDriver? driver = null;
                
                try
                {
                    Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_TEST_START}] Starting ChromeDriver test...");
                    
                    // Step 2.1: Create Chrome options
                    ChromeOptions options;
                    try
                    {
                        options = new ChromeOptions();
                        options.AddArgument("--headless=new");
                        options.AddArgument("--no-sandbox");
                        options.AddArgument("--disable-dev-shm-usage");
                        options.AddArgument("--disable-gpu");
                        options.AddArgument("--disable-web-security");
                        options.AddArgument("--disable-features=VizDisplayCompositor");
                        options.AddArgument("--disable-extensions");
                        options.AddArgument("--disable-plugins");
                        options.AddArgument("--disable-images");
                        options.AddArgument("--disable-javascript");
                        options.AddArgument("--disable-default-apps");
                        options.AddArgument("--disable-background-timer-throttling");
                        options.AddArgument("--disable-backgrounding-occluded-windows");
                        options.AddArgument("--disable-renderer-backgrounding");
                        options.AddArgument("--disable-background-networking");
                        options.AddArgument("--no-first-run");
                        options.AddArgument("--no-default-browser-check");
                        Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_TEST_OPTIONS_CREATION}] Chrome options created");
                    }
                    catch (Exception ex)
                    {
                        SetError(ERROR_CODE_TEST_OPTIONS_CREATION, 
                               "Failed to create Chrome options",
                               ex);
                        return false;
                    }
                    
                    // Step 2.2: Create ChromeDriverService
                    Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_TEST_SERVICE_CREATION}] Creating ChromeDriverService...");
                    try
                    {
                        service = ChromeDriverService.CreateDefaultService();
                        service.HideCommandPromptWindow = true;
                        service.SuppressInitialDiagnosticInformation = true;
                        
                        Debug.WriteLine($"[ChromeDriverManager] Service created successfully");
                        Debug.WriteLine($"[ChromeDriverManager]   Path: {service.DriverServicePath ?? "(empty)"}");
                        Debug.WriteLine($"[ChromeDriverManager]   Executable: {service.DriverServiceExecutableName ?? "(empty)"}");
                    }
                    catch (Exception ex)
                    {
                        SetError(ERROR_CODE_TEST_SERVICE_CREATION,
                               "Failed to create ChromeDriverService",
                               ex);
                        return false;
                    }
                    
                    // Step 2.3: Create ChromeDriver instance
                    Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_TEST_DRIVER_CREATION}] Creating ChromeDriver instance...");
                    try
                    {
                        driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(10));
                        Debug.WriteLine("[ChromeDriverManager] ChromeDriver instance created successfully");
                    }
                    catch (WebDriverTimeoutException ex)
                    {
                        SetError(ERROR_CODE_TEST_DRIVER_TIMEOUT,
                               "ChromeDriver creation timed out after 10 seconds",
                               ex,
                               $"Service Path: {service?.DriverServicePath}\nService Exe: {service?.DriverServiceExecutableName}");
                        return false;
                    }
                    catch (DriverServiceNotFoundException ex)
                    {
                        SetError(ERROR_CODE_TEST_DRIVER_NOT_FOUND,
                               "ChromeDriver executable not found",
                               ex,
                               $"Service Path: {service?.DriverServicePath}\nService Exe: {service?.DriverServiceExecutableName}");
                        return false;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        SetError(ERROR_CODE_TEST_DRIVER_PERMISSION_DENIED,
                               "Permission denied when trying to execute ChromeDriver",
                               ex,
                               $"Service Path: {service?.DriverServicePath}\nService Exe: {service?.DriverServiceExecutableName}\n\nThis may be caused by:\n- macOS Gatekeeper blocking the executable\n- Insufficient file permissions\n- Security software blocking execution");
                        return false;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Check if this is a version mismatch error
                        if (ex.Message.Contains("This version of ChromeDriver only supports Chrome version"))
                        {
                            Debug.WriteLine($"[ChromeDriverManager] Detected ChromeDriver version mismatch. Clearing all caches and retrying...");
                            
                            // Clear ALL possible cache locations
                            var cacheLocations = new List<string>();
                            
                            // 1. Default ~/.cache/selenium
                            var defaultCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "selenium");
                            cacheLocations.Add(defaultCache);
                            
                            // 2. Alternative cache in Application Support
                            var appSupportCache = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "AkademiTrack",
                                "selenium-cache"
                            );
                            cacheLocations.Add(appSupportCache);
                            
                            // 3. macOS-specific Library/Application Support
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                            {
                                var macCache = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    "Library",
                                    "Application Support",
                                    "AkademiTrack",
                                    "selenium-cache"
                                );
                                cacheLocations.Add(macCache);
                            }
                            
                            // Clear all cache locations
                            bool anyCleared = false;
                            foreach (var cachePath in cacheLocations)
                            {
                                if (TryFixCachePermissions(cachePath))
                                {
                                    anyCleared = true;
                                    Debug.WriteLine($"[ChromeDriverManager] Cleared cache: {cachePath}");
                                }
                            }
                            
                            if (anyCleared)
                            {
                                Debug.WriteLine($"[ChromeDriverManager] Caches cleared. Setting up alternative cache and retrying...");
                                
                                // Use the alternative cache location that we know works
                                var workingCache = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) 
                                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AkademiTrack", "selenium-cache")
                                    : appSupportCache;
                                
                                Environment.SetEnvironmentVariable("SE_CACHE_PATH", workingCache);
                                Directory.CreateDirectory(workingCache);
                                Debug.WriteLine($"[ChromeDriverManager] Set SE_CACHE_PATH to: {workingCache}");
                                
                                // Retry once after clearing cache
                                try
                                {
                                    // Recreate service to pick up new driver
                                    service = ChromeDriverService.CreateDefaultService();
                                    service.HideCommandPromptWindow = true;
                                    service.SuppressInitialDiagnosticInformation = true;
                                    
                                    driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(10));
                                    Debug.WriteLine("[ChromeDriverManager] ChromeDriver instance created successfully after cache clear");
                                    // Continue with navigation test
                                    goto NavigationTest;
                                }
                                catch (Exception retryEx)
                                {
                                    SetError(ERROR_CODE_TEST_DRIVER_INCOMPATIBLE,
                                           "ChromeDriver is incompatible with installed Chrome version (retry failed)",
                                           retryEx,
                                           $"Chrome Version: {GetChromeVersion() ?? "Unknown"}\n\n" +
                                           $"Attempted to download correct ChromeDriver version but failed.\n" +
                                           $"Please update Chrome to the latest version and restart the app.");
                                    return false;
                                }
                            }
                            else
                            {
                                SetError(ERROR_CODE_TEST_DRIVER_INCOMPATIBLE,
                                       "ChromeDriver is incompatible with installed Chrome version (cache clear failed)",
                                       ex,
                                       $"Chrome Version: {GetChromeVersion() ?? "Unknown"}\n\n" +
                                       $"Could not clear cache to download correct ChromeDriver version.\n" +
                                       $"Please update Chrome to the latest version and restart the app.");
                                return false;
                            }
                        }
                        else
                        {
                            SetError(ERROR_CODE_TEST_DRIVER_INCOMPATIBLE,
                                   "ChromeDriver is incompatible with installed Chrome version",
                                   ex,
                                   $"Chrome Version: {GetChromeVersion() ?? "Unknown"}\nService Path: {service?.DriverServicePath}\nService Exe: {service?.DriverServiceExecutableName}");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Check if this is a cache permission error
                        var errorMessage = ex.Message + (ex.InnerException?.Message ?? "");
                        if (errorMessage.Contains("Permission denied") && errorMessage.Contains(".cache"))
                        {
                            Debug.WriteLine($"[ChromeDriverManager] Detected cache permission error. Using alternative cache location...");
                            
                            // Use an alternative cache location in the app's data folder
                            var appDataCache = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "AkademiTrack",
                                "selenium-cache"
                            );
                            
                            // Set environment variable to override Selenium's cache location
                            Environment.SetEnvironmentVariable("SE_CACHE_PATH", appDataCache);
                            Debug.WriteLine($"[ChromeDriverManager] Set alternative cache path: {appDataCache}");
                            
                            // Ensure the directory exists
                            Directory.CreateDirectory(appDataCache);
                            
                            // Retry with alternative cache location
                            try
                            {
                                // Recreate service to pick up new environment variable
                                service = ChromeDriverService.CreateDefaultService();
                                service.HideCommandPromptWindow = true;
                                service.SuppressInitialDiagnosticInformation = true;
                                
                                driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(10));
                                Debug.WriteLine("[ChromeDriverManager] ChromeDriver instance created successfully with alternative cache");
                                // Continue with navigation test
                                goto NavigationTest;
                            }
                            catch (Exception retryEx)
                            {
                                SetError(ERROR_CODE_TEST_DRIVER_CREATION,
                                       "Failed to create ChromeDriver instance even with alternative cache location",
                                       retryEx,
                                       $"Alternative cache: {appDataCache}");
                                return false;
                            }
                        }
                        else
                        {
                            SetError(ERROR_CODE_TEST_DRIVER_CREATION,
                                   "Failed to create ChromeDriver instance",
                                   ex,
                                   $"Service Path: {service?.DriverServicePath}\nService Exe: {service?.DriverServiceExecutableName}");
                        }
                        return false;
                    }
                    
                    NavigationTest:
                    // Step 2.4: Test navigation
                    Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_TEST_NAVIGATION}] Testing navigation...");
                    
                    if (driver == null)
                    {
                        SetError(ERROR_CODE_TEST_NAVIGATION,
                               "Driver is null before navigation test",
                               null);
                        return false;
                    }
                    
                    try
                    {
                        driver.Navigate().GoToUrl("data:text/html,<html><body>Test</body></html>");
                        Debug.WriteLine("[ChromeDriverManager] Navigation successful");
                    }
                    catch (WebDriverTimeoutException ex)
                    {
                        SetError(ERROR_CODE_TEST_NAVIGATION_TIMEOUT,
                               "Navigation timed out",
                               ex);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        SetError(ERROR_CODE_TEST_NAVIGATION,
                               "Failed to navigate to test page",
                               ex);
                        return false;
                    }
                    
                    Debug.WriteLine("[ChromeDriverManager] All tests passed successfully!");
                    return true;
                }
                catch (Exception ex)
                {
                    SetError(ERROR_CODE_TEST_UNEXPECTED,
                           "Unexpected error during ChromeDriver test",
                           ex);
                    return false;
                }
                finally
                {
                    try
                    {
                        driver?.Quit();
                        driver?.Dispose();
                        service?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChromeDriverManager] Warning: Error during cleanup: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// Setup ChromeDriver using WebDriverManager.Net as fallback
        /// </summary>
        private static async Task<bool> SetupChromeDriverWithWebDriverManagerAsync(IProgress<string>? progress = null)
        {
            try
            {
                Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_FALLBACK_START}] Starting fallback setup...");
                progress?.Report("Setting up ChromeDriver...");
                
                await Task.Run(() =>
                {
                    try
                    {
                        Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_FALLBACK_WEBDRIVER_MANAGER}] Using WebDriverManager.Net...");
                        new DriverManager().SetUpDriver(new ChromeConfig());
                        Debug.WriteLine("[ChromeDriverManager] ✓ WebDriverManager.Net completed");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChromeDriverManager] WebDriverManager.Net failed: {ex.Message}");
                        // This is expected for Chrome 115+, Selenium Manager will handle it
                    }
                });
                
                progress?.Report("ChromeDriver setup complete");
                
                // Test again after setup
                Debug.WriteLine($"[ChromeDriverManager] [{ERROR_CODE_FALLBACK_RETEST_FAILED}] Re-testing ChromeDriver...");
                var retestResult = await TestChromeDriverAsync();
                
                if (!retestResult)
                {
                    SetError(ERROR_CODE_FALLBACK_RETEST_FAILED,
                           "ChromeDriver still not working after fallback setup",
                           LastException,
                           $"Previous error: {LastErrorCode}");
                }
                
                return retestResult;
            }
            catch (Exception ex)
            {
                SetError(ERROR_CODE_FALLBACK_EXCEPTION,
                       "Exception during fallback setup",
                       ex);
                progress?.Report($"Setup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if ChromeDriver is available WITHOUT downloading it
        /// </summary>
        public static async Task<bool> IsChromeDriverAvailableAsync()
        {
            try
            {
                return await TestChromeDriverAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a configured ChromeDriver instance for automation
        /// </summary>
        public static ChromeDriver CreateChromeDriver(bool headless = true, string? downloadPath = null)
        {
            // Ensure cache is initialized before creating driver
            EnsureCacheInitialized();
            
            var options = new ChromeOptions();
            
            // Basic Chrome options for automation
            if (headless)
            {
                options.AddArgument("--headless=new"); // Use new headless mode
            }
            
            // macOS: Prevent Chrome from appearing in Dock
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // These arguments help prevent Dock icon on macOS
                options.AddArgument("--disable-features=TranslateUI");
                options.AddArgument("--disable-features=Translate");
            }
            
            // Security and performance options
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--disable-features=VizDisplayCompositor");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-plugins");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-renderer-backgrounding");
            options.AddArgument("--disable-background-networking");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-ipc-flooding-protection");
            
            // Security and performance options
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--disable-features=VizDisplayCompositor");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-plugins");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-renderer-backgrounding");
            options.AddArgument("--disable-background-networking");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-ipc-flooding-protection");
            
            // SPEED OPTIMIZATIONS - Block what we can
            options.AddArgument("--disable-images");
            options.AddArgument("--blink-settings=imagesEnabled=false");
            
            // User agent to mimic a real browser
            options.AddArgument("--user-agent=Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            // Window size for consistent rendering
            options.AddArgument("--window-size=1920,1080");
            
            // Download preferences and resource blocking (CSS cannot be blocked in Chrome)
            var prefs = new Dictionary<string, object>
            {
                ["profile.default_content_settings.popups"] = 0,
                ["profile.default_content_setting_values.notifications"] = 2,
                
                // Block what Chrome allows us to block
                ["profile.managed_default_content_settings.images"] = 2,           // Block images
                ["profile.default_content_setting_values.plugins"] = 2,            // Block plugins
                ["profile.default_content_setting_values.media_stream"] = 2,       // Block media
                ["profile.default_content_setting_values.geolocation"] = 2,        // Block location
                ["profile.default_content_setting_values.automatic_downloads"] = 2 // Block auto downloads
            };
            
            if (!string.IsNullOrEmpty(downloadPath))
            {
                prefs["download.default_directory"] = downloadPath;
                prefs["download.prompt_for_download"] = false;
                prefs["download.directory_upgrade"] = true;
                prefs["safebrowsing.enabled"] = false;
            }
            
            foreach (var pref in prefs)
            {
                options.AddUserProfilePreference(pref.Key, pref.Value);
            }
            
            // Chrome service configuration
            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;
            
            // Create and return the driver
            var driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(60));
            
            Debug.WriteLine("[ChromeDriverManager] ✅ Created ChromeDriver with image blocking (CSS cannot be blocked in Chrome)");
            return driver;
        }

        /// <summary>
        /// Get Chrome browser version for debugging
        /// </summary>
        public static string? GetChromeVersion()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = "query \"HKEY_CURRENT_USER\\Software\\Google\\Chrome\\BLBeacon\" /v version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        var lines = output.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("version") && line.Contains("REG_SZ"))
                            {
                                var parts = line.Split(new[] { "REG_SZ" }, StringSplitOptions.None);
                                if (parts.Length > 1)
                                {
                                    return parts[1].Trim();
                                }
                            }
                        }
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        return output.Trim().Replace("Google Chrome ", "");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "google-chrome",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        return output.Trim().Replace("Google Chrome ", "");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeDriverManager] Failed to get Chrome version: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Attempts to fix cache folder permissions by deleting and recreating it
        /// </summary>
        
                /// <summary>
                /// Attempts to fix cache folder permissions by deleting and recreating it
                /// </summary>
                private static bool TryFixCachePermissions(string seleniumCachePath)
                {
                    try
                    {
                        Debug.WriteLine($"[ChromeDriverManager] Attempting to clear cache: {seleniumCachePath}");

                        // If the selenium cache folder exists, try to delete it
                        if (Directory.Exists(seleniumCachePath))
                        {
                            Debug.WriteLine($"[ChromeDriverManager] Cache folder exists, attempting to delete...");
                            try
                            {
                                // Try to delete all files first, then the directory
                                var files = Directory.GetFiles(seleniumCachePath, "*", SearchOption.AllDirectories);
                                foreach (var file in files)
                                {
                                    try
                                    {
                                        File.SetAttributes(file, FileAttributes.Normal);
                                        File.Delete(file);
                                    }
                                    catch (Exception fileEx)
                                    {
                                        Debug.WriteLine($"[ChromeDriverManager] Could not delete file {file}: {fileEx.Message}");
                                    }
                                }
                                
                                // Now delete the directory structure
                                Directory.Delete(seleniumCachePath, true);
                                Debug.WriteLine($"[ChromeDriverManager] Successfully deleted cache folder");
                            }
                            catch (UnauthorizedAccessException uaEx)
                            {
                                Debug.WriteLine($"[ChromeDriverManager] Permission denied deleting cache: {uaEx.Message}");
                                // Don't return false yet - we might still be able to use an alternative location
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ChromeDriverManager] Error deleting cache: {ex.Message}");
                                // Don't return false yet - we might still be able to use an alternative location
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[ChromeDriverManager] Cache folder does not exist");
                        }

                        // Try to create the cache folder to verify we have permissions
                        try
                        {
                            // Ensure the parent folder exists
                            var parentFolder = Path.GetDirectoryName(seleniumCachePath);
                            if (!string.IsNullOrEmpty(parentFolder) && !Directory.Exists(parentFolder))
                            {
                                Directory.CreateDirectory(parentFolder);
                                Debug.WriteLine($"[ChromeDriverManager] Created parent folder: {parentFolder}");
                            }

                            // Create the selenium cache folder
                            if (!Directory.Exists(seleniumCachePath))
                            {
                                Directory.CreateDirectory(seleniumCachePath);
                                Debug.WriteLine($"[ChromeDriverManager] Created fresh selenium cache folder");
                            }

                            // Test if we can write to the folder
                            var testFile = Path.Combine(seleniumCachePath, ".test");
                            File.WriteAllText(testFile, "test");
                            File.Delete(testFile);

                            Debug.WriteLine($"[ChromeDriverManager] Cache cleared and recreated successfully");
                            return true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Debug.WriteLine($"[ChromeDriverManager] No write permission for cache folder");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChromeDriverManager] Failed to clear cache: {ex.GetType().Name} - {ex.Message}");
                        return false;
                    }
                }


    }
}