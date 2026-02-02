using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AkademiTrack.Services
{
    public class WebKitManager
    {
        private static IPlaywright? _playwright;
        private static IBrowserType? _webkit;
        private static readonly object _lock = new object();
        
        // WebKit installation paths
        private static string GetWebKitInstallPath()
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataDir, "AkademiTrack", "webkit-browsers");
        }

        public static async Task<bool> EnsureWebKitInstalledAsync(IProgress<string>? progress = null)
        {
            try
            {
                Debug.WriteLine("[WebKitManager] Ensuring WebKit is installed...");
                progress?.Report("Checking WebKit installation...");
                
                // Initialize Playwright if not already done
                await InitializePlaywrightAsync();
                
                // Check if WebKit is already installed
                var webkitPath = GetWebKitInstallPath();
                if (Directory.Exists(webkitPath) && Directory.GetFiles(webkitPath, "*", SearchOption.AllDirectories).Length > 0)
                {
                    Debug.WriteLine("[WebKitManager] WebKit already installed");
                    progress?.Report("WebKit already installed");
                    return true;
                }
                
                Debug.WriteLine("[WebKitManager] Installing WebKit...");
                progress?.Report("Downloading WebKit browser (62MB)...");
                
                // Simulate progress updates while installation runs
                var installTask = Task.Run(() =>
                {
                    try
                    {
                        return Microsoft.Playwright.Program.Main(new[] { "install", "webkit" });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebKitManager] Playwright install exception: {ex.Message}");
                        return -1;
                    }
                });
                
                // Provide progress updates while waiting
                var progressValues = new[] { 10, 25, 40, 55, 70, 85, 95 };
                var progressIndex = 0;
                
                while (!installTask.IsCompleted)
                {
                    await Task.Delay(3000); // Update every 3 seconds
                    
                    if (progressIndex < progressValues.Length)
                    {
                        progress?.Report($"Downloading WebKit: {progressValues[progressIndex]}%");
                        progressIndex++;
                    }
                    else
                    {
                        progress?.Report("Finalizing WebKit installation...");
                    }
                }
                
                var exitCode = await installTask;
                
                if (exitCode == 0)
                {
                    Debug.WriteLine("[WebKitManager] WebKit installed successfully");
                    progress?.Report("WebKit installed successfully!");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[WebKitManager] WebKit installation failed with exit code: {exitCode}");
                    progress?.Report($"WebKit installation failed (exit code: {exitCode})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] WebKit installation error: {ex.Message}");
                progress?.Report($"Installation error: {ex.Message}");
                return false;
            }
        }

        private static async Task InitializePlaywrightAsync()
        {
            if (_playwright == null)
            {
                Debug.WriteLine("[WebKitManager] Initializing Playwright...");
                _playwright = await Playwright.CreateAsync();
                _webkit = _playwright.Webkit;
            }
        }

        public static async Task<IBrowser> LaunchBrowserAsync(bool headless = true)
        {
            await InitializePlaywrightAsync();
            
            if (_webkit == null)
            {
                throw new InvalidOperationException("WebKit browser type not initialized");
            }

            Debug.WriteLine("[WebKitManager] Launching WebKit browser...");
            
            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Args = new[]
                {
                    "--disable-web-security",
                    "--disable-features=VizDisplayCompositor",
                    "--disable-background-networking",
                    "--disable-sync",
                    "--disable-translate",
                    "--disable-plugins"
                }
            };

            // Add platform-specific args for invisibility
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // WebKit on macOS is naturally more discrete than Chromium
                Debug.WriteLine("[WebKitManager] Using macOS WebKit configuration");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("[WebKitManager] Using Windows WebKit configuration");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Debug.WriteLine("[WebKitManager] Using Linux WebKit configuration");
            }

            try
            {
                var browser = await _webkit.LaunchAsync(launchOptions);
                Debug.WriteLine("[WebKitManager] WebKit browser launched successfully");
                return browser;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] Browser launch failed: {ex.Message}");
                
                // Try to ensure WebKit is installed and retry once
                Debug.WriteLine("[WebKitManager] Attempting to install WebKit and retry...");
                var installed = await EnsureWebKitInstalledAsync();
                
                if (installed)
                {
                    Debug.WriteLine("[WebKitManager] Retrying browser launch after installation...");
                    return await _webkit.LaunchAsync(launchOptions);
                }
                
                throw new InvalidOperationException($"WebKit browser launch failed: {ex.Message}", ex);
            }
        }

        public static async Task<string?> GetWebKitExecutablePathAsync()
        {
            try
            {
                await InitializePlaywrightAsync();
                
                if (_webkit == null)
                {
                    return null;
                }

                // WebKit executable path is managed by Playwright
                // We don't need to expose the path directly like with Chromium
                var webkitPath = GetWebKitInstallPath();
                
                if (Directory.Exists(webkitPath))
                {
                    Debug.WriteLine($"[WebKitManager] WebKit installed at: {webkitPath}");
                    return webkitPath;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] Error getting WebKit path: {ex.Message}");
                return null;
            }
        }

        public static void Dispose()
        {
            _playwright?.Dispose();
            _playwright = null;
            _webkit = null;
        }
    }
}