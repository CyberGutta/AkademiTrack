using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace AkademiTrack.Services
{
    public class ChromeManager
    {
        private static readonly HttpClient _httpClient = new();
        
        // Add caching to avoid repeated checks
        private static string? _cachedChromePath;
        private static DateTime _lastCacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTimeout = TimeSpan.FromMinutes(5);
        
        // Chrome detection paths by platform
        private static readonly Dictionary<OSPlatform, string[]> SystemChromePaths = new()
        {
            [OSPlatform.OSX] = new[]
            {
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            },
            [OSPlatform.Windows] = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
            },
            [OSPlatform.Linux] = new[]
            {
                "/usr/bin/google-chrome",
                "/usr/bin/google-chrome-stable",
                "/usr/bin/chromium",
                "/usr/bin/chromium-browser",
                "/snap/bin/chromium",
                "/usr/bin/microsoft-edge"
            }
        };

        // Chrome download URLs
        private static readonly Dictionary<OSPlatform, string> ChromeDownloadUrls = new()
        {
            [OSPlatform.OSX] = "https://dl.google.com/chrome/mac/stable/GGRO/googlechrome.dmg",
            [OSPlatform.Windows] = "https://dl.google.com/chrome/install/ChromeStandaloneSetup64.exe",
            [OSPlatform.Linux] = "https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb"
        };

        public static async Task<string?> GetChromeExecutablePathAsync(bool forceRefresh = false)
        {
            // Test mode: Force specific scenarios
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--test-no-chrome"))
            {
                Debug.WriteLine("[ChromeManager] TEST MODE: Simulating no Chrome found");
                return await FallbackToChromiumAsync();
            }
            if (args.Contains("--test-force-install"))
            {
                Debug.WriteLine("[ChromeManager] TEST MODE: Forcing Chrome installation");
                return await InstallChromePrivatelyAsync();
            }

            // Use cache unless forced refresh or cache expired
            if (!forceRefresh && 
                !string.IsNullOrEmpty(_cachedChromePath) && 
                DateTime.Now - _lastCacheTime < CacheTimeout &&
                File.Exists(_cachedChromePath))
            {
                return _cachedChromePath;
            }

            Debug.WriteLine("[ChromeManager] Starting Chrome detection...");
            
            // Step 1: Check for system Chrome
            var systemChrome = DetectSystemChrome();
            if (!string.IsNullOrEmpty(systemChrome))
            {
                Debug.WriteLine($"[ChromeManager] ✅ Found system Chrome: {systemChrome}");
                _cachedChromePath = systemChrome;
                _lastCacheTime = DateTime.Now;
                return systemChrome;
            }

            // Step 2: Check for app-private Chrome installation
            var privateChrome = GetPrivateChromeInstallPath();
            if (File.Exists(privateChrome))
            {
                Debug.WriteLine($"[ChromeManager] ✅ Found private Chrome: {privateChrome}");
                _cachedChromePath = privateChrome;
                _lastCacheTime = DateTime.Now;
                return privateChrome;
            }

            // Step 3: Try to install Chrome to private location
            Debug.WriteLine("[ChromeManager] No Chrome found, attempting installation...");
            var installedChrome = await InstallChromePrivatelyAsync();
            if (!string.IsNullOrEmpty(installedChrome))
            {
                Debug.WriteLine($"[ChromeManager] ✅ Chrome installed successfully: {installedChrome}");
                _cachedChromePath = installedChrome;
                _lastCacheTime = DateTime.Now;
                return installedChrome;
            }

            // Step 4: Fallback to PuppeteerSharp Chromium
            Debug.WriteLine("[ChromeManager] Chrome installation failed, falling back to Chromium...");
            var chromiumPath = await FallbackToChromiumAsync();
            if (!string.IsNullOrEmpty(chromiumPath))
            {
                _cachedChromePath = chromiumPath;
                _lastCacheTime = DateTime.Now;
            }
            return chromiumPath;
        }

        private static string? DetectSystemChrome()
        {
            var currentPlatform = GetCurrentPlatform();
            if (!SystemChromePaths.ContainsKey(currentPlatform))
                return null;

            var paths = SystemChromePaths[currentPlatform];
            
            foreach (var path in paths)
            {
                try
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    if (File.Exists(expandedPath))
                    {
                        // Verify it's actually executable
                        if (IsExecutableValid(expandedPath))
                        {
                            Debug.WriteLine($"[ChromeManager] System browser found: {expandedPath}");
                            return expandedPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChromeManager] Error checking path {path}: {ex.Message}");
                }
            }

            return null;
        }

        private static string GetPrivateChromeInstallPath()
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var chromeDir = Path.Combine(appDataDir, "AkademiTrack", "Chrome");
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(chromeDir, "Google Chrome.app", "Contents", "MacOS", "Google Chrome");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(chromeDir, "chrome.exe");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(chromeDir, "google-chrome");
            }

            return string.Empty;
        }

        private static async Task<string?> InstallChromePrivatelyAsync()
        {
            try
            {
                var currentPlatform = GetCurrentPlatform();
                if (!ChromeDownloadUrls.ContainsKey(currentPlatform))
                {
                    Debug.WriteLine($"[ChromeManager] No download URL for platform: {currentPlatform}");
                    return null;
                }

                var downloadUrl = ChromeDownloadUrls[currentPlatform];
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var chromeDir = Path.Combine(appDataDir, "AkademiTrack", "Chrome");
                Directory.CreateDirectory(chromeDir);

                Debug.WriteLine($"[ChromeManager] Downloading Chrome from: {downloadUrl}");
                Debug.WriteLine($"[ChromeManager] Installing to: {chromeDir}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return await InstallChromeOnMacAsync(downloadUrl, chromeDir);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return await InstallChromeOnWindowsAsync(downloadUrl, chromeDir);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return await InstallChromeOnLinuxAsync(downloadUrl, chromeDir);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Chrome installation failed: {ex.Message}");
            }

            return null;
        }

        private static async Task<string?> InstallChromeOnMacAsync(string downloadUrl, string installDir)
        {
            try
            {
                var dmgPath = Path.Combine(Path.GetTempPath(), "chrome.dmg");
                var mountPoint = "/Volumes/Google Chrome";
                
                // Download DMG
                Debug.WriteLine("[ChromeManager] Downloading Chrome DMG...");
                using var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = File.Create(dmgPath);
                await response.Content.CopyToAsync(fileStream);
                
                Debug.WriteLine("[ChromeManager] DMG downloaded, mounting...");
                
                // Mount DMG
                var mountResult = await RunCommandAsync("hdiutil", $"attach \"{dmgPath}\" -nobrowse -quiet");
                if (mountResult.ExitCode != 0)
                {
                    Debug.WriteLine($"[ChromeManager] Failed to mount DMG: {mountResult.Error}");
                    return null;
                }

                // Copy Chrome app
                var sourceApp = Path.Combine(mountPoint, "Google Chrome.app");
                var destApp = Path.Combine(installDir, "Google Chrome.app");
                
                if (Directory.Exists(sourceApp))
                {
                    Debug.WriteLine("[ChromeManager] Copying Chrome app...");
                    await RunCommandAsync("cp", $"-R \"{sourceApp}\" \"{destApp}\"");
                    
                    // Remove quarantine attributes
                    await RunCommandAsync("xattr", $"-cr \"{destApp}\"");
                    
                    // Unmount DMG
                    await RunCommandAsync("hdiutil", $"detach \"{mountPoint}\" -quiet");
                    
                    // Clean up
                    File.Delete(dmgPath);
                    
                    var executablePath = Path.Combine(destApp, "Contents", "MacOS", "Google Chrome");
                    if (File.Exists(executablePath))
                    {
                        Debug.WriteLine("[ChromeManager] ✅ Chrome installed successfully on macOS");
                        return executablePath;
                    }
                }
                
                // Cleanup on failure
                await RunCommandAsync("hdiutil", $"detach \"{mountPoint}\" -quiet -force");
                File.Delete(dmgPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] macOS Chrome installation failed: {ex.Message}");
            }

            return null;
        }

        private static async Task<string?> InstallChromeOnWindowsAsync(string downloadUrl, string installDir)
        {
            try
            {
                var installerPath = Path.Combine(Path.GetTempPath(), "ChromeSetup.exe");
                
                // Download installer
                Debug.WriteLine("[ChromeManager] Downloading Chrome installer...");
                using var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = File.Create(installerPath);
                await response.Content.CopyToAsync(fileStream);
                
                // Run installer with custom install directory
                Debug.WriteLine("[ChromeManager] Running Chrome installer...");
                var installResult = await RunCommandAsync(installerPath, $"--system-level --do-not-launch-chrome --install-path=\"{installDir}\"");
                
                // Clean up
                File.Delete(installerPath);
                
                var executablePath = Path.Combine(installDir, "chrome.exe");
                if (File.Exists(executablePath))
                {
                    Debug.WriteLine("[ChromeManager] ✅ Chrome installed successfully on Windows");
                    return executablePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Windows Chrome installation failed: {ex.Message}");
            }

            return null;
        }

        private static async Task<string?> InstallChromeOnLinuxAsync(string downloadUrl, string installDir)
        {
            try
            {
                var debPath = Path.Combine(Path.GetTempPath(), "chrome.deb");
                
                // Download DEB package
                Debug.WriteLine("[ChromeManager] Downloading Chrome DEB package...");
                using var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = File.Create(debPath);
                await response.Content.CopyToAsync(fileStream);
                
                // Extract DEB to custom location
                Debug.WriteLine("[ChromeManager] Extracting Chrome package...");
                var extractResult = await RunCommandAsync("dpkg-deb", $"-x \"{debPath}\" \"{installDir}\"");
                
                // Clean up
                File.Delete(debPath);
                
                var executablePath = Path.Combine(installDir, "opt", "google", "chrome", "google-chrome");
                if (File.Exists(executablePath))
                {
                    // Make executable
                    await RunCommandAsync("chmod", $"+x \"{executablePath}\"");
                    Debug.WriteLine("[ChromeManager] ✅ Chrome installed successfully on Linux");
                    return executablePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Linux Chrome installation failed: {ex.Message}");
            }

            return null;
        }

        private static async Task<string?> FallbackToChromiumAsync()
        {
            try
            {
                Debug.WriteLine("[ChromeManager] Falling back to PuppeteerSharp Chromium...");
                
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var chromiumCacheDir = Path.Combine(appDataDir, "AkademiTrack", "chromium-cache");
                
                var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
                {
                    Path = chromiumCacheDir
                });

                // Check if already downloaded
                var installedBrowsers = browserFetcher.GetInstalledBrowsers();
                if (installedBrowsers.Any())
                {
                    var browser = installedBrowsers.First();
                    var executablePath = browser.GetExecutablePath();
                    
                    if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                    {
                        Debug.WriteLine($"[ChromeManager] Using existing Chromium: {executablePath}");
                        return executablePath;
                    }
                }

                // Download Chromium
                Debug.WriteLine("[ChromeManager] Downloading Chromium...");
                var downloadedBrowser = await browserFetcher.DownloadAsync();
                
                if (downloadedBrowser != null)
                {
                    var executablePath = downloadedBrowser.GetExecutablePath();
                    
                    // Remove quarantine on macOS
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !string.IsNullOrEmpty(executablePath))
                    {
                        var chromiumDir = Path.GetDirectoryName(executablePath);
                        if (!string.IsNullOrEmpty(chromiumDir))
                        {
                            await RunCommandAsync("xattr", $"-cr \"{chromiumDir}\"");
                        }
                    }
                    
                    Debug.WriteLine($"[ChromeManager] ✅ Chromium fallback successful: {executablePath}");
                    return executablePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Chromium fallback failed: {ex.Message}");
            }

            return null;
        }

        private static bool IsExecutableValid(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                var fileInfo = new System.IO.FileInfo(path);
                return fileInfo.Length > 1024; // At least 1KB
            }
            catch
            {
                return false;
            }
        }

        private static OSPlatform GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return OSPlatform.OSX;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return OSPlatform.Linux;
            
            throw new PlatformNotSupportedException("Unsupported platform");
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command, string arguments)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.Start();
                
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                var output = await outputTask;
                var error = await errorTask;
                
                return (process.ExitCode, output, error);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Command failed: {command} {arguments} - {ex.Message}");
                return (-1, "", ex.Message);
            }
        }

        public static async Task<IBrowser> LaunchBrowserAsync(bool headless = true)
        {
            var executablePath = await GetChromeExecutablePathAsync();
            
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException("No suitable browser found. Please install Google Chrome.");
            }

            Debug.WriteLine($"[ChromeManager] Launching browser: {executablePath}");
            
            var launchOptions = new LaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-default-apps",
                    "--disable-extensions",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-renderer-backgrounding"
                }
            };

            // Add platform-specific args
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                launchOptions.Args = launchOptions.Args.Concat(new[]
                {
                    "--disable-background-mode",
                    "--disable-features=TranslateUI"
                }).ToArray();
            }

            try
            {
                return await Puppeteer.LaunchAsync(launchOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChromeManager] Browser launch failed: {ex.Message}");
                
                // Runtime failsafe: If Chrome disappeared, try to get a new one
                Debug.WriteLine("[ChromeManager] Attempting runtime recovery...");
                
                // Force refresh - don't use cached paths
                var fallbackPath = await GetChromeExecutablePathAsync(forceRefresh: true);
                
                if (!string.IsNullOrEmpty(fallbackPath) && fallbackPath != executablePath)
                {
                    Debug.WriteLine($"[ChromeManager] Trying fallback browser: {fallbackPath}");
                    launchOptions.ExecutablePath = fallbackPath;
                    return await Puppeteer.LaunchAsync(launchOptions);
                }
                
                throw new InvalidOperationException($"Browser launch failed and no fallback available: {ex.Message}", ex);
            }
        }
    }
}