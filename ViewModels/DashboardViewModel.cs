using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using AkademiTrack.Services;
using Avalonia.Threading;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly AttendanceDataService _attendanceService;
        private ILoggingService? _loggingService;

        private TodayScheduleData? _cachedTodaySchedule;
        private DateTime _cacheDate = DateTime.MinValue;


        // Today's STU sessions
        private string _todayDisplay = "0/0";
        private string _todayStatus = "Venter på data...";

        // Next class
        private string _nextClassName = "Ingen time";
        private string _nextClassTime = "--:-- - --:--";
        private string _nextClassRoom = "";
        private System.Timers.Timer? _nextClassUpdateTimer;


        // Monthly attendance
        private string _monthlyDisplay = "0/0";
        private string _monthlyStatus = "0% fremmøte";

        public string MonthlyDisplay
        {
            get => _monthlyDisplay;
            set
            {
                _monthlyDisplay = value;
                OnPropertyChanged(nameof(MonthlyDisplay));
            }
        }

        public string MonthlyStatus
        {
            get => _monthlyStatus;
            set
            {
                _monthlyStatus = value;
                OnPropertyChanged(nameof(MonthlyStatus));
            }
        }

        // Weekly attendance
        private string _weeklyPercentage = "0%";
        private string _weeklyDisplay = "0 av 0 økter registrert";
        private string _weeklyRemaining = "0 økter gjenstår";
        private List<DailyAttendance> _weeklyDays = InitializeEmptyWeek();

        public string WeeklyPercentage
        {
            get => _weeklyPercentage;
            set
            {
                _weeklyPercentage = value;
                OnPropertyChanged(nameof(WeeklyPercentage));
            }
        }

        public string WeeklyDisplay
        {
            get => _weeklyDisplay;
            set
            {
                _weeklyDisplay = value;
                OnPropertyChanged(nameof(WeeklyDisplay));
            }
        }

        public string WeeklyRemaining
        {
            get => _weeklyRemaining;
            set
            {
                _weeklyRemaining = value;
                OnPropertyChanged(nameof(WeeklyRemaining));
            }
        }

        public List<DailyAttendance> WeeklyDays
        {
            get => _weeklyDays;
            set
            {
                _weeklyDays = value;
                OnPropertyChanged(nameof(WeeklyDays));
            }
        }

        // Over/Undertid
        private string _overtimeValue = "0.0";
        private string _overtimeStatus = "Balansert";
        private string _overtimeColor = "orange";

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardViewModel()
        {
            _attendanceService = new AttendanceDataService();
            
            // Connect logging service for auto-retry functionality
            _loggingService = ServiceLocator.Instance.GetService<ILoggingService>();
            _attendanceService.SetLoggingService(_loggingService);
            
            _weeklyDays = InitializeEmptyWeek();
        }

        // Today's STU Sessions
        public string TodayDisplay
        {
            get => _todayDisplay;
            set
            {
                _todayDisplay = value;
                OnPropertyChanged(nameof(TodayDisplay));
            }
        }

        public string TodayStatus
        {
            get => _todayStatus;
            set
            {
                _todayStatus = value;
                OnPropertyChanged(nameof(TodayStatus));
            }
        }

        // Next Class
        public string NextClassName
        {
            get => _nextClassName;
            set
            {
                _nextClassName = value;
                OnPropertyChanged(nameof(NextClassName));
            }
        }

        public string NextClassTime
        {
            get => _nextClassTime;
            set
            {
                _nextClassTime = value;
                OnPropertyChanged(nameof(NextClassTime));
            }
        }

        public string NextClassRoom
        {
            get => _nextClassRoom;
            set
            {
                _nextClassRoom = value;
                OnPropertyChanged(nameof(NextClassRoom));
            }
        }

        // Over/Undertid (Overtime)
        public string OvertimeValue
        {
            get => _overtimeValue;
            set
            {
                _overtimeValue = value;
                OnPropertyChanged(nameof(OvertimeValue));
            }
        }

        public string OvertimeStatus
        {
            get => _overtimeStatus;
            set
            {
                _overtimeStatus = value;
                OnPropertyChanged(nameof(OvertimeStatus));
            }
        }

        public string OvertimeColor
        {
            get => _overtimeColor;
            set
            {
                _overtimeColor = value;
                OnPropertyChanged(nameof(OvertimeColor));
            }
        }

        public void SetCredentials(Services.UserParameters parameters, Dictionary<string, string> cookies)
        {
            _attendanceService.SetCredentials(parameters, cookies);
        }

        public bool IsCacheStale()
        {
            // Cache is stale if:
            // 1. No cached data exists
            // 2. Cache is from a different day (midnight has passed)

            if (_cachedTodaySchedule == null)
            {
                _loggingService?.LogDebug("[CACHE] No cached data - cache is stale");
                return true;
            }

            if (_cacheDate.Date != DateTime.Now.Date)
            {
                _loggingService?.LogDebug($"[CACHE] Cache is from different day ({_cacheDate.Date:yyyy-MM-dd} vs {DateTime.Now.Date:yyyy-MM-dd}) - cache is stale");
                return true;
            }

            _loggingService?.LogDebug($"[CACHE] Cache is fresh (from today {_cacheDate:HH:mm:ss})");
            return false;
        }

        public async Task RefreshDataAsync()
        {
            try
            {
                var summary = await _attendanceService.GetAttendanceSummaryAsync();
                if (summary != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateOvertimeDisplay(summary);
                    });
                }

                var todayData = await _attendanceService.GetTodayScheduleAsync();
                if (todayData != null)
                {
                    CacheTodaySchedule(todayData);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateTodayDisplay(todayData);
                        UpdateNextClassDisplay(todayData);
                    });
                }

                // Fetch monthly attendance
                var monthlyData = await _attendanceService.GetMonthlyAttendanceAsync();
                if (monthlyData != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateMonthlyDisplay(monthlyData);
                    });
                }

                // Fetch weekly attendance
                var weeklyData = await _attendanceService.GetWeeklyAttendanceAsync();
                if (weeklyData != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateWeeklyDisplay(weeklyData);
                    });
                }
            }
            catch (Exception)
            {
                // Log error if needed
            }
        }

        public void IncrementRegisteredSessionCount()
        {
            if (_cachedTodaySchedule != null && _cacheDate.Date == DateTime.Now.Date)
            {
                _cachedTodaySchedule.RegisteredStuSessions++;
                
                UpdateTodayDisplay(_cachedTodaySchedule);
                
                _loggingService?.LogDebug($"[CACHE] Incremented registered count to {_cachedTodaySchedule.RegisteredStuSessions}");
            }
            else
            {
                _loggingService?.LogWarning("[CACHE] Cache is stale - triggering full refresh");
                _ = RefreshDataAsync();
            }
        }

        public void UpdateNextClassFromCache()
        {
            if (_cachedTodaySchedule != null && _cacheDate.Date == DateTime.Now.Date)
            {
                var recalculatedData = RecalculateNextClass(_cachedTodaySchedule);
                UpdateNextClassDisplay(recalculatedData);

                ScheduleNextClassUpdate(recalculatedData);
            }
        }

        private void ScheduleNextClassUpdate(TodayScheduleData data)
        {
            try
            {
                _nextClassUpdateTimer?.Dispose();
                _nextClassUpdateTimer = null;

                if (data.NextClass == null)
                    return;

                var nextClassStartTime = ParseTimeString(data.NextClass.StartKl);
                if (!nextClassStartTime.HasValue)
                    return;

                var now = DateTime.Now.TimeOfDay;
                var timeUntilNextClass = nextClassStartTime.Value - now;

                if (timeUntilNextClass.TotalSeconds > 0)
                {
                    _nextClassUpdateTimer = new System.Timers.Timer(timeUntilNextClass.TotalMilliseconds);
                    _nextClassUpdateTimer.Elapsed += (s, e) =>
                    {
                        _loggingService?.LogInfo($"[NEXT CLASS] ⏰ Class started - updating to show next class");
                        UpdateNextClassFromCache();
                    };
                    _nextClassUpdateTimer.AutoReset = false;
                    _nextClassUpdateTimer.Start();

                    _loggingService?.LogDebug($"[NEXT CLASS] Will update in {timeUntilNextClass.TotalMinutes:F1} minutes");
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[NEXT CLASS] Error scheduling update: {ex.Message}");
            }
        }

        private TodayScheduleData RecalculateNextClass(TodayScheduleData cachedData)
        {
            if (cachedData.AllTodayItems == null || !cachedData.AllTodayItems.Any())
                return cachedData;

            var now = DateTime.Now.TimeOfDay;

            var currentClasses = cachedData.AllTodayItems
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

            Services.ScheduleItem? currentClass = currentClasses.FirstOrDefault()?.Item;

            var upcomingClasses = cachedData.AllTodayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeString(i.StartKl)
                })
                .Where(x => x.StartTime.HasValue && x.StartTime.Value > now)
                .OrderBy(x => x.StartTime)
                .ToList();

            Services.ScheduleItem? nextClass = upcomingClasses.FirstOrDefault()?.Item;

            return new TodayScheduleData
            {
                RegisteredStuSessions = cachedData.RegisteredStuSessions,
                TotalStuSessions = cachedData.TotalStuSessions,
                NextClass = nextClass,
                CurrentClass = currentClass,
                AllTodayItems = cachedData.AllTodayItems
            };
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

        private void CacheTodaySchedule(TodayScheduleData data)
        {
            _cachedTodaySchedule = data;
            _cacheDate = DateTime.Now;
        }

        private void UpdateTodayDisplay(TodayScheduleData data)
        {
            TodayDisplay = $"{data.RegisteredStuSessions}/{data.TotalStuSessions}";

            if (data.RegisteredStuSessions == data.TotalStuSessions && data.TotalStuSessions > 0)
            {
                TodayStatus = "✓ Alle registrert";
            }
            else if (data.RegisteredStuSessions == 0 && data.TotalStuSessions > 0)
            {
                TodayStatus = "Ingen registrert ennå";
            }
            else if (data.RegisteredStuSessions > 0)
            {
                int remaining = data.TotalStuSessions - data.RegisteredStuSessions;
                TodayStatus = $"{remaining} gjenstår";
            }
            else
            {
                TodayStatus = "Ingen STU-økter i dag";
            }
        }

        private void UpdateNextClassDisplay(TodayScheduleData data)
        {
            Services.ScheduleItem? displayClass = null;

            if (data.NextClass != null)
            {
                displayClass = data.NextClass;
            }

            if (displayClass != null)
            {
                NextClassName = GetSubjectDisplayName(displayClass);

                if (!string.IsNullOrEmpty(displayClass.StartKl) && !string.IsNullOrEmpty(displayClass.SluttKl))
                {
                    string startTime = FormatTime(displayClass.StartKl);
                    string endTime = FormatTime(displayClass.SluttKl);
                    NextClassTime = $"{startTime} - {endTime}";
                }
                else
                {
                    NextClassTime = "--:-- - --:--";
                }

                NextClassRoom = "";
            }
            else
            {
                NextClassName = "Ingen flere timer";
                NextClassTime = "--:-- - --:--";
                NextClassRoom = "";
            }
        }

        private bool DoClassesOverlap(Services.ScheduleItem class1, Services.ScheduleItem class2)
        {
            try
            {
                if (!TimeSpan.TryParse(class1.StartKl, out var start1) ||
                    !TimeSpan.TryParse(class1.SluttKl, out var end1) ||
                    !TimeSpan.TryParse(class2.StartKl, out var start2) ||
                    !TimeSpan.TryParse(class2.SluttKl, out var end2))
                {
                    return false;
                }

                return start1 < end2 && start2 < end1;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateMonthlyDisplay(MonthlyAttendanceData data)
        {
            MonthlyDisplay = $"{data.RegisteredSessions}/{data.TotalSessions}";
            MonthlyStatus = $"{data.AttendancePercentage:F1}% fremmøte";
        }

        private void UpdateWeeklyDisplay(WeeklyAttendanceData data)
        {
            WeeklyPercentage = $"{data.WeeklyPercentage:F0}%";
            WeeklyDisplay = $"{data.TotalRegistered} av {data.TotalSessions} økter registrert";

            int remaining = data.TotalSessions - data.TotalRegistered;
            WeeklyRemaining = remaining > 0 ? $"{remaining} økter gjenstår" : "Alle økter fullført!";

            WeeklyDays = data.DailyAttendance ?? new List<DailyAttendance>();
        }

        private string GetSubjectDisplayName(Services.ScheduleItem item)
        {
            // Map short codes to full names
            var subjectMap = new Dictionary<string, string>
            {
                { "NOR", "Norsk" },
                { "MAT", "Matematikk" },
                { "ENG", "Engelsk" },
                { "NAT", "Naturfag" },
                { "SAM", "Samfunnsfag" },
                { "HIS", "Historie" },
                { "KRO", "Kroppsøving" },
                { "MUS", "Musikk" },
                { "KHV", "Kunst og håndverk" },
                { "MAH", "Mat og helse" },
                { "STU", "Studietid" },
                { "2PY", "Matematikk 2P-Y" },
                { "MR1", "Matematikk R1" },
                { "MOM", "Morgenmøte" },
                { "FDA", "Fagdag" },
                { "FDS", "Fagdag Studietid" }
            };

            if (item.KNavn != null && subjectMap.ContainsKey(item.KNavn))
            {
                return subjectMap[item.KNavn];
            }

            return item.KNavn ?? "Ukjent fag";
        }

        private string FormatTime(string time)
        {
            if (string.IsNullOrEmpty(time) || time.Length != 4)
                return time;

            return $"{time.Substring(0, 2)}:{time.Substring(2, 2)}";
        }

        private void UpdateOvertimeDisplay(AttendanceSummary summary)
        {
            double saldo = summary.Saldo;

            OvertimeValue = saldo >= 0 ? $"+{saldo:F1}" : $"{saldo:F1}";

            if (saldo > 0)
            {
                OvertimeStatus = "Du er over målet! ✓";
                OvertimeColor = "green";
            }
            else if (saldo < 0)
            {
                OvertimeStatus = "Du er under grensen!";
                OvertimeColor = "orange";
            }
            else
            {
                OvertimeStatus = "Balansert";
                OvertimeColor = "blue";
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ClearCache()
        {
            _cachedTodaySchedule = null;
            _cacheDate = DateTime.MinValue;
            _loggingService?.LogDebug("[CACHE] Cache cleared");
        }

        public void Dispose()
        {
            _nextClassUpdateTimer?.Dispose();
            _attendanceService?.Dispose();
        }

        private static List<DailyAttendance> InitializeEmptyWeek()
        {
            var today = DateTime.Now;
            var dayOfWeek = (int)today.DayOfWeek;
            var daysUntilMonday = dayOfWeek == 0 ? -6 : -(dayOfWeek - 1);
            var monday = today.AddDays(daysUntilMonday);

            var emptyWeek = new List<DailyAttendance>();
            for (int i = 0; i < 5; i++)
            {
                emptyWeek.Add(new DailyAttendance
                {
                    DayOfWeek = monday.AddDays(i).DayOfWeek,
                    Date = monday.AddDays(i),
                    RegisteredSessions = 0,
                    TotalSessions = 0,
                    FillPercentage = 0
                });
            }
            return emptyWeek;
        }
    }
    
}