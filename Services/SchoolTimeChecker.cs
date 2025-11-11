using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Manages intelligent auto-start based on school hours and STU session times.
    /// Checks time online first, falls back to local time if offline.
    /// Prevents time manipulation by students.
    /// </summary>
    public static class SchoolTimeChecker
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static DateTime? _lastOnlineTimeCheck = null;
        private static TimeSpan _onlineTimeOffset = TimeSpan.Zero;

        /// <summary>
        /// Gets the current time, preferring online time to prevent manipulation
        /// </summary>
        public static async Task<DateTime> GetTrustedCurrentTimeAsync()
        {
            try
            {
                // Try to get time from world time API
                var response = await _httpClient.GetAsync("http://worldtimeapi.org/api/timezone/Europe/Oslo");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var timeData = JsonSerializer.Deserialize<JsonElement>(json);
                    
                    if (timeData.TryGetProperty("datetime", out var datetimeElement))
                    {
                        var onlineTime = DateTime.Parse(datetimeElement.GetString()!);
                        
                        // Calculate offset between online and local time
                        _onlineTimeOffset = onlineTime - DateTime.Now;
                        _lastOnlineTimeCheck = DateTime.Now;
                        
                        Debug.WriteLine($"[TIME CHECK] Online time: {onlineTime:yyyy-MM-dd HH:mm:ss}");
                        Debug.WriteLine($"[TIME CHECK] Local time:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        Debug.WriteLine($"[TIME CHECK] Offset: {_onlineTimeOffset.TotalSeconds:F0} seconds");
                        
                        return onlineTime;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TIME CHECK] Online time check failed: {ex.Message}");
            }

            // Fallback: Use local time with last known offset
            var localTime = DateTime.Now;
            
            if (_lastOnlineTimeCheck.HasValue)
            {
                // If we checked online recently (within last 30 minutes), apply the offset
                if ((DateTime.Now - _lastOnlineTimeCheck.Value).TotalMinutes < 30)
                {
                    var adjustedTime = localTime + _onlineTimeOffset;
                    Debug.WriteLine($"[TIME CHECK] Using local time with offset: {adjustedTime:yyyy-MM-dd HH:mm:ss}");
                    return adjustedTime;
                }
            }

            Debug.WriteLine($"[TIME CHECK] Using raw local time: {localTime:yyyy-MM-dd HH:mm:ss}");
            return localTime;
        }

        /// <summary>
        /// Checks if current time is within school hours (considering STU sessions)
        /// </summary>
        public static async Task<bool> IsWithinSchoolHoursAsync()
        {
            var now = await GetTrustedCurrentTimeAsync();
            
            // Only check Monday-Friday
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            {
                Debug.WriteLine($"[SCHOOL HOURS] Weekend - outside school hours");
                return false;
            }

            var currentTime = now.TimeOfDay;
            
            // Monday: 9:00 - 15:30
            if (now.DayOfWeek == DayOfWeek.Monday)
            {
                bool isWithinHours = currentTime >= TimeSpan.FromHours(9) && 
                                    currentTime <= TimeSpan.FromHours(15.5);
                Debug.WriteLine($"[SCHOOL HOURS] Monday {now:HH:mm} - {(isWithinHours ? "INSIDE" : "OUTSIDE")} school hours (9:00-15:30)");
                return isWithinHours;
            }
            
            // Tuesday-Friday: 8:15 - 15:30
            bool isSchoolDay = currentTime >= new TimeSpan(8, 15, 0) && 
                              currentTime <= TimeSpan.FromHours(15.5);
            Debug.WriteLine($"[SCHOOL HOURS] {now.DayOfWeek} {now:HH:mm} - {(isSchoolDay ? "INSIDE" : "OUTSIDE")} school hours (8:15-15:30)");
            return isSchoolDay;
        }

        /// <summary>
        /// Checks if automation should auto-start based on schedule and last run date
        /// </summary>
        public static async Task<(bool shouldStart, string reason, DateTime? nextStartTime)> ShouldAutoStartAutomationAsync()
        {
            try
            {
                var now = await GetTrustedCurrentTimeAsync();
                Debug.WriteLine($"=== AUTO-START CHECK at {now:yyyy-MM-dd HH:mm:ss} ===");

                // Check last run date first
                var lastRunDate = await GetLastSuccessfulRunDateAsync();
                var today = now.Date;

                if (lastRunDate.HasValue && lastRunDate.Value.Date == today)
                {
                    Debug.WriteLine($"[AUTO-START] Already ran today ({lastRunDate:yyyy-MM-dd HH:mm})");
                    var nextDay = GetNextSchoolDay(now);
                    var nextStart = GetSchoolStartTime(nextDay);
                    return (false, $"Allerede fullført i dag kl. {lastRunDate:HH:mm}", nextStart);
                }

                // Check if we're within school hours
                if (!await IsWithinSchoolHoursAsync())
                {
                    var nextStart = GetNextSchoolStartTime(now);
                    if (nextStart.HasValue)
                    {
                        var timeUntil = nextStart.Value - now;
                        string waitMessage = timeUntil.TotalHours < 24 
                            ? $"Starter automatisk {nextStart:HH:mm} ({GetNorwegianDayName(nextStart.Value.DayOfWeek)})"
                            : $"Starter automatisk {nextStart:dddd HH:mm}";
                        return (false, waitMessage, nextStart);
                    }
                    return (false, "Utenfor skoletid", null);
                }

                Debug.WriteLine($"[AUTO-START] ✓ Should start - within school hours and not run today");
                return (true, $"Starter automatisering for {GetNorwegianDayName(now.DayOfWeek)} kl. {now:HH:mm}", null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error: {ex.Message}");
                return (false, $"Feil ved sjekk: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets the next school start time
        /// </summary>
        private static DateTime? GetNextSchoolStartTime(DateTime from)
        {
            var current = from.Date;
            
            // Check today first if we haven't passed school hours
            if (from.TimeOfDay < TimeSpan.FromHours(15.5))
            {
                var todayStart = GetSchoolStartTime(current);
                if (todayStart > from)
                    return todayStart;
            }

            // Check next 7 days
            for (int i = 1; i <= 7; i++)
            {
                var checkDate = current.AddDays(i);
                if (checkDate.DayOfWeek != DayOfWeek.Saturday && checkDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    return GetSchoolStartTime(checkDate);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the school start time for a specific date
        /// </summary>
        private static DateTime GetSchoolStartTime(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Monday)
                return date.Date.AddHours(9); // Monday starts at 9:00
            else
                return date.Date.Add(new TimeSpan(8, 15, 0)); // Other days at 8:15
        }

        /// <summary>
        /// Gets the next school day from a given date
        /// </summary>
        private static DateTime GetNextSchoolDay(DateTime from)
        {
            var nextDay = from.Date.AddDays(1);
            while (nextDay.DayOfWeek == DayOfWeek.Saturday || nextDay.DayOfWeek == DayOfWeek.Sunday)
            {
                nextDay = nextDay.AddDays(1);
            }
            return nextDay;
        }

        /// <summary>
        /// Gets Norwegian day name
        /// </summary>
        private static string GetNorwegianDayName(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "mandag",
                DayOfWeek.Tuesday => "tirsdag",
                DayOfWeek.Wednesday => "onsdag",
                DayOfWeek.Thursday => "torsdag",
                DayOfWeek.Friday => "fredag",
                DayOfWeek.Saturday => "lørdag",
                DayOfWeek.Sunday => "søndag",
                _ => day.ToString()
            };
        }

        /// <summary>
        /// Marks today as successfully completed
        /// </summary>
        public static async Task MarkTodayAsCompletedAsync()
        {
            try
            {
                var now = await GetTrustedCurrentTimeAsync();
                var filePath = GetLastRunFilePath();
                var data = new { lastRun = now, completed = true };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(filePath, json);
                
                Debug.WriteLine($"[AUTO-START] Marked {now:yyyy-MM-dd} as completed at {now:HH:mm}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error saving completion: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the last successful run date
        /// </summary>
        private static async Task<DateTime?> GetLastSuccessfulRunDateAsync()
        {
            try
            {
                var filePath = GetLastRunFilePath();
                if (!System.IO.File.Exists(filePath))
                    return null;

                var json = await System.IO.File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (data.TryGetProperty("lastRun", out var lastRunElement))
                {
                    return DateTime.Parse(lastRunElement.GetString()!);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error reading last run: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Resets the daily completion flag (for testing or manual reset)
        /// </summary>
        public static void ResetDailyCompletion()
        {
            try
            {
                var filePath = GetLastRunFilePath();
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    Debug.WriteLine("[AUTO-START] Daily completion flag reset");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error resetting completion: {ex.Message}");
            }
        }

        private static string GetLastRunFilePath()
        {
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            System.IO.Directory.CreateDirectory(appDataDir);
            return System.IO.Path.Combine(appDataDir, "last_auto_run.json");
        }
    }
}