using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Services.Http;
using AkademiTrack.Services.DependencyInjection;
using System.Diagnostics;

namespace AkademiTrack.Services
{
    public class AttendanceDataService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ICacheService? _cacheService;
        private UserParameters? _userParameters;
        private Dictionary<string, string>? _cookies;
        private bool _disposed = false;
        
        private ILoggingService? _loggingService;

        public void SetLoggingService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        public AttendanceDataService(ICacheService? cacheService = null)
        {
            _httpClient = HttpClientFactory.DefaultClient;
            _cacheService = cacheService;
        }

        public void SetCredentials(UserParameters parameters, Dictionary<string, string> cookies)
        {
            _userParameters = parameters;
            _cookies = cookies;
            
            // Invalidate cache when credentials change
            if (_cacheService != null)
            {
                _cacheService.RemoveByPattern("attendance_");
                _cacheService.RemoveByPattern("today_schedule_");
                _cacheService.RemoveByPattern("monthly_attendance_");
                _cacheService.RemoveByPattern("weekly_attendance_");
            }
        }

        /// <summary>
        /// Invalidate all cached attendance data
        /// </summary>
        public void InvalidateCache()
        {
            if (_cacheService != null)
            {
                _cacheService.RemoveByPattern("attendance_");
                _cacheService.RemoveByPattern("today_schedule_");
                _cacheService.RemoveByPattern("monthly_attendance_");
                _cacheService.RemoveByPattern("weekly_attendance_");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            // Note: _httpClient is shared from HttpClientFactory, so we don't dispose it
            _disposed = true;
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
                    _loggingService?.LogInfo("Data kunne ikke hentes - prøver å oppdatere autentisering");
                    
                    var notificationService = ServiceContainer.GetService<INotificationService>();
                    using var authService = new AuthenticationService(notificationService);
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
                        
                        _loggingService?.LogSuccess("Autentisering oppdatert");
                        
                        // Retry with fresh cookies
                        result = await fetchFunc();
                        
                        if (result == null)
                        {
                            _loggingService?.LogError("Kunne ikke hente data selv etter re-autentisering");
                        }
                    }
                    else
                    {
                        _loggingService?.LogError("Re-autentisering feilet");
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
            if (_cacheService != null && _userParameters != null)
            {
                var cacheKey = $"attendance_summary_{_userParameters.FylkeId}_{_userParameters.SkoleId}_{DateTime.Today:yyyyMMdd}";
                
                return await _cacheService.GetOrSetAsync(
                    cacheKey,
                    () => FetchWithRetryAsync(FetchAttendanceSummaryAsync),
                    TimeSpan.FromMinutes(10) // Cache for 10 minutes
                );
            }
            
            return await FetchWithRetryAsync(FetchAttendanceSummaryAsync);
        }

        private async Task<AttendanceSummary?> FetchAttendanceSummaryAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                {
                    _loggingService?.LogError("[ATTENDANCE] Missing parameters or cookies for summary fetch");
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId))
                {
                    _loggingService?.LogError("[ATTENDANCE] Missing JSESSIONID cookie");
                    return null;
                }

                var url = $"https://iskole.net/iskole_elev/rest/v0/VoFravaer_oppmote_studietid_sum;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=25&offset=0&totalResults=true";

                _loggingService?.LogDebug($"🌐 [ATTENDANCE] Fetching summary from: {url.Replace(jsessionId, "***")}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies?.Select(c => $"{c.Key}={c.Value}") ?? Enumerable.Empty<string>());
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                
                _loggingService?.LogDebug($"[ATTENDANCE] Response status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    _loggingService?.LogError($"[ATTENDANCE] HTTP error {response.StatusCode} when fetching summary");
                    
                    // Log the response content to help debug parameter issues
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Length < 500)
                    {
                        _loggingService?.LogDebug($"🔍 [ATTENDANCE] Error response: {errorContent}");
                    }
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _loggingService?.LogDebug($"🌐 [ATTENDANCE] Response length: {json.Length} characters");
                
                if (json.Length < 50)
                {
                    _loggingService?.LogDebug($"🌐 [ATTENDANCE] Short response content: {json}");
                }

                var summaryResponse = JsonSerializer.Deserialize<AttendanceSummaryResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (summaryResponse?.Items?.FirstOrDefault() != null)
                {
                    _loggingService?.LogSuccess("[ATTENDANCE] Successfully fetched attendance summary");
                }
                else
                {
                    _loggingService?.LogWarning("⚠️ [ATTENDANCE] Summary response was empty or null");
                    _loggingService?.LogDebug($"🔍 [ATTENDANCE] Raw response content: {json.Substring(0, Math.Min(300, json.Length))}");
                }

                return summaryResponse?.Items?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[ATTENDANCE] Exception in FetchAttendanceSummaryAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<TodayScheduleData?> GetTodayScheduleAsync()
        {
            if (_cacheService != null && _userParameters != null)
            {
                var cacheKey = $"today_schedule_{_userParameters.FylkeId}_{_userParameters.SkoleId}_{DateTime.Today:yyyyMMdd}";
                
                return await _cacheService.GetOrSetAsync(
                    cacheKey,
                    () => FetchWithRetryAsync(FetchTodayScheduleAsync),
                    TimeSpan.FromMinutes(5) // Cache for 5 minutes (schedule changes more frequently)
                );
            }
            
            return await FetchWithRetryAsync(FetchTodayScheduleAsync);
        }

        private async Task<TodayScheduleData?> FetchTodayScheduleAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                {
                    _loggingService?.LogError("[TODAY] Missing parameters or cookies for today schedule fetch");
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId))
                {
                    _loggingService?.LogError("[TODAY] Missing JSESSIONID cookie");
                    return null;
                }
                
                // Fetch both APIs
                var dailyData = await FetchDailyScheduleAsync();
                var monthlyData = await FetchMonthlyScheduleAsync();
                
                if (dailyData == null || monthlyData == null)
                {
                    _loggingService?.LogError("[TODAY] Failed to fetch both daily and monthly data");
                    return null;
                }

                _loggingService?.LogSuccess($"[TODAY] Fetched {dailyData.Count} daily items and {monthlyData.Count} monthly items");
                return ProcessCombinedScheduleData(dailyData, monthlyData);
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[TODAY] Exception in FetchTodayScheduleAsync: {ex.Message}");
                return null;
            }
        }

        private async Task<List<DailyScheduleItem>?> FetchDailyScheduleAsync()
        {
            try
            {
                var jsessionId = _cookies?.GetValueOrDefault("JSESSIONID", "") ?? "";
                var today = DateTime.Now.ToString("yyyyMMdd");
                
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_dato;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters?.FylkeId},planperi={_userParameters?.PlanPeri},skoleid={_userParameters?.SkoleId},dato={today}&onlyData=true";

                _loggingService?.LogDebug($"🌐 [DAILY] Fetching daily schedule from: {url.Replace(jsessionId, "***")}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies?.Select(c => $"{c.Key}={c.Value}") ?? Enumerable.Empty<string>());
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) 
                {
                    _loggingService?.LogError($"[DAILY] HTTP error {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _loggingService?.LogDebug($"🌐 [DAILY] Response length: {json.Length} characters");
                
                var dailyResponse = JsonSerializer.Deserialize<SimpleDailyScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (dailyResponse?.Items == null)
                {
                    _loggingService?.LogWarning("⚠️ [DAILY] Daily response was null or had no items");
                    return null;
                }

                _loggingService?.LogSuccess($"[DAILY] Successfully fetched {dailyResponse.Items.Count} daily items");
                return dailyResponse.Items;
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[DAILY] Exception: {ex.Message}");
                return null;
            }
        }

        private async Task<List<MonthlyScheduleItem>?> FetchMonthlyScheduleAsync()
        {
            try
            {
                var jsessionId = _cookies?.GetValueOrDefault("JSESSIONID", "") ?? "";
                var today = DateTime.Now.ToString("yyyyMMdd");
                var tomorrow = DateTime.Now.AddDays(1).ToString("yyyyMMdd");
                
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters?.FylkeId},planperi={_userParameters?.PlanPeri},skoleid={_userParameters?.SkoleId},startDate={today},endDate={tomorrow}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies?.Select(c => $"{c.Key}={c.Value}") ?? Enumerable.Empty<string>());
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var scheduleResponse = JsonSerializer.Deserialize<MonthlyScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return scheduleResponse?.Items;
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[MONTHLY] Exception: {ex.Message}");
                return null;
            }
        }

        private TodayScheduleData ProcessCombinedScheduleData(List<DailyScheduleItem> dailyItems, List<MonthlyScheduleItem> monthlyItems)
        {
            var today = DateTime.Now.Date;
            var now = DateTime.Now;

            // Filter daily items (all are for today since we fetch by date)
            var validDailyItems = dailyItems
                .Where(item => !string.IsNullOrEmpty(item.StartKl) &&
                              !string.IsNullOrEmpty(item.SluttKl))
                .ToList();

            // Get all STU sessions for today
            var todayStuSessions = validDailyItems
                .Where(item => item.Fag != null && item.Fag.Contains("STU"))
                .ToList();
            
            // Get all regular (non-STU) classes for today
            var regularClasses = validDailyItems
                .Where(item => item.Fag != null && !item.Fag.Contains("STU"))
                .ToList();
            
            // Filter out STU sessions that overlap with regular classes
            var validStuSessions = new List<DailyScheduleItem>();
            foreach (var stuSession in todayStuSessions)
            {
                bool hasConflict = false;
                
                // Check if this STU session overlaps with any regular class
                foreach (var regularClass in regularClasses)
                {
                    if (DoDailyItemsOverlap(stuSession, regularClass))
                    {
                        hasConflict = true;
                        _loggingService?.LogDebug($"[TODAY OVERLAP] STU '{stuSession.Fag}' ({stuSession.StartKl}-{stuSession.SluttKl}) overlaps with '{regularClass.Fag}' ({regularClass.StartKl}-{regularClass.SluttKl}) - excluding from count");
                        break;
                    }
                }
                
                if (!hasConflict)
                {
                    validStuSessions.Add(stuSession);
                }
            }
            
            // Count only valid (non-overlapping) STU sessions
            var registeredStuCount = 0;
            var totalStuCount = validStuSessions.Count;

            foreach (var stuSession in validStuSessions)
            {
                var matchingMonthly = monthlyItems.FirstOrDefault(m => m.Timenr == stuSession.Timenr);
                if (matchingMonthly?.Fravaer == "M")
                {
                    registeredStuCount++;
                }
            }

            // Find next class (including STU, after current time)
            var nextDailyItem = validDailyItems
                .Where(item => IsUpcomingClassSimple(item, now))
                .OrderBy(item => item.StartKl)
                .FirstOrDefault();

            ScheduleItem? nextClass = null;
            if (nextDailyItem != null)
            {
                // Find matching monthly item for subject name
                var matchingMonthlyItem = monthlyItems.FirstOrDefault(m => m.Timenr == nextDailyItem.Timenr);
                nextClass = CombineScheduleItemSimple(nextDailyItem, matchingMonthlyItem);
            }

            // Find current class
            var currentDailyItem = validDailyItems
                .Where(item => IsCurrentClassSimple(item, now))
                .FirstOrDefault();

            ScheduleItem? currentClass = null;
            if (currentDailyItem != null)
            {
                var matchingMonthlyItem = monthlyItems.FirstOrDefault(m => m.Timenr == currentDailyItem.Timenr);
                currentClass = CombineScheduleItemSimple(currentDailyItem, matchingMonthlyItem);
            }

            return new TodayScheduleData
            {
                RegisteredStuSessions = registeredStuCount,
                TotalStuSessions = totalStuCount,
                NextClass = nextClass,
                CurrentClass = currentClass,
                AllTodayItems = new List<ScheduleItem>() // Not needed for next class display
            };
        }
        
        private bool DoDailyItemsOverlap(DailyScheduleItem session1, DailyScheduleItem session2)
        {
            try
            {
                // Parse time strings (format: "HH:mm" or "HHmm")
                if (TimeSpan.TryParse(session1.StartKl, out var start1) &&
                    TimeSpan.TryParse(session1.SluttKl, out var end1) &&
                    TimeSpan.TryParse(session2.StartKl, out var start2) &&
                    TimeSpan.TryParse(session2.SluttKl, out var end2))
                {
                    return start1 < end2 && start2 < end1;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private ScheduleItem CombineScheduleItemSimple(DailyScheduleItem dailyItem, MonthlyScheduleItem? monthlyItem)
        {
            string displayName = "";
            string roomNumber = "";

            if (monthlyItem != null)
            {
                // Use subject name from monthly API
                displayName = GetCleanSubjectName(monthlyItem.Fagnavn);
                roomNumber = monthlyItem.Romnr?.Trim() ?? "";
            }
            else
            {
                // Fallback to daily item subject
                displayName = dailyItem.Fag ?? "";
            }

            var today = DateTime.Now.ToString("yyyyMMdd");

            return new ScheduleItem
            {
                Id = dailyItem.Id,
                Fag = dailyItem.Fag,
                Dato = today,
                Timenr = dailyItem.Timenr,
                StartKl = dailyItem.StartKl,  // Use time from daily API
                SluttKl = dailyItem.SluttKl,  // Use time from daily API
                KNavn = ExtractSubjectCode(dailyItem.Fag),
                DisplayName = displayName,    // Use name from monthly API
                Romnr = roomNumber,          // Use room from monthly API
                Typefravaer = monthlyItem?.Fravaer == "M" ? "M" : null,
                UndervisningPaagaar = 1
            };
        }

        private bool IsCurrentClassSimple(DailyScheduleItem item, DateTime now)
        {
            if (!TimeSpan.TryParse($"{item.StartKl?.PadLeft(4, '0').Insert(2, ":")}", out var startTime) ||
                !TimeSpan.TryParse($"{item.SluttKl?.PadLeft(4, '0').Insert(2, ":")}", out var endTime))
            {
                return false;
            }

            var today = DateTime.Now.Date;
            var classStart = today.Add(startTime);
            var classEnd = today.Add(endTime);

            return now >= classStart && now <= classEnd;
        }

        private bool IsUpcomingClassSimple(DailyScheduleItem item, DateTime now)
        {
            if (!TimeSpan.TryParse($"{item.StartKl?.PadLeft(4, '0').Insert(2, ":")}", out var startTime))
            {
                return false;
            }

            var today = DateTime.Now.Date;
            var classStart = today.Add(startTime);
            return classStart > now;
        }

        public async Task<MonthlyAttendanceData?> GetMonthlyAttendanceAsync()
        {
            if (_cacheService != null && _userParameters != null)
            {
                var cacheKey = $"monthly_attendance_{_userParameters.FylkeId}_{_userParameters.SkoleId}_{DateTime.Today:yyyyMM}";
                
                return await _cacheService.GetOrSetAsync(
                    cacheKey,
                    () => FetchWithRetryAsync(FetchMonthlyAttendanceAsync),
                    TimeSpan.FromMinutes(15) // Cache for 15 minutes (monthly data changes less frequently)
                );
            }
            
            return await FetchWithRetryAsync(FetchMonthlyAttendanceAsync);
        }

        private async Task<MonthlyAttendanceData?> FetchMonthlyAttendanceAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                {
                    Debug.WriteLine("[MONTHLY DEBUG] No parameters or cookies available");
                    return null;
                }

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");

                var now = DateTime.Now;
                var firstDayOfMonth = new DateTime(now.Year, now.Month, 1);
                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                var startDate = firstDayOfMonth.ToString("yyyyMMdd");
                var endDate = lastDayOfMonth.ToString("yyyyMMdd");

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

                // Group by date first
                var itemsByDate = scheduleResponse.Items.GroupBy(i => i.Dato).ToList();
                
                var validStuSessions = new List<MonthlyScheduleItem>();
                
                foreach (var dayGroup in itemsByDate)
                {
                    var daySessions = dayGroup.ToList();
                    
                    // Get STU sessions for this day
                    var dayStuSessions = daySessions.Where(item =>
                        (item.Fag != null && item.Fag.Contains("STU")) ||
                        (item.Fagnavn != null && item.Fagnavn.Contains("Studietid"))
                    ).ToList();
                    
                    // Get regular classes for this day
                    var regularClasses = daySessions.Where(item =>
                        item.Fag != null &&
                        !item.Fag.Contains("STU") &&
                        item.Fagnavn != null &&
                        !item.Fagnavn.Contains("Studietid")
                    ).ToList();
                    
                    // Filter STU sessions - remove any that conflict with regular classes
                    foreach (var stuSession in dayStuSessions)
                    {
                        bool hasConflict = false;
                        
                        foreach (var regularClass in regularClasses)
                        {
                            if (DoSessionsOverlap(stuSession, regularClass))
                            {
                                hasConflict = true;
                                break;
                            }
                        }
                        
                        if (!hasConflict)
                        {
                            validStuSessions.Add(stuSession);
                        }
                    }
                }

                var registeredCount = validStuSessions.Count(s => s.Fravaer == "M");
                var totalCount = validStuSessions.Count;

                double percentage = totalCount > 0 ? (double)registeredCount / totalCount * 100 : 0;

                return new MonthlyAttendanceData
                {
                    RegisteredSessions = registeredCount,
                    TotalSessions = totalCount,
                    AttendancePercentage = percentage
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MONTHLY DEBUG] Exception: {ex.Message}");
                return null;
            }
        }

        private bool DoSessionsOverlap(MonthlyScheduleItem session1, MonthlyScheduleItem session2)
        {
            try
            {
                if (DateTime.TryParse(session1.Fradato, out var start1) &&
                    DateTime.TryParse(session1.Tildato, out var end1) &&
                    DateTime.TryParse(session2.Fradato, out var start2) &&
                    DateTime.TryParse(session2.Tildato, out var end2))
                {
                    return start1 < end2 && start2 < end1;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool DoScheduleItemsOverlap(ScheduleItem session1, ScheduleItem session2)
        {
            try
            {
                // Both sessions must be on the same date
                if (session1.Dato != session2.Dato)
                    return false;
                
                // Parse time strings (format: "HH:mm")
                if (TimeSpan.TryParse(session1.StartKl, out var start1) &&
                    TimeSpan.TryParse(session1.SluttKl, out var end1) &&
                    TimeSpan.TryParse(session2.StartKl, out var start2) &&
                    TimeSpan.TryParse(session2.SluttKl, out var end2))
                {
                    return start1 < end2 && start2 < end1;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        public async Task<WeeklyAttendanceData?> GetWeeklyAttendanceAsync()
        {
            if (_cacheService != null && _userParameters != null)
            {
                var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1); // Monday of current week
                var cacheKey = $"weekly_attendance_{_userParameters.FylkeId}_{_userParameters.SkoleId}_{weekStart:yyyyMMdd}";
                
                return await _cacheService.GetOrSetAsync(
                    cacheKey,
                    () => FetchWithRetryAsync(FetchWeeklyAttendanceAsync),
                    TimeSpan.FromMinutes(10) // Cache for 10 minutes
                );
            }
            
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
                    
                    // Get ALL sessions for this day
                    var allDaySessions = scheduleResponse.Items
                        .Where(item => item.Dato == dayString)
                        .ToList();
                    
                    // Get STU sessions
                    var dayStuSessions = allDaySessions.Where(item =>
                        (item.Fag != null && item.Fag.Contains("STU")) || 
                        (item.Fagnavn != null && item.Fagnavn.Contains("Studietid"))
                    ).ToList();
                    
                    // Get regular classes (non-STU)
                    var regularClasses = allDaySessions.Where(item =>
                        item.Fag != null &&
                        !item.Fag.Contains("STU") &&
                        item.Fagnavn != null &&
                        !item.Fagnavn.Contains("Studietid")
                    ).ToList();
                    
                    // Filter STU sessions - remove any that conflict with regular classes
                    var validStuSessions = new List<MonthlyScheduleItem>();
                    
                    foreach (var stuSession in dayStuSessions)
                    {
                        bool hasConflict = false;
                        
                        foreach (var regularClass in regularClasses)
                        {
                            if (DoSessionsOverlap(stuSession, regularClass))
                            {
                                hasConflict = true;
                                _loggingService?.LogDebug($"[CONFLICT] STU '{stuSession.Fagnavn}' overlaps with '{regularClass.Fagnavn}' on {currentDay.DayOfWeek}");
                                break;
                            }
                        }
                        
                        if (!hasConflict)
                        {
                            validStuSessions.Add(stuSession);
                        }
                    }
                    
                    var registeredCount = validStuSessions.Count(s => s.Fravaer == "M");
                    var totalCount = validStuSessions.Count;
                    
                    int conflictCount = dayStuSessions.Count - validStuSessions.Count;
                    
                    _loggingService?.LogDebug($"[WEEKLY] {currentDay.DayOfWeek}: {registeredCount}/{totalCount} valid STU ({conflictCount} conflicts filtered out)");
                    
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
            catch (Exception ex)
            {
                _loggingService?.LogError($"Weekly attendance error: {ex.Message}");
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

        private TodayScheduleData ProcessTodayScheduleFromMonthlyData(List<MonthlyScheduleItem> items)
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var todayItems = items.Where(i => i.Dato == today).ToList();

            // Get STU sessions for today only
            var stuSessions = todayItems.Where(i => 
                (i.Fag != null && i.Fag.Contains("STU")) ||
                (i.Fagnavn != null && i.Fagnavn.Contains("Studietid"))
            ).ToList();
            
            // Get regular classes for today only (non-STU)
            var regularClasses = todayItems.Where(i => 
                i.Fag != null &&
                !i.Fag.Contains("STU") &&
                i.Fagnavn != null &&
                !i.Fagnavn.Contains("Studietid")
            ).ToList();
            
            // Filter STU sessions - remove any that conflict with regular classes
            var validStuSessions = new List<MonthlyScheduleItem>();
            
            foreach (var stuSession in stuSessions)
            {
                bool hasConflict = false;
                
                foreach (var regularClass in regularClasses)
                {
                    if (DoSessionsOverlap(stuSession, regularClass))
                    {
                        hasConflict = true;
                        break;
                    }
                }
                
                if (!hasConflict)
                {
                    validStuSessions.Add(stuSession);
                }
            }
            
            var registeredStuCount = validStuSessions.Count(s => s.Fravaer == "M");
            var totalStuCount = validStuSessions.Count;

            var now = DateTime.Now;

            // Find current class from today's items only
            var currentClasses = todayItems
                .Where(i => !string.IsNullOrEmpty(i.Fradato) && !string.IsNullOrEmpty(i.Tildato))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseDateTime(i.Fradato),
                    EndTime = ParseDateTime(i.Tildato)
                })
                .Where(x => x.StartTime.HasValue && x.EndTime.HasValue &&
                           x.StartTime.Value <= now && x.EndTime.Value >= now)
                .OrderBy(x => x.StartTime)
                .ToList();

            MonthlyScheduleItem? currentClass = currentClasses.FirstOrDefault()?.Item;

            // Find next class from ALL items (today and tomorrow), including STU sessions
            var upcomingClasses = items
                .Where(i => !string.IsNullOrEmpty(i.Fradato))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseDateTime(i.Fradato)
                })
                .Where(x => x.StartTime.HasValue && x.StartTime.Value > now)
                .OrderBy(x => x.StartTime)
                .ToList();

            MonthlyScheduleItem? nextClass = upcomingClasses.FirstOrDefault()?.Item;

            return new TodayScheduleData
            {
                RegisteredStuSessions = registeredStuCount,
                TotalStuSessions = totalStuCount,
                NextClass = ConvertToScheduleItem(nextClass),
                CurrentClass = ConvertToScheduleItem(currentClass),
                AllTodayItems = todayItems.Select(ConvertToScheduleItem).Where(item => item != null).ToList()!
            };
        }

        private DateTime? ParseDateTime(string? dateTimeStr)
        {
            if (string.IsNullOrEmpty(dateTimeStr))
                return null;

            try
            {
                if (DateTime.TryParse(dateTimeStr, out var dateTime))
                {
                    return dateTime;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private ScheduleItem? ConvertToScheduleItem(MonthlyScheduleItem? monthlyItem)
        {
            if (monthlyItem == null)
                return null;

            string cleanSubjectCode = ExtractSubjectCode(monthlyItem.Fag);
            string displayName = GetCleanSubjectName(monthlyItem.Fagnavn);

            return new ScheduleItem
            {
                Id = int.TryParse(monthlyItem.Id, out var id) ? id : 0,
                Fag = monthlyItem.Fag,
                Dato = monthlyItem.Dato,
                Timenr = monthlyItem.Timenr,
                StartKl = FormatTimeFromDateTime(monthlyItem.Fradato),
                SluttKl = FormatTimeFromDateTime(monthlyItem.Tildato),
                KNavn = cleanSubjectCode,
                DisplayName = displayName, // Store the clean full name
                Romnr = monthlyItem.Romnr?.Trim(), // Add room information
                Typefravaer = monthlyItem.Fravaer == "M" ? "M" : null,
                UndervisningPaagaar = 1
            };
        }

        private string ExtractSubjectCode(string? fag)
        {
            if (string.IsNullOrEmpty(fag))
                return "";

            var parts = fag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return parts[1]; // Return the subject code part
            }
            
            return fag; // Return as-is if no space found
        }

        private string GetCleanSubjectName(string? fagnavn)
        {
            if (string.IsNullOrEmpty(fagnavn))
                return "";

            // Remove any prefix before the actual subject name
            // Examples: "NAT1018 Naturfag" -> "Naturfag", "Studietid" -> "Studietid"
            
            // Check if it contains a course code pattern (letters followed by numbers)
            var parts = fagnavn.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length >= 2)
            {
                var firstPart = parts[0];
                // If first part looks like a course code (contains both letters and numbers)
                if (firstPart.Any(char.IsLetter) && firstPart.Any(char.IsDigit))
                {
                    // Return everything after the first part
                    var cleanName = string.Join(" ", parts.Skip(1));
                    
                    // Simple shortening for very long names
                    if (cleanName.Length > 25)
                    {
                        cleanName = cleanName.Replace("hovedmål, skriftlig", "skriftlig");
                        cleanName = cleanName.Replace("hovedmål, muntlig", "muntlig");
                        cleanName = cleanName.Replace("sidemål, skriftlig", "nynorsk skriftlig");
                        cleanName = cleanName.Replace("sidemål, muntlig", "nynorsk muntlig");
                    }
                    
                    return cleanName;
                }
            }
            
            // Return as-is if no course code pattern found
            return fagnavn;
        }

        private string FormatTimeFromDateTime(string? dateTimeStr)
        {
            if (string.IsNullOrEmpty(dateTimeStr))
                return "";

            try
            {
                if (DateTime.TryParse(dateTimeStr, out var dateTime))
                {
                    return dateTime.ToString("HHmm");
                }
                return "";
            }
            catch
            {
                return "";
            }
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
        public string? DisplayName { get; set; } // Full subject name from Fagnavn
        public string? GruppeNr { get; set; }
        public string? Dato { get; set; }
        public int Timenr { get; set; }
        public string? StartKl { get; set; }
        public string? SluttKl { get; set; }
        public string? Romnr { get; set; } // Room number
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

    // Daily API models for better time accuracy
    public class DailyScheduleItem
    {
        public int Id { get; set; }
        public int Timenr { get; set; }
        public string? Fag { get; set; }      // "PB3A STU", "PB3A NOR", etc.
        public string? StartKl { get; set; }  // "0815" format
        public string? SluttKl { get; set; }  // "0900" format
    }

    public class SimpleDailyScheduleResponse
    {
        public List<DailyScheduleItem>? Items { get; set; }
        public int Count { get; set; }
        public bool HasMore { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}
