using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

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
                Debug.WriteLine("[HealthCheck] Checking browser driver availability...");
                
                await Task.CompletedTask; // Make it async
                
                try
                {
                    // Check multiple possible ChromeDriver locations
                    var possiblePaths = new[]
                    {
                        // Selenium Manager cache
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "selenium"),
                        // WebDriverManager cache
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wdm", "drivers", "chromedriver"),
                        // NuGet packages
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "selenium.webdriver.chromedriver"),
                        // Current directory
                        Directory.GetCurrentDirectory()
                    };
                    
                    bool foundDriver = false;
                    string foundLocation = "";
                    
                    foreach (var basePath in possiblePaths)
                    {
                        if (Directory.Exists(basePath))
                        {
                            var drivers = Directory.GetFiles(basePath, "chromedriver", SearchOption.AllDirectories)
                                .Concat(Directory.GetFiles(basePath, "chromedriver.exe", SearchOption.AllDirectories))
                                .ToArray();
                            
                            if (drivers.Length > 0)
                            {
                                foundDriver = true;
                                foundLocation = Path.GetDirectoryName(drivers[0]) ?? basePath;
                                break;
                            }
                        }
                    }
                    
                    // Also try the default service path
                    if (!foundDriver)
                    {
                        try
                        {
                            var service = OpenQA.Selenium.Chrome.ChromeDriverService.CreateDefaultService();
                            var driverPath = service.DriverServicePath;
                            
                            if (!string.IsNullOrEmpty(driverPath))
                            {
                                var chromeDriverExists = File.Exists(Path.Combine(driverPath, "chromedriver")) || 
                                                        File.Exists(Path.Combine(driverPath, "chromedriver.exe"));
                                
                                if (chromeDriverExists)
                                {
                                    foundDriver = true;
                                    foundLocation = driverPath;
                                }
                            }
                        }
                        catch { }
                    }
                    
                    stopwatch.Stop();
                    
                    if (foundDriver)
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "Browser Driver",
                            Status = HealthStatus.Healthy,
                            Message = "Tilgjengelig",
                            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                            Details = $"ChromeDriver funnet"
                        };
                    }
                    else
                    {
                        return new HealthCheckResult
                        {
                            ComponentName = "Browser Driver",
                            Status = HealthStatus.Warning,
                            Message = "Ikke funnet",
                            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                            Details = "ChromeDriver må lastes ned ved første kjøring av automatisering"
                        };
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    return new HealthCheckResult
                    {
                        ComponentName = "Browser Driver",
                        Status = HealthStatus.Warning,
                        Message = "Kunne ikke verifisere",
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        Details = $"Vil lastes ned automatisk ved behov: {ex.Message}"
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
        
    }
}