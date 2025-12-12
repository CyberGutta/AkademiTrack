using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

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
                
                var response = await _httpClient.GetAsync("https://innsyn.feide.no/");
                stopwatch.Stop();

                var responseTime = stopwatch.ElapsedMilliseconds;

                // Feide auth endpoint will redirect or return 400 (bad request) without params
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

        public async Task<HealthCheckResult> CheckSeleniumDriverAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            ChromeDriver? driver = null;
            ChromeDriverService? service = null;

            try
            {
                service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                var options = new ChromeOptions();
                options.AddArgument("--headless");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");

                driver = new ChromeDriver(service, options);
                driver.Navigate().GoToUrl("about:blank");

                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Selenium Driver",
                    Status = HealthStatus.Healthy,
                    Message = "Fungerer",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = $"ChromeDriver initialisert på {stopwatch.ElapsedMilliseconds}ms"
                };
            }
            catch (DriverServiceNotFoundException)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Selenium Driver",
                    Status = HealthStatus.Error,
                    Message = "ChromeDriver ikke funnet",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = "ChromeDriver må installeres. Vennligst reinstaller applikasjonen."
                };
            }
            catch (WebDriverException ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Selenium Driver",
                    Status = HealthStatus.Error,
                    Message = "Driver feil",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthCheckResult
                {
                    ComponentName = "Selenium Driver",
                    Status = HealthStatus.Error,
                    Message = "Ukjent feil",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    Details = ex.Message
                };
            }
            finally
            {
                try
                {
                    driver?.Quit();
                    driver?.Dispose();
                    service?.Dispose();
                }
                catch { }
            }
        }

        public async Task<HealthCheckResult[]> RunFullHealthCheckAsync()
        {
            var tasks = new[]
            {
                CheckInternetConnectivityAsync(),
                CheckFeideAvailabilityAsync(),
                CheckISkoleApiAsync(),
                CheckSeleniumDriverAsync()
            };

            return await Task.WhenAll(tasks);
        }
    }
}