using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace AkademiTrack.Services
{
    public enum HealthStatus
    {
        Healthy,
        Warning,
        Error,
        Unknown
    }

    public class HealthCheckResult
    {
        public string ComponentName { get; set; } = "";
        public HealthStatus Status { get; set; }
        public string Message { get; set; } = "";
        public long ResponseTimeMs { get; set; }
        public string Details { get; set; } = "";
    }

    public class SystemHealthCheck
    {
        private readonly HttpClient _httpClient;

        public SystemHealthCheck()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true // Allow redirects for Feide
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<HealthCheckResult> CheckInternetConnectivityAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _httpClient.GetAsync("https://www.google.com");
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    return new HealthCheckResult
                    {
                        ComponentName = "Internett",
                        Status = HealthStatus.Healthy,
                        Message = "Tilkoblet",
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Details = $"Responstid: {stopwatch.ElapsedMilliseconds}ms"
                    };
                }
                else
                {
                    return new HealthCheckResult
                    {
                        ComponentName = "Internett",
                        Status = HealthStatus.Warning,
                        Message = "Tilkoblet, men tregt",
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Details = $"Status code: {response.StatusCode}"
                    };
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Internett",
                    Status = HealthStatus.Error,
                    Message = "Timeout - ingen forbindelse",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = "Kunne ikke nå google.com innen 10 sekunder"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Internett",
                    Status = HealthStatus.Error,
                    Message = "Ingen forbindelse",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        public async Task<HealthCheckResult> CheckFeideAvailabilityAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                
                var response = await _httpClient.GetAsync("https://iskole.net/iskole_login/dataporten_login?RelayState=/elev");
                stopwatch.Stop();

                var responseTime = stopwatch.ElapsedMilliseconds;

                // Both mean the server is working
                if (response.IsSuccessStatusCode ||
                    response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.Moved ||
                    response.StatusCode == HttpStatusCode.Found)
                {
                    if (responseTime < 2000)
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "Feide",
                            Status = HealthStatus.Healthy,
                            Message = "Tilgjengelig",
                            ResponseTimeMs = responseTime,
                            Details = $"Responstid: {responseTime}ms (Status: {response.StatusCode})"
                        };
                    }
                    else
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "Feide",
                            Status = HealthStatus.Warning,
                            Message = "Tilgjengelig, men tregt",
                            ResponseTimeMs = responseTime,
                            Details = $"Responstid: {responseTime}ms (over 2s)"
                        };
                    }
                }
                else
                {
                    return new HealthCheckResult
                    {
                        ComponentName = "Feide",
                        Status = HealthStatus.Warning,
                        Message = "Problemer oppdaget",
                        ResponseTimeMs = responseTime,
                        Details = $"Status code: {response.StatusCode}"
                    };
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Feide",
                    Status = HealthStatus.Error,
                    Message = "Ikke tilgjengelig (timeout)",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = "Kunne ikke nå Feide innen 10 sekunder"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Feide",
                    Status = HealthStatus.Error,
                    Message = "Ikke tilgjengelig",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        public async Task<HealthCheckResult> CheckISkoleApiAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Check the actual iSkole endpoint
                var response = await _httpClient.GetAsync("https://iskole.net");
                stopwatch.Stop();

                var responseTime = stopwatch.ElapsedMilliseconds;

                // iSkole often redirects (301/302) or returns other codes
                // Accept 200, 301, 302, 303 as working
                if (response.IsSuccessStatusCode ||
                    response.StatusCode == HttpStatusCode.Moved ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.RedirectMethod ||
                    response.StatusCode == HttpStatusCode.Found)
                {
                    if (responseTime < 3000)
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "iSkole API",
                            Status = HealthStatus.Healthy,
                            Message = "Tilgjengelig",
                            ResponseTimeMs = responseTime,
                            Details = $"Responstid: {responseTime}ms (Status: {response.StatusCode})"
                        };
                    }
                    else
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "iSkole API",
                            Status = HealthStatus.Warning,
                            Message = "Tilgjengelig, men tregt",
                            ResponseTimeMs = responseTime,
                            Details = $"Responstid: {responseTime}ms (over 3s)"
                        };
                    }
                }
                else
                {
                    return new HealthCheckResult
                    {
                        ComponentName = "iSkole API",
                        Status = HealthStatus.Warning,
                        Message = "Problemer oppdaget",
                        ResponseTimeMs = responseTime,
                        Details = $"Status code: {response.StatusCode}"
                    };
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "iSkole API",
                    Status = HealthStatus.Error,
                    Message = "Ikke tilgjengelig (timeout)",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = "Kunne ikke nå iSkole innen 10 sekunder"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "iSkole API",
                    Status = HealthStatus.Error,
                    Message = "Ikke tilgjengelig",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
        }

        public async Task<HealthCheckResult> CheckBrowserDriverAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Use consistent cache directory like other services
                var chromiumCacheDir = GetChromiumCacheDirectory();
                var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
                {
                    Path = chromiumCacheDir
                });
                
                // Check if browser is already installed first
                var installedBrowsers = browserFetcher.GetInstalledBrowsers();
                var bundledChromiumPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "chromium-cache");
                bool usingBundledChromium = Directory.Exists(bundledChromiumPath);
                
                if (installedBrowsers.Any())
                {
                    var browser = installedBrowsers.First();
                    var executablePath = browser.GetExecutablePath();
                    
                    if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                    {
                        stopwatch.Stop();
                        string details = usingBundledChromium ? 
                            $"Bundled Chromium klar på {stopwatch.ElapsedMilliseconds}ms" :
                            $"Chromium installert og klar på {stopwatch.ElapsedMilliseconds}ms";
                        
                        return new HealthCheckResult
                        {
                            ComponentName = "Browser Driver",
                            Status = HealthStatus.Healthy,
                            Message = "Tilgjengelig",
                            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                            Details = details
                        };
                    }
                }
                
                // If not installed and no bundled version, try to download
                if (!usingBundledChromium)
                {
                    var revisionInfo = await browserFetcher.DownloadAsync();
                    
                    if (revisionInfo != null)
                    {
                        stopwatch.Stop();
                        return new HealthCheckResult
                        {
                            ComponentName = "Browser Driver",
                            Status = HealthStatus.Healthy,
                            Message = "Tilgjengelig",
                            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                            Details = $"Chromium lastet ned og klar på {stopwatch.ElapsedMilliseconds}ms"
                        };
                    }
                    else
                    {
                        stopwatch.Stop();
                        return new HealthCheckResult
                        {
                            ComponentName = "Browser Driver",
                            Status = HealthStatus.Error,
                            Message = "Browser feil",
                            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                            Details = "Chromium kunne ikke lastes ned"
                        };
                    }
                }
                else
                {
                    stopwatch.Stop();
                    return new HealthCheckResult
                    {
                        ComponentName = "Browser Driver",
                        Status = HealthStatus.Error,
                        Message = "Browser feil",
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Details = "Bundled Chromium ikke funnet eller skadet"
                    };
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Browser Driver",
                    Status = HealthStatus.Error,
                    Message = "Browser feil",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = $"Feil: {ex.Message}"
                };
            }
        }

        public async Task<HealthCheckResult[]> RunFullHealthCheckAsync()
        {
            var tasks = new[]
            {
                CheckInternetConnectivityAsync(),
                CheckFeideAvailabilityAsync(),
                CheckISkoleApiAsync(),
                CheckBrowserDriverAsync()
            };

            return await Task.WhenAll(tasks);
        }
        
        private static string GetChromiumCacheDirectory()
        {
            // EXTERNAL CHROMIUM APPROACH: Check for external signed Chromium first
            var externalChromiumPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AkademiTrack-Chromium");
            
            if (Directory.Exists(externalChromiumPath))
            {
                Debug.WriteLine($"[CHROMIUM] Using external signed Chromium at: {externalChromiumPath}");
                
                // Ensure executable permissions on macOS/Linux
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    EnsureExecutablePermissions(externalChromiumPath);
                }
                
                return externalChromiumPath;
            }
            
            // Fallback: Check for bundled Chromium (legacy)
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var bundledChromiumPath = Path.Combine(appDirectory, "Assets", "chromium-cache");
            
            if (Directory.Exists(bundledChromiumPath))
            {
                Debug.WriteLine($"[CHROMIUM] Using bundled Chromium at: {bundledChromiumPath}");
                
                // Ensure executable permissions on macOS/Linux
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    EnsureExecutablePermissions(bundledChromiumPath);
                }
                
                return bundledChromiumPath;
            }
            
            // Final fallback: AppData directory for download
            Debug.WriteLine("[CHROMIUM] No external or bundled Chromium found, falling back to AppData directory");
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cacheDir = Path.Combine(appDataDir, "AkademiTrack", "chromium-cache");
            
            // Ensure directory exists
            Directory.CreateDirectory(cacheDir);
            
            return cacheDir;
        }
        
        private static void EnsureExecutablePermissions(string chromiumPath)
        {
            try
            {
                // Find Chrome executable in the bundled path
                var chromeExecutables = Directory.GetFiles(chromiumPath, "*Chrome*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileName(f).Contains("Chrome") && !Path.GetExtension(f).Equals(".app", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var executable in chromeExecutables)
                {
                    if (File.Exists(executable))
                    {
                        Debug.WriteLine($"[CHROMIUM] Setting executable permissions for: {executable}");
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"+x \"{executable}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CHROMIUM] Failed to set executable permissions: {ex.Message}");
            }
        }
    }
}