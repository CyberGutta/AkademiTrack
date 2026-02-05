using System;
using System.Collections.Generic;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use ~/Library/Application Support/AkademiTrack for macOS
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(homeDir, "Library", "Application Support", "AkademiTrack", "webkit-browsers");
            }
            else
            {
                // Use standard AppData for Windows/Linux
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appDataDir, "AkademiTrack", "webkit-browsers");
            }
        }

        public static async Task<bool> EnsureWebKitInstalledAsync(IProgress<string>? progress = null)
        {
            try
            {
                Debug.WriteLine("[WebKitManager] Checking WebKit installation status...");
                progress?.Report("Checking WebKit installation...");
                
                // Initialize Playwright if not already done
                await InitializePlaywrightAsync();
                
                // First, check if WebKit is already properly installed (NO DOWNLOAD)
                if (await IsWebKitProperlyInstalledAsync())
                {
                    Debug.WriteLine("[WebKitManager] ✅ WebKit is already properly installed");
                    progress?.Report("WebKit already installed");
                    return true;
                }
                
                Debug.WriteLine("[WebKitManager] WebKit not found or incomplete, installing...");
                progress?.Report("Downloading WebKit browser (62MB)...");
                
                // For Windows, try multiple installation attempts with different strategies
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return await InstallWebKitWindowsAsync(progress);
                }
                else
                {
                    return await InstallWebKitDefaultAsync(progress);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] WebKit installation error: {ex.Message}");
                progress?.Report($"Installation error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if WebKit is installed WITHOUT downloading it
        /// </summary>
        public static async Task<bool> IsWebKitInstalledAsync()
        {
            try
            {
                Debug.WriteLine("[WebKitManager] Checking if WebKit is installed (no download)...");
                
                // Initialize Playwright if not already done
                await InitializePlaywrightAsync();
                
                // Check if WebKit is properly installed
                return await IsWebKitProperlyInstalledAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] Error checking WebKit installation: {ex.Message}");
                return false;
            }
        }

        private static Task<bool> IsWebKitProperlyInstalledAsync()
        {
            try
            {
                Debug.WriteLine("[WebKitManager] Performing thorough WebKit installation check...");
                
                // Method 1: Check if Playwright can find WebKit
                if (_webkit != null)
                {
                    try
                    {
                        // Try to get the executable path - this will fail if WebKit isn't installed
                        var executablePath = _webkit.ExecutablePath;
                        if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                        {
                            Debug.WriteLine($"[WebKitManager] ✅ Found WebKit executable: {executablePath}");
                            return Task.FromResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebKitManager] WebKit executable check failed: {ex.Message}");
                    }
                }
                
                // Method 2: Check standard Playwright browser directories
                var possiblePaths = new List<string>();
                
                // Add platform-specific Playwright browser paths
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    possiblePaths.Add(Path.Combine(userProfile, "AppData", "Local", "ms-playwright"));
                    possiblePaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkademiTrack", "webkit-browsers"));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    possiblePaths.Add(Path.Combine(homeDir, "Library", "Caches", "ms-playwright"));
                    possiblePaths.Add(Path.Combine(homeDir, "Library", "Application Support", "AkademiTrack", "webkit-browsers"));
                }
                else // Linux
                {
                    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    possiblePaths.Add(Path.Combine(homeDir, ".cache", "ms-playwright"));
                    possiblePaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkademiTrack", "webkit-browsers"));
                }
                
                // Check each possible path for WebKit installation
                foreach (var basePath in possiblePaths)
                {
                    if (Directory.Exists(basePath))
                    {
                        Debug.WriteLine($"[WebKitManager] Checking path: {basePath}");
                        
                        // Look for webkit directories
                        var webkitDirs = Directory.GetDirectories(basePath, "*webkit*", SearchOption.AllDirectories);
                        
                        foreach (var webkitDir in webkitDirs)
                        {
                            Debug.WriteLine($"[WebKitManager] Found WebKit directory: {webkitDir}");
                            
                            // Check if this directory contains WebKit executable files
                            var executableExtensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                                ? new[] { "*.exe" } 
                                : new[] { "*" }; // On Unix, executables don't need extensions
                            
                            foreach (var extension in executableExtensions)
                            {
                                var executables = Directory.GetFiles(webkitDir, extension, SearchOption.AllDirectories)
                                    .Where(f => Path.GetFileName(f).ToLower().Contains("webkit") || 
                                               Path.GetFileName(f).ToLower().Contains("playwright") ||
                                               (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && Path.GetFileName(f) == "Playwright"))
                                    .ToArray();
                                
                                if (executables.Length > 0)
                                {
                                    Debug.WriteLine($"[WebKitManager] ✅ Found WebKit executables in: {webkitDir}");
                                    Debug.WriteLine($"[WebKitManager] Executables: {string.Join(", ", executables.Select(Path.GetFileName))}");
                                    return Task.FromResult(true);
                                }
                            }
                        }
                    }
                }
                
                Debug.WriteLine("[WebKitManager] ❌ No WebKit installation found in any expected location");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] Error checking WebKit installation: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private static async Task<bool> InstallWebKitWindowsAsync(IProgress<string>? progress)
        {
            Debug.WriteLine("[WebKitManager] Starting Windows-specific WebKit installation...");
            
            // Try installation with retry logic for Windows
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Debug.WriteLine($"[WebKitManager] Installation attempt {attempt}/3");
                    progress?.Report($"Installing WebKit (attempt {attempt}/3)...");
                    
                    var installTask = Task.Run(() =>
                    {
                        try
                        {
                            // Set environment variables for better compatibility
                            var customPath = GetWebKitInstallPath();
                            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", customPath);
                            Environment.SetEnvironmentVariable("PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD", "0");
                            
                            // Ensure the directory exists
                            Directory.CreateDirectory(customPath);
                            
                            Debug.WriteLine($"[WebKitManager] Set PLAYWRIGHT_BROWSERS_PATH to: {customPath}");
                            
                            return Microsoft.Playwright.Program.Main(new[] { "install", "webkit", "--with-deps" });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebKitManager] Playwright install exception: {ex.Message}");
                            return -1;
                        }
                    });
                    
                    // Provide progress updates while waiting (longer timeout for Windows)
                    var progressValues = new[] { 10, 20, 30, 45, 60, 75, 85, 95 };
                    var progressIndex = 0;
                    var maxWaitTime = TimeSpan.FromMinutes(10); // 10 minutes for Windows
                    var startTime = DateTime.Now;
                    
                    while (!installTask.IsCompleted && (DateTime.Now - startTime) < maxWaitTime)
                    {
                        await Task.Delay(4000); // Update every 4 seconds for Windows
                        
                        if (progressIndex < progressValues.Length)
                        {
                            progress?.Report($"Downloading WebKit: {progressValues[progressIndex]}% (attempt {attempt})");
                            progressIndex++;
                        }
                        else
                        {
                            progress?.Report($"Finalizing WebKit installation... (attempt {attempt})");
                        }
                    }
                    
                    if (!installTask.IsCompleted)
                    {
                        Debug.WriteLine($"[WebKitManager] Installation attempt {attempt} timed out");
                        progress?.Report($"Installation attempt {attempt} timed out, retrying...");
                        continue;
                    }
                    
                    var exitCode = await installTask;
                    
                    if (exitCode == 0)
                    {
                        Debug.WriteLine("[WebKitManager] WebKit installed successfully on Windows");
                        
                        // Remove quarantine attributes on macOS to prevent Gatekeeper issues
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            await RemoveQuarantineAttributesAsync();
                        }
                        
                        progress?.Report("WebKit installed successfully!");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"[WebKitManager] Installation attempt {attempt} failed with exit code: {exitCode}");
                        if (attempt < 3)
                        {
                            progress?.Report($"Installation failed (exit code: {exitCode}), retrying...");
                            await Task.Delay(2000); // Wait before retry
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebKitManager] Installation attempt {attempt} exception: {ex.Message}");
                    if (attempt < 3)
                    {
                        progress?.Report($"Installation error, retrying... ({ex.Message})");
                        await Task.Delay(2000);
                    }
                }
            }
            
            Debug.WriteLine("[WebKitManager] All Windows installation attempts failed");
            progress?.Report("WebKit installation failed after 3 attempts");
            return false;
        }

        private static async Task<bool> InstallWebKitDefaultAsync(IProgress<string>? progress)
        {
            // Set custom installation path for macOS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var customPath = GetWebKitInstallPath();
                Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", customPath);
                Debug.WriteLine($"[WebKitManager] Set PLAYWRIGHT_BROWSERS_PATH to: {customPath}");
                
                // Ensure the directory exists
                Directory.CreateDirectory(customPath);
            }
            
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
                
                // Remove quarantine attributes on macOS to prevent Gatekeeper issues
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await RemoveQuarantineAttributesAsync();
                }
                
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

        private static async Task InitializePlaywrightAsync()
        {
            if (_playwright == null)
            {
                Debug.WriteLine("[WebKitManager] Initializing Playwright...");
                
                // Set custom browser path for consistent behavior
                var customPath = GetWebKitInstallPath();
                Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", customPath);
                Debug.WriteLine($"[WebKitManager] Set PLAYWRIGHT_BROWSERS_PATH to: {customPath}");
                
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

        /// <summary>
        /// Remove quarantine attributes from WebKit binaries to prevent Gatekeeper blocking
        /// </summary>
        private static async Task RemoveQuarantineAttributesAsync()
        {
            try
            {
                Debug.WriteLine("[WebKitManager] Removing quarantine attributes from WebKit binaries...");
                
                var webkitPath = GetWebKitInstallPath();
                if (!Directory.Exists(webkitPath))
                {
                    Debug.WriteLine("[WebKitManager] WebKit directory not found, skipping quarantine removal");
                    return;
                }

                // Find all WebKit directories
                var webkitDirs = Directory.GetDirectories(webkitPath, "*webkit*", SearchOption.AllDirectories);
                
                foreach (var webkitDir in webkitDirs)
                {
                    Debug.WriteLine($"[WebKitManager] Removing quarantine from: {webkitDir}");
                    
                    // Use xattr command to remove quarantine attributes recursively
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "xattr",
                            Arguments = $"-dr com.apple.quarantine \"{webkitDir}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        Debug.WriteLine($"[WebKitManager] ✅ Successfully removed quarantine from {Path.GetFileName(webkitDir)}");
                    }
                    else
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        Debug.WriteLine($"[WebKitManager] ⚠️ Failed to remove quarantine from {Path.GetFileName(webkitDir)}: {error}");
                    }
                }
                
                Debug.WriteLine("[WebKitManager] Quarantine removal completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebKitManager] Error removing quarantine attributes: {ex.Message}");
                // Don't throw - this is not critical enough to stop the app
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