using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    public class AttendanceDataService
    {
        private readonly HttpClient _httpClient;
        private UserParameters? _userParameters;
        private Dictionary<string, string>? _cookies;
        
        private ILoggingService? _loggingService;

        public void SetLoggingService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        public AttendanceDataService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public void SetCredentials(UserParameters parameters, Dictionary<string, string> cookies)
        {
            _userParameters = parameters;
            _cookies = cookies;
        }

        private async Task<T?> FetchWithRetryAsync<T>(Func<Task<T?>> fetchFunc) where T : class
        {
            try
            {
                // First attempt with current cookies
                var result = await fetchFunc();
                
                // If fetch failed (likely expired cookies), re-authenticate and retry
                if (result == null)
                {
                    _loggingService?.LogInfo("📡 Data kunne ikke hentes - prøver å oppdatere autentisering...");
                    
                    var notificationService = ServiceLocator.Instance.GetService<INotificationService>();
                    var authService = new AuthenticationService(notificationService);
                    var authResult = await authService.AuthenticateAsync();
                    
                    if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                    {
                        // Update credentials with fresh cookies
                        var newParams = new UserParameters
                        {
                            FylkeId = authResult.Parameters.FylkeId,
                            PlanPeri = authResult.Parameters.PlanPeri,
                            SkoleId = authResult.Parameters.SkoleId
                        };
                        SetCredentials(newParams, authResult.Cookies);
                        
                        _loggingService?.LogSuccess("✓ Autentisering oppdatert - prøver å hente data på nytt...");
                        
                        // Retry with fresh cookies
                        result = await fetchFunc();
                        
                        if (result == null)
                        {
                            _loggingService?.LogError("❌ Kunne ikke hente data selv etter re-autentisering");
                        }
                    }
                    else
                    {
                        _loggingService?.LogError("❌ Re-autentisering feilet");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Fetch with retry error: {ex.Message}");
                return null;
            }
        }


        // Fetch summary statistics (over/undertid, total hours, etc.)
        public async Task<AttendanceSummary?> GetAttendanceSummaryAsync()
        {
            return await FetchWithRetryAsync(FetchAttendanceSummaryAsync);
        }

        private async Task<AttendanceSummary?> FetchAttendanceSummaryAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoFravaer_oppmote_studietid_sum;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=25&offset=0&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var summaryResponse = JsonSerializer.Deserialize<AttendanceSummaryResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return summaryResponse?.Items?.FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Fetch today's schedule with attendance status
        public async Task<TodayScheduleData?> GetTodayScheduleAsync()
        {
            return await FetchWithRetryAsync(FetchTodayScheduleAsync);
        }

        private async Task<TodayScheduleData?> FetchTodayScheduleAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (scheduleResponse?.Items == null)
                    return null;

                return ProcessTodaySchedule(scheduleResponse.Items);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<MonthlyAttendanceData?> GetMonthlyAttendanceAsync()
        {
            return await FetchWithRetryAsync(FetchMonthlyAttendanceAsync);
        }

        private async Task<MonthlyAttendanceData?> FetchMonthlyAttendanceAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                {
                    Console.WriteLine("[MONTHLY DEBUG] No parameters or cookies available");
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");

                var now = DateTime.Now;
                var firstDayOfMonth = new DateTime(now.Year, now.Month, 1);
                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                var startDate = firstDayOfMonth.ToString("yyyyMMdd");
                var endDate = lastDayOfMonth.ToString("yyyyMMdd");

                Console.WriteLine($"[MONTHLY DEBUG] Date range: {startDate} to {endDate}");

                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId},startDate={startDate},endDate={endDate}&onlyData=true&limit=1000&totalResults=true";

                Console.WriteLine($"[MONTHLY DEBUG] Request URL: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);

                Console.WriteLine($"[MONTHLY DEBUG] Response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[MONTHLY DEBUG] Request failed with status {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[MONTHLY DEBUG] JSON length: {json.Length}");

                var scheduleResponse = JsonSerializer.Deserialize<MonthlyScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (scheduleResponse?.Items == null)
                {
                    Console.WriteLine($"[MONTHLY DEBUG] scheduleResponse is null or has no items");
                    return null;
                }

                Console.WriteLine($"[MONTHLY DEBUG] Total items received: {scheduleResponse.Items.Count}");

                if (scheduleResponse.Items.Count > 0)
                {
                    var firstItem = scheduleResponse.Items[0];
                    Console.WriteLine($"[MONTHLY DEBUG] Sample item #1 - Fag: '{firstItem.Fag}', Fagnavn: '{firstItem.Fagnavn}', Fravaer: '{firstItem.Fravaer}'");

                    if (scheduleResponse.Items.Count > 1)
                    {
                        var secondItem = scheduleResponse.Items[1];
                        Console.WriteLine($"[MONTHLY DEBUG] Sample item #2 - Fag: '{secondItem.Fag}', Fagnavn: '{secondItem.Fagnavn}', Fravaer: '{secondItem.Fravaer}'");
                    }
                }

                var stuSessions = scheduleResponse.Items.Where(i =>
                    (i.Fag != null && i.Fag.Contains("STU")) ||
                    (i.Fagnavn != null && i.Fagnavn.Contains("Studietid"))
                ).ToList();

                Console.WriteLine($"[MONTHLY DEBUG] STU sessions found: {stuSessions.Count}");

                if (stuSessions.Count > 0)
                {
                    Console.WriteLine($"[MONTHLY DEBUG] First STU session - Fag: '{stuSessions[0].Fag}', Fagnavn: '{stuSessions[0].Fagnavn}', Fravaer: '{stuSessions[0].Fravaer}'");
                }

                var registeredCount = stuSessions.Count(s => s.Fravaer == "M");
                var totalCount = stuSessions.Count;

                Console.WriteLine($"[MONTHLY DEBUG] Registered: {registeredCount}, Total: {totalCount}");

                double percentage = totalCount > 0 ? (double)registeredCount / totalCount * 100 : 0;

                Console.WriteLine($"[MONTHLY DEBUG] Percentage: {percentage:F1}%");

                return new MonthlyAttendanceData
                {
                    RegisteredSessions = registeredCount,
                    TotalSessions = totalCount,
                    AttendancePercentage = percentage
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONTHLY DEBUG] Exception: {ex.Message}");
                Console.WriteLine($"[MONTHLY DEBUG] Stack trace: {ex.StackTrace}");
                return null;
            }
        }
        
        public async Task<WeeklyAttendanceData?> GetWeeklyAttendanceAsync()
        {
            return await FetchWithRetryAsync(FetchWeeklyAttendanceAsync);
        }

        private async Task<WeeklyAttendanceData?> FetchWeeklyAttendanceAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                
                var today = DateTime.Now;
                var dayOfWeek = (int)today.DayOfWeek;
                
                var daysUntilMonday = dayOfWeek == 0 ? -6 : -(dayOfWeek - 1);
                var monday = today.AddDays(daysUntilMonday);
                var friday = monday.AddDays(4);
                
                var startDate = monday.ToString("yyyyMMdd");
                var endDate = friday.ToString("yyyyMMdd");
                
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId},startDate={startDate},endDate={endDate}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var scheduleResponse = JsonSerializer.Deserialize<MonthlyScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (scheduleResponse?.Items == null)
                    return null;

                var dailyStats = new List<DailyAttendance>();
                
                for (int i = 0; i < 5; i++)
                {
                    var currentDay = monday.AddDays(i);
                    var dayString = currentDay.ToString("yyyyMMdd");
                    
                    var dayStuSessions = scheduleResponse.Items.Where(item =>
                        item.Dato == dayString &&
                        ((item.Fag != null && item.Fag.Contains("STU")) || 
                        (item.Fagnavn != null && item.Fagnavn.Contains("Studietid")))
                    ).ToList();
                    
                    var registeredCount = dayStuSessions.Count(s => s.Fravaer == "M");
                    var totalCount = dayStuSessions.Count;
                    
                    dailyStats.Add(new DailyAttendance
                    {
                        DayOfWeek = currentDay.DayOfWeek,
                        Date = currentDay,
                        RegisteredSessions = registeredCount,
                        TotalSessions = totalCount,
                        FillPercentage = totalCount > 0 ? (double)registeredCount / totalCount * 100 : 0
                    });
                }
                
                var weeklyRegistered = dailyStats.Sum(d => d.RegisteredSessions);
                var weeklyTotal = dailyStats.Sum(d => d.TotalSessions);
                var weeklyPercentage = weeklyTotal > 0 ? (double)weeklyRegistered / weeklyTotal * 100 : 0;
                
                return new WeeklyAttendanceData
                {
                    DailyAttendance = dailyStats,
                    TotalRegistered = weeklyRegistered,
                    TotalSessions = weeklyTotal,
                    WeeklyPercentage = weeklyPercentage
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private TimeSpan? ParseTimeString(string? timeStr)
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr.Length != 4)
                return null;

            try
            {
                // Parse time in format "HHmm" (e.g., "0815", "1330")
                if (int.TryParse(timeStr.Substring(0, 2), out int hours) &&
                    int.TryParse(timeStr.Substring(2, 2), out int minutes))
                {
                    if (hours >= 0 && hours < 24 && minutes >= 0 && minutes < 60)
                    {
                        return new TimeSpan(hours, minutes, 0);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private TodayScheduleData ProcessTodaySchedule(List<ScheduleItem> items)
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var todayItems = items.Where(i => i.Dato == today).ToList();

            // Get STU sessions
            var stuSessions = todayItems.Where(i => i.KNavn == "STU").ToList();
            var registeredStuCount = stuSessions.Count(s => s.Typefravaer == "M");
            var totalStuCount = stuSessions.Count;

            var now = DateTime.Now.TimeOfDay;

            // Get current class (if ongoing) - include ALL subjects
            var currentClasses = todayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl) && !string.IsNullOrEmpty(i.SluttKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeString(i.StartKl),
                    EndTime = ParseTimeString(i.SluttKl)
                })
                .Where(x => x.StartTime.HasValue && x.EndTime.HasValue &&
                           x.StartTime.Value <= now && x.EndTime.Value >= now)
                .OrderBy(x => x.StartTime)
                .ToList();

            ScheduleItem? currentClass = currentClasses.FirstOrDefault()?.Item;

            // Get next class - include ALL subjects (STU and regular classes)
            var upcomingClasses = todayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeString(i.StartKl)
                })
                .Where(x => x.StartTime.HasValue && x.StartTime.Value > now)
                .OrderBy(x => x.StartTime)
                .ToList();

            ScheduleItem? nextClass = upcomingClasses.FirstOrDefault()?.Item;

            return new TodayScheduleData
            {
                RegisteredStuSessions = registeredStuCount,
                TotalStuSessions = totalStuCount,
                NextClass = nextClass,
                CurrentClass = currentClass,
                AllTodayItems = todayItems
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class AttendanceSummary
    {
        public int Elevnr { get; set; }
        public int DagerUndervist { get; set; }  // Days taught so far
        public double TimerMinimum { get; set; }  // Minimum hours required
        public double Tilstede { get; set; }      // Total hours attended
        public double Prosent { get; set; }       // Percentage
        public double Saldo { get; set; }         // Over/undertid (overtime/undertime in hours)
    }

    public class AttendanceSummaryResponse
    {
        public List<AttendanceSummary>? Items { get; set; }
        public int TotalResults { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string? Fag { get; set; }
        public string? Stkode { get; set; }
        public string? KlTrinn { get; set; }
        public string? KlId { get; set; }
        public string? KNavn { get; set; }
        public string? GruppeNr { get; set; }
        public string? Dato { get; set; }
        public int Timenr { get; set; }
        public string? StartKl { get; set; }
        public string? SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string? Typefravaer { get; set; }  // "M" means registered
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string? TidsromTilstedevaerelse { get; set; }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem>? Items { get; set; }
        public int TotalResults { get; set; }
    }

    public class TodayScheduleData
    {
        public int RegisteredStuSessions { get; set; }
        public int TotalStuSessions { get; set; }
        public ScheduleItem? NextClass { get; set; }
        public ScheduleItem? CurrentClass { get; set; }
        public List<ScheduleItem>? AllTodayItems { get; set; }
    }
    public class MonthlyAttendanceData
    {
        public int RegisteredSessions { get; set; }
        public int TotalSessions { get; set; }
        public double AttendancePercentage { get; set; }
    }

    public class MonthlyScheduleItem
    {
        public string? Id { get; set; } 
        public string? Dato { get; set; }
        public int Timenr { get; set; }
        public string? Fradato { get; set; }
        public string? Tildato { get; set; }
        public string? Fag { get; set; }
        public string? Fagnavn { get; set; }
        public string? Skoletype { get; set; }
        public string? Romnr { get; set; }
        public string? Kode { get; set; }
        public string? Faglaerer { get; set; }
        public string? ProviderId { get; set; }
        public string? Fravaer { get; set; }  // "M" means registered (Møtt)
        public string? Fravaerstekst { get; set; }
        public string? Merknad { get; set; }
        public string? Egenmelding { get; set; }
        public string? Dokumentert { get; set; }
        public string? Tidssone { get; set; }
        public string? Timetype { get; set; }
    }

    public class MonthlyScheduleResponse
    {
        public List<MonthlyScheduleItem>? Items { get; set; }
        public int TotalResults { get; set; }
    }

    public class WeeklyAttendanceData
    {
        public List<DailyAttendance>? DailyAttendance { get; set; }
        public int TotalRegistered { get; set; }
        public int TotalSessions { get; set; }
        public double WeeklyPercentage { get; set; }
    }

    public class DailyAttendance
    {
        public DayOfWeek DayOfWeek { get; set; }
        public DateTime Date { get; set; }
        public int RegisteredSessions { get; set; }
        public int TotalSessions { get; set; }
        public double FillPercentage { get; set; } // 0-100 for the visual fill
    }
}   