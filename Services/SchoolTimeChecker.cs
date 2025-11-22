using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;

namespace AkademiTrack.Services
{
    public static class SchoolTimeChecker
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static DateTime? _lastOnlineTimeCheck = null;
        private static TimeSpan _onlineTimeOffset = TimeSpan.Zero;

        private static DateTime _lastNotificationTime = DateTime.MinValue;
        private static string _lastNotificationMessage = "";
        private static bool _hasNotifiedTodayOutsideHours = false;

        private static bool _hasPerformedInitialCheck = false;

        public static async Task<DateTime> GetTrustedCurrentTimeAsync(bool silent = false)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                
                var response = await _httpClient.GetAsync(
                    "http://worldtimeapi.org/api/timezone/Europe/Oslo", 
                    cts.Token
                ).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var timeData = JsonSerializer.Deserialize<JsonElement>(json);

                    if (timeData.TryGetProperty("datetime", out var datetimeElement))
                    {
                        var onlineTime = DateTime.Parse(datetimeElement.GetString()!);

                        _onlineTimeOffset = onlineTime - DateTime.Now;
                        _lastOnlineTimeCheck = DateTime.Now;

                        if (!silent)
                        {
                            Debug.WriteLine($"[TIME CHECK] Online time: {onlineTime:yyyy-MM-dd HH:mm:ss}");
                            Debug.WriteLine($"[TIME CHECK] Local time:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            Debug.WriteLine($"[TIME CHECK] Offset: {_onlineTimeOffset.TotalSeconds:F0} seconds");
                        }

                        return onlineTime;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!silent)
                    Debug.WriteLine($"[TIME CHECK] Online time check timed out - using local time");
            }
            catch (Exception ex)
            {
                if (!silent)
                    Debug.WriteLine($"[TIME CHECK] Online time check failed: {ex.Message}");
            }

            var localTime = DateTime.Now;

            if (_lastOnlineTimeCheck.HasValue)
            {
                if ((DateTime.Now - _lastOnlineTimeCheck.Value).TotalMinutes < 30)
                {
                    var adjustedTime = localTime + _onlineTimeOffset;
                    if (!silent)
                        Debug.WriteLine($"[TIME CHECK] Using local time with offset: {adjustedTime:yyyy-MM-dd HH:mm:ss}");
                    return adjustedTime;
                }
            }

            if (!silent)
                Debug.WriteLine($"[TIME CHECK] Using raw local time: {localTime:yyyy-MM-dd HH:mm:ss}");
            return localTime;
        }

        public static async Task<bool> IsWithinSchoolHoursAsync()
        {
            var now = await GetTrustedCurrentTimeAsync().ConfigureAwait(false);

            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            {
                Debug.WriteLine($"[SCHOOL HOURS] Weekend - outside school hours");
                return false;
            }

            var currentTime = now.TimeOfDay;

            if (now.DayOfWeek == DayOfWeek.Monday)
            {
                bool isWithinHours = currentTime >= TimeSpan.FromHours(9) &&
                                    currentTime <= TimeSpan.FromHours(15.25);
                Debug.WriteLine($"[SCHOOL HOURS] Monday {now:HH:mm} - {(isWithinHours ? "INSIDE" : "OUTSIDE")} school hours (9:00-15:15)");
                return isWithinHours;
            }

            bool isSchoolDay = currentTime >= new TimeSpan(8, 15, 0) &&
                              currentTime <= TimeSpan.FromHours(15.25);
            Debug.WriteLine($"[SCHOOL HOURS] {now.DayOfWeek} {now:HH:mm} - {(isSchoolDay ? "INSIDE" : "OUTSIDE")} school hours (8:15-15:15)");
            return isSchoolDay;
        }

        private static DateTime _lastCheckTime = DateTime.MinValue;
        private const int WAKE_DETECTION_THRESHOLD_MINUTES = 5;

        public static double NOTIFICATION_COOLDOWN_MINUTES { get; private set; }

        public static bool DetectWakeFromSleep()
        {
            if (_lastCheckTime == DateTime.MinValue)
            {
                _lastCheckTime = DateTime.Now;
                return false;
            }

            var timeSinceLastCheck = DateTime.Now - _lastCheckTime;
            _lastCheckTime = DateTime.Now;

            if (timeSinceLastCheck.TotalMinutes > WAKE_DETECTION_THRESHOLD_MINUTES)
            {
                Debug.WriteLine($"[WAKE DETECTION] Detected wake from sleep ({timeSinceLastCheck.TotalMinutes:F0} minutes passed)");
                return true;
            }

            return false;
        }

        public static async Task<(bool shouldStart, string reason, DateTime? nextStartTime, bool shouldNotify)> ShouldAutoStartAutomationAsync(bool silent = false)
        {
            try 
            {
                var now = await GetTrustedCurrentTimeAsync(silent).ConfigureAwait(false);
                
                if (!silent)
                    Debug.WriteLine($"=== AUTO-START CHECK at {now:yyyy-MM-dd HH:mm:ss} ===");

                var completionStatus = await GetCompletionStatusAsync().ConfigureAwait(false);
                var today = now.Date;

                if (completionStatus.HasValue && completionStatus.Value.Date == today)
                {
                    if (!silent)
                        Debug.WriteLine($"[AUTO-START] Already COMPLETED today ({completionStatus:yyyy-MM-dd HH:mm})");
                    
                    var nextDay = GetNextSchoolDay(now);
                    var nextStart = GetSchoolStartTime(nextDay);

                    string reason = $"Allerede fullført i dag kl. {completionStatus:HH:mm}";
                    bool shouldNotify = !_hasPerformedInitialCheck;

                    return (false, reason, nextStart, shouldNotify);
                }

                if (!await IsWithinSchoolHoursAsync().ConfigureAwait(false))
                {
                    var nextStart = GetNextSchoolStartTime(now);
                    if (nextStart.HasValue)
                    {
                        var timeUntil = nextStart.Value - now;
                        string waitMessage = timeUntil.TotalHours < 24
                            ? $"Starter automatisk {nextStart.Value:HH:mm} ({GetNorwegianDayName(nextStart.Value.DayOfWeek)})"
                            : $"Starter automatisk {GetNorwegianDayName(nextStart.Value.DayOfWeek)} {nextStart.Value:HH:mm}";

                        bool shouldNotify = !_hasPerformedInitialCheck || ShouldShowNotification(waitMessage);
                        
                        return (false, waitMessage, nextStart, shouldNotify);
                    }

                    string outsideReason = "Utenfor skoletid";
                    bool shouldNotifyOutside = !_hasPerformedInitialCheck;
                    
                    return (false, outsideReason, null, shouldNotifyOutside);
                }

                if (!silent)
                    Debug.WriteLine($"[AUTO-START] ✓ Should start - within school hours and not completed today");
                
                string startReason = $"Starter automatisering for {GetNorwegianDayName(now.DayOfWeek)} kl. {now:HH:mm}";

                UpdateLastNotification(startReason);
                _hasPerformedInitialCheck = true;

                return (true, startReason, null, true);
            }
            catch (Exception ex)
            {
                if (!silent)
                    Debug.WriteLine($"[AUTO-START] Error: {ex.Message}");
                return (false, $"Feil ved sjekk: {ex.Message}", null, false);
            }
        }

        private static bool ShouldShowNotification(string message)
        {
            if (message != _lastNotificationMessage)
            {
                var timeSinceLastNotification = DateTime.Now - _lastNotificationTime;
                if (timeSinceLastNotification.TotalMinutes >= 30)
                {
                    UpdateLastNotification(message);
                    return true;
                }
                return false;
            }

            return false;
        }

        private static void UpdateLastNotification(string message)
        {
            _lastNotificationTime = DateTime.Now;
            _lastNotificationMessage = message;
            _hasPerformedInitialCheck = true;
        }

        private static DateTime? GetNextSchoolStartTime(DateTime from)
        {
            var current = from.Date;

            if (from.TimeOfDay < TimeSpan.FromHours(15.25))
            {
                var todayStart = GetSchoolStartTime(current);
                if (todayStart > from)
                    return todayStart;
            }

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

        private static DateTime GetSchoolStartTime(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Monday)
                return date.Date.AddHours(9);
            else
                return date.Date.Add(new TimeSpan(8, 15, 0));
        }

        private static DateTime GetNextSchoolDay(DateTime from)
        {
            var nextDay = from.Date.AddDays(1);
            while (nextDay.DayOfWeek == DayOfWeek.Saturday || nextDay.DayOfWeek == DayOfWeek.Sunday)
            {
                nextDay = nextDay.AddDays(1);
            }
            return nextDay;
        }

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

        public static async Task MarkTodayAsStartedAsync()
        {
            try
            {
                var now = await GetTrustedCurrentTimeAsync().ConfigureAwait(false);
                var filePath = GetLastRunFilePath();
                var data = new { lastRun = now, started = true, completed = false };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

                Debug.WriteLine($"[AUTO-START] Marked {now:yyyy-MM-dd} as STARTED at {now:HH:mm}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error saving started status: {ex.Message}");
            }
        }

        public static async Task MarkTodayAsCompletedAsync()
        {
            try
            {
                var now = await GetTrustedCurrentTimeAsync().ConfigureAwait(false);
                var filePath = GetLastRunFilePath();
                var data = new { lastRun = now, started = true, completed = true };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

                Debug.WriteLine($"[AUTO-START] Marked {now:yyyy-MM-dd} as COMPLETED at {now:HH:mm}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error saving completion: {ex.Message}");
            }
        }

        private static async Task<DateTime?> GetCompletionStatusAsync()
        {
            try
            {
                var filePath = GetLastRunFilePath();
                if (!System.IO.File.Exists(filePath))
                    return null;

                var json = await System.IO.File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("completed", out var completedElement) &&
                    completedElement.GetBoolean() == true)
                {
                    if (data.TryGetProperty("lastRun", out var lastRunElement))
                    {
                        var completedDate = DateTime.Parse(lastRunElement.GetString()!);
                        Debug.WriteLine($"[AUTO-START] Found completion record: {completedDate:yyyy-MM-dd HH:mm}");
                        return completedDate;
                    }
                }
                else
                {
                    Debug.WriteLine("[AUTO-START] Found start record but not completed - app can restart");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-START] Error reading completion status: {ex.Message}");
            }
            return null;
        }

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
                
                _hasPerformedInitialCheck = false;
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