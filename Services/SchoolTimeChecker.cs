using System;
using System.IO;
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
        private static bool _hasPerformedInitialCheck = false;

        private static SchoolHoursSettings? _cachedSchoolHours = null;
        private static DateTime _lastSchoolHoursLoad = DateTime.MinValue;

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

            if (_lastOnlineTimeCheck.HasValue && (DateTime.Now - _lastOnlineTimeCheck.Value).TotalMinutes < 30)
            {
                var adjustedTime = localTime + _onlineTimeOffset;
                if (!silent)
                    Debug.WriteLine($"[TIME CHECK] Using local time with offset: {adjustedTime:yyyy-MM-dd HH:mm:ss}");
                return adjustedTime;
            }

            if (!silent)
                Debug.WriteLine($"[TIME CHECK] Using raw local time: {localTime:yyyy-MM-dd HH:mm:ss}");
            return localTime;
        }

        private static async Task<SchoolHoursSettings> LoadSchoolHoursAsync()
        {
            if (_cachedSchoolHours != null && (DateTime.Now - _lastSchoolHoursLoad).TotalMinutes < 5)
            {
                return _cachedSchoolHours;
            }

            try
            {
                var filePath = GetSchoolHoursFilePath();

                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _cachedSchoolHours = JsonSerializer.Deserialize<SchoolHoursSettings>(json) ?? SchoolHoursSettings.GetDefault();
                    _lastSchoolHoursLoad = DateTime.Now;
                    Debug.WriteLine("[SCHOOL HOURS] Loaded custom school hours from file");
                }
                else
                {
                    _cachedSchoolHours = SchoolHoursSettings.GetDefault();
                    _lastSchoolHoursLoad = DateTime.Now;
                    Debug.WriteLine("[SCHOOL HOURS] Using default school hours");
                }

                return _cachedSchoolHours;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SCHOOL HOURS] Error loading: {ex.Message}");
                return SchoolHoursSettings.GetDefault();
            }
        }

        public static async Task<bool> IsWithinSchoolHoursAsync()
        {
            var now = await GetTrustedCurrentTimeAsync().ConfigureAwait(false);
            var schoolHours = await LoadSchoolHoursAsync().ConfigureAwait(false);

            if (!schoolHours.IsDayEnabled(now.DayOfWeek))
            {
                Debug.WriteLine($"[SCHOOL HOURS] {now.DayOfWeek} is disabled - outside school hours");
                return false;
            }

            var (startTime, endTime) = schoolHours.GetDayTimes(now.DayOfWeek);
            var currentTime = now.TimeOfDay;

            bool isWithinHours = currentTime >= startTime && currentTime <= endTime;

            Debug.WriteLine($"[SCHOOL HOURS] {now.DayOfWeek} {now:HH:mm} - {(isWithinHours ? "INSIDE" : "OUTSIDE")} school hours ({startTime:hh\\:mm}-{endTime:hh\\:mm})");

            return isWithinHours;
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

                    var nextDay = await GetNextSchoolDayAsync(now);
                    var nextStart = await GetSchoolStartTimeAsync(nextDay);

                    string reason = $"Allerede fullført i dag kl. {completionStatus:HH:mm}";
                    bool shouldNotify = !_hasPerformedInitialCheck;

                    return (false, reason, nextStart, shouldNotify);
                }

                if (!await IsWithinSchoolHoursAsync().ConfigureAwait(false))
                {
                    var nextStart = await GetNextSchoolStartTimeAsync(now);
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
                    Debug.WriteLine($"[AUTO-START] Should start - within school hours and not completed today");

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

        private static async Task<DateTime?> GetNextSchoolStartTimeAsync(DateTime from)
        {
            var schoolHours = await LoadSchoolHoursAsync();
            var current = from.Date;

            if (from.TimeOfDay < (await GetSchoolEndTimeAsync(from.DayOfWeek)))
            {
                var todayStart = await GetSchoolStartTimeAsync(current);
                if (todayStart > from && schoolHours.IsDayEnabled(from.DayOfWeek))
                    return todayStart;
            }

            for (int i = 1; i <= 7; i++)
            {
                var checkDate = current.AddDays(i);
                if (schoolHours.IsDayEnabled(checkDate.DayOfWeek))
                {
                    return await GetSchoolStartTimeAsync(checkDate);
                }
            }

            return null;
        }

        private static async Task<DateTime> GetSchoolStartTimeAsync(DateTime date)
        {
            var schoolHours = await LoadSchoolHoursAsync();
            var (startTime, _) = schoolHours.GetDayTimes(date.DayOfWeek);
            return date.Date.Add(startTime);
        }

        private static async Task<TimeSpan> GetSchoolEndTimeAsync(DayOfWeek day)
        {
            var schoolHours = await LoadSchoolHoursAsync();
            var (_, endTime) = schoolHours.GetDayTimes(day);
            return endTime;
        }

        private static async Task<DateTime> GetNextSchoolDayAsync(DateTime from)
        {
            var schoolHours = await LoadSchoolHoursAsync();
            var nextDay = from.Date.AddDays(1);

            for (int i = 0; i < 7; i++)
            {
                if (schoolHours.IsDayEnabled(nextDay.DayOfWeek))
                {
                    return nextDay;
                }
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
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

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
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

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
                if (!File.Exists(filePath))
                    return null;

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
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
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
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
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "last_auto_run.json");
        }

        private static string GetSchoolHoursFilePath()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "school_hours.json");
        }

        public static void InvalidateSchoolHoursCache()
        {
            _cachedSchoolHours = null;
            _lastSchoolHoursLoad = DateTime.MinValue;
            Debug.WriteLine("[SCHOOL HOURS] Cache invalidated - will reload on next check");
        }
    }
}