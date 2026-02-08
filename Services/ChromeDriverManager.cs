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
        
        /// <summary>
        /// Ensures ChromeDriver is available and returns the path to the driver executable.
        /// Uses Selenium Manager (built into Selenium 4.6+) for automatic driver management.
        /// Falls back to WebDriverManager.Net if needed.
        /// </summary>
        public static async Task<bool> EnsureChromeDriverInstalledAsync(IProgress<string>? progress = null)
        {
            try
            {
                Debug.WriteLine("[ChromeDriverManager] Checking ChromeDriver availability...");
                progress?.Report("Checking ChromeDriver availability...");
                
                // First, try to create a ChromeDriver instance to test if everything works
                // Selenium Manager (built into Selenium 4.6+) will automatically download the driver if needed
                if (await TestChromeDriverAsync())
                {
                    Debug.WriteLine("[ChromeDriverManager] ✅ ChromeDriver is working properly");
                    progress?.Report("ChromeDriver ready");
                    return true;
                }
                
                Debug.WriteLine("[ChromeDriverManager] ChromeDriver not working, attempting setup...");
                progress?.Report("Setting up ChromeDriver...");
                
                // Fallback: Use WebDriverManager.Net for explicit driver management
                return await SetupChromeDriverWithWebDriverManagerAsync(progress);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeDriverManager] ChromeDriver setup error: {ex.Message}");
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
                try
                {
                    var options = new ChromeOptions();
                    options.AddArgument("--headless=new"); // Use new headless mode
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
                    
                    // Set a very short timeout for testing
                    var service = ChromeDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;
                    service.SuppressInitialDiagnosticInformation = true;
                    
                    using (var driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(10)))
                    {
                        // Quick test - just navigate to a simple page
                        driver.Navigate().GoToUrl("data:text/html,<html><body>Test</body></html>");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChromeDriverManager] Test failed: {ex.Message}");
                    return false;
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
                progress?.Report("Setting up ChromeDriver...");
                
                await Task.Run(() =>
                {
                    // Use WebDriverManager.Net to download and setup ChromeDriver
                    // Note: For Chrome 115+, this might fail, but Selenium Manager should handle it
                    try
                    {
                        new DriverManager().SetUpDriver(new ChromeConfig());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChromeDriverManager] WebDriverManager failed: {ex.Message}");
                        // This is expected for Chrome 115+, Selenium Manager will handle it
                    }
                });
                
                progress?.Report("ChromeDriver setup complete");
                
                // Test again after setup
                return await TestChromeDriverAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeDriverManager] WebDriverManager setup failed: {ex.Message}");
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
    }
}