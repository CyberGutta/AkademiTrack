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
        private bool _isRefreshing = false; // Concurrency guard


        // Today's STU sessions
        private string _todayDisplay = "0/0";
        private string _todayStatus = "Venter på data...";

        // Next class
        private string _nextClassName = "Ingen time";
        private string _nextClassTime = "--:-- - --:--";
        private string _nextClassRoom = "";
        private System.Timers.Timer? _nextClassUpdateTimer;
        private bool _showCurrentClass = false; // Toggle between next and current class
        private DateTime _lastSystemTime = DateTime.Now; // For sleep detection


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
            
            // Set up wake-from-sleep detection
            SetupSleepDetection();
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

        // Toggle between next and current class
        public bool ShowCurrentClass
        {
            get => _showCurrentClass;
            set
            {
                _showCurrentClass = value;
                OnPropertyChanged(nameof(ShowCurrentClass));
                OnPropertyChanged(nameof(ClassToggleLabel));
                RefreshClassDisplay();
            }
        }

        public string ClassToggleLabel => _showCurrentClass ? "Neste" : "Nå";

        public void ToggleClassView()
        {
            ShowCurrentClass = !ShowCurrentClass;
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
            // Prevent concurrent refreshes
            if (_isRefreshing)
            {
                _loggingService?.LogDebug("[DASHBOARD] Refresh already in progress - skipping duplicate call");
                return;
            }

            try
            {
                _isRefreshing = true;
                _loggingService?.LogDebug("[DASHBOARD] Starting data refresh...");

                // Add timeout to each service call
                var summaryTask = _attendanceService.GetAttendanceSummaryAsync();
                var summaryTimeout = Task.Delay(TimeSpan.FromSeconds(15));
                var summaryCompleted = await Task.WhenAny(summaryTask, summaryTimeout);
                
                if (summaryCompleted == summaryTask)
                {
                    var summary = await summaryTask;
                    if (summary != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            UpdateOvertimeDisplay(summary);
                        });
                        _loggingService?.LogDebug("[DASHBOARD] ✓ Overtime data loaded");
                    }
                }
                else
                {
                    _loggingService?.LogWarning("[DASHBOARD] Overtime data fetch timed out after 15 seconds");
                }

                // Today's schedule with timeout
                var todayTask = _attendanceService.GetTodayScheduleAsync();
                var todayTimeout = Task.Delay(TimeSpan.FromSeconds(15));
                var todayCompleted = await Task.WhenAny(todayTask, todayTimeout);
                
                if (todayCompleted == todayTask)
                {
                    var todayData = await todayTask;
                    if (todayData != null)
                    {
                        CacheTodaySchedule(todayData);
                        
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            UpdateTodayDisplay(todayData);
                            UpdateNextClassDisplay(todayData);
                            ScheduleNextClassUpdate(todayData);
                        });
                        _loggingService?.LogDebug("[DASHBOARD] Today's schedule loaded");
                    }
                }
                else
                {
                    _loggingService?.LogWarning("[DASHBOARD] Today's schedule fetch timed out after 15 seconds");
                }

                // Monthly attendance with timeout
                var monthlyTask = _attendanceService.GetMonthlyAttendanceAsync();
                var monthlyTimeout = Task.Delay(TimeSpan.FromSeconds(15));
                var monthlyCompleted = await Task.WhenAny(monthlyTask, monthlyTimeout);
                
                if (monthlyCompleted == monthlyTask)
                {
                    var monthlyData = await monthlyTask;
                    if (monthlyData != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            UpdateMonthlyDisplay(monthlyData);
                        });
                        _loggingService?.LogDebug("[DASHBOARD] ✓ Monthly data loaded");
                    }
                }
                else
                {
                    _loggingService?.LogWarning("[DASHBOARD] Monthly data fetch timed out after 15 seconds");
                }

                // Weekly attendance with timeout
                var weeklyTask = _attendanceService.GetWeeklyAttendanceAsync();
                var weeklyTimeout = Task.Delay(TimeSpan.FromSeconds(15));
                var weeklyCompleted = await Task.WhenAny(weeklyTask, weeklyTimeout);
                
                if (weeklyCompleted == weeklyTask)
                {
                    var weeklyData = await weeklyTask;
                    if (weeklyData != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            UpdateWeeklyDisplay(weeklyData);
                        });
                        _loggingService?.LogDebug("[DASHBOARD] ✓ Weekly data loaded");
                    }
                }
                else
                {
                    _loggingService?.LogWarning("[DASHBOARD] Weekly data fetch timed out after 15 seconds");
                }

                _loggingService?.LogSuccess("[DASHBOARD] ✓ Data refresh complete!");
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[DASHBOARD] Error refreshing data: {ex.Message}");
                _loggingService?.LogDebug($"[DASHBOARD] Stack trace: {ex.StackTrace}");
                
                // Re-throw so the caller knows it failed
                throw;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public void IncrementRegisteredSessionCount()
        {
            if (_cachedTodaySchedule != null && _cacheDate.Date == DateTime.Now.Date)
            {
                _cachedTodaySchedule.RegisteredStuSessions++;
                
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateTodayDisplay(_cachedTodaySchedule);
                });
                
                _loggingService?.LogDebug($"[CACHE] ✓ Incremented registered count to {_cachedTodaySchedule.RegisteredStuSessions}/{_cachedTodaySchedule.TotalStuSessions}");
            }
            else
            {
                _loggingService?.LogWarning("[CACHE] Cache is stale - cannot increment from cache");
                _ = RefreshDataAsync();
            }
        }


        public void UpdateNextClassFromCache()
        {
            // Check if cache is from today
            if (_cachedTodaySchedule != null && _cacheDate.Date == DateTime.Now.Date)
            {
                var recalculatedData = RecalculateNextClass(_cachedTodaySchedule);
                UpdateNextClassDisplay(recalculatedData);

                ScheduleNextClassUpdate(recalculatedData);
            }
            else
            {
                // Cache is stale (different day or no cache) - refresh from server
                _loggingService?.LogWarning($"[CACHE] Cache is stale - triggering full refresh (cache date: {_cacheDate.Date:yyyy-MM-dd}, today: {DateTime.Now.Date:yyyy-MM-dd})");
                _ = RefreshDataAsync();
            }
        }

        private void ScheduleNextClassUpdate(TodayScheduleData data)
        {
            try
            {
                _nextClassUpdateTimer?.Dispose();
                _nextClassUpdateTimer = null;

                var now = DateTime.Now.TimeOfDay;
                TimeSpan? nextEventTime = null;
                string eventDescription = "";

                // Check for current class ending
                if (data.CurrentClass != null && !string.IsNullOrEmpty(data.CurrentClass.SluttKl))
                {
                    var currentClassEndTime = ParseTimeString(data.CurrentClass.SluttKl);
                    if (currentClassEndTime.HasValue && currentClassEndTime.Value > now)
                    {
                        nextEventTime = currentClassEndTime.Value;
                        eventDescription = $"current class ends at {FormatTime(data.CurrentClass.SluttKl)}";
                    }
                }

                // Check for next class starting
                if (data.NextClass != null && !string.IsNullOrEmpty(data.NextClass.StartKl))
                {
                    var nextClassStartTime = ParseTimeString(data.NextClass.StartKl);
                    if (nextClassStartTime.HasValue && nextClassStartTime.Value > now)
                    {
                        if (!nextEventTime.HasValue || nextClassStartTime.Value < nextEventTime.Value)
                        {
                            nextEventTime = nextClassStartTime.Value;
                            eventDescription = $"next class starts at {FormatTime(data.NextClass.StartKl)}";
                        }
                    }
                }

                // If no upcoming events today, schedule check for midnight (new day)
                if (!nextEventTime.HasValue)
                {
                    var midnight = TimeSpan.FromDays(1); // Tomorrow at 00:00
                    var timeUntilMidnight = midnight - now;
                    
                    _loggingService?.LogDebug($"[NEXT CLASS] No more classes today - will check at midnight in {timeUntilMidnight.TotalHours:F1} hours");

                    _nextClassUpdateTimer = new System.Timers.Timer(timeUntilMidnight.TotalMilliseconds);
                    _nextClassUpdateTimer.Elapsed += (s, e) =>
                    {
                        _loggingService?.LogInfo("[NEXT CLASS] 🌅 Midnight reached - refreshing for new day");
                        Dispatcher.UIThread.Post(async () =>
                        {
                            await RefreshDataAsync();
                        });
                    };
                    _nextClassUpdateTimer.AutoReset = false;
                    _nextClassUpdateTimer.Start();
                    return;
                }

                // Schedule timer for the next event
                var timeUntilEvent = nextEventTime.Value - now;

                if (timeUntilEvent.TotalSeconds <= 0)
                {
                    // Event time has passed, recalculate immediately
                    _loggingService?.LogDebug("[NEXT CLASS] Event time has passed - recalculating immediately");
                    Dispatcher.UIThread.Post(() => UpdateNextClassFromCache());
                    return;
                }

                _nextClassUpdateTimer = new System.Timers.Timer(timeUntilEvent.TotalMilliseconds);
                _nextClassUpdateTimer.Elapsed += (s, e) =>
                {
                    _loggingService?.LogInfo($"[NEXT CLASS] Event triggered: {eventDescription}");
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateNextClassFromCache(); // Recalculates and reschedules for next event
                    });
                };
                _nextClassUpdateTimer.AutoReset = false;
                _nextClassUpdateTimer.Start();

                _loggingService?.LogDebug($"[NEXT CLASS] Timer set - will update in {timeUntilEvent.TotalMinutes:F1} minutes ({eventDescription})");
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[NEXT CLASS] Error scheduling update: {ex.Message}");
            }
        }

        private void SetupSleepDetection()
        {
            try
            {
                // Set up a timer to check for system sleep/wake every 30 seconds
                var sleepDetectionTimer = new System.Timers.Timer(30000); // 30 seconds
                sleepDetectionTimer.Elapsed += (s, e) =>
                {
                    var currentTime = DateTime.Now;
                    var timeDifference = currentTime - _lastSystemTime;
                    
                    // If more than 2 minutes have passed since last check, assume system was sleeping
                    if (timeDifference.TotalMinutes > 2)
                    {
                        _loggingService?.LogInfo($"[SLEEP DETECTION] 💤 System wake detected - time gap: {timeDifference.TotalMinutes:F1} minutes");
                        
                        Dispatcher.UIThread.Post(async () =>
                        {
                            _loggingService?.LogInfo("[SLEEP DETECTION] 🔄 Refreshing data after wake from sleep");
                            await RefreshDataAsync();
                        });
                    }
                    
                    _lastSystemTime = currentTime;
                };
                sleepDetectionTimer.AutoReset = true;
                sleepDetectionTimer.Start();
                
                _loggingService?.LogDebug("[SLEEP DETECTION] ✅ Sleep detection timer started");
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[SLEEP DETECTION] Error setting up sleep detection: {ex.Message}");
            }
        }

        private TodayScheduleData RecalculateNextClass(TodayScheduleData cachedData)
        {
            if (cachedData.AllTodayItems == null || !cachedData.AllTodayItems.Any())
                return cachedData;

            var now = DateTime.Now;

            var currentClasses = cachedData.AllTodayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl) && !string.IsNullOrEmpty(i.SluttKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeToDateTime(i.StartKl, i.Dato),
                    EndTime = ParseTimeToDateTime(i.SluttKl, i.Dato)
                })
                .Where(x => x.StartTime.HasValue && x.EndTime.HasValue &&
                           x.StartTime.Value <= now && x.EndTime.Value >= now)
                .OrderBy(x => x.StartTime)
                .ToList();

            Services.ScheduleItem? currentClass = currentClasses.FirstOrDefault()?.Item;

            // For next class, we need to check if we have data beyond today
            // Since RecalculateNextClass works with cached data, it might not have tomorrow's data
            // The next class calculation should be done in the main data fetch
            Services.ScheduleItem? nextClass = cachedData.NextClass; // Keep the existing next class from the original fetch

            return new TodayScheduleData
            {
                RegisteredStuSessions = cachedData.RegisteredStuSessions,
                TotalStuSessions = cachedData.TotalStuSessions,
                NextClass = nextClass,
                CurrentClass = currentClass,
                AllTodayItems = cachedData.AllTodayItems
            };
        }

        private DateTime? ParseTimeToDateTime(string? timeStr, string? dateStr)
        {
            if (string.IsNullOrEmpty(timeStr) || string.IsNullOrEmpty(dateStr))
                return null;

            try
            {
                // Parse date from "yyyyMMdd" format
                if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    var timeSpan = ParseTimeString(timeStr);
                    if (timeSpan.HasValue)
                    {
                        return date.Add(timeSpan.Value);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private TimeSpan? ParseTimeString(string? timeStr)
        {
            if (string.IsNullOrEmpty(timeStr))
                return null;

            try
            {
                // Handle both old format "HHmm" and new format "HH:mm"
                if (timeStr.Length == 4 && timeStr.All(char.IsDigit))
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
                }
                else if (TimeSpan.TryParse(timeStr, out var timeSpan))
                {
                    // Handle "HH:mm" format
                    return timeSpan;
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
            _cachedTodaySchedule = data; // Store data for toggle functionality
            RefreshClassDisplay();
        }

        private void RefreshClassDisplay()
        {
            if (_cachedTodaySchedule == null) return;

            Services.ScheduleItem? displayClass = null;

            if (_showCurrentClass)
            {
                // Show current class
                displayClass = _cachedTodaySchedule.CurrentClass;
            }
            else
            {
                // Show next class
                displayClass = _cachedTodaySchedule.NextClass;
            }

            if (displayClass != null)
            {
                NextClassName = GetSubjectDisplayName(displayClass);

                if (!string.IsNullOrEmpty(displayClass.StartKl) && !string.IsNullOrEmpty(displayClass.SluttKl))
                {
                    string startTime = FormatTime(displayClass.StartKl);
                    string endTime = FormatTime(displayClass.SluttKl);
                    
                    // Check if the class is tomorrow
                    var today = DateTime.Now.ToString("yyyyMMdd");
                    if (displayClass.Dato != today)
                    {
                        // Class is tomorrow, add day indicator
                        NextClassTime = $"I morgen {startTime} - {endTime}";
                    }
                    else
                    {
                        NextClassTime = $"{startTime} - {endTime}";
                    }
                }
                else
                {
                    NextClassTime = "--:-- - --:--";
                }

                // Show room information if available
                NextClassRoom = !string.IsNullOrEmpty(displayClass.Romnr) ? $"Rom {displayClass.Romnr.Trim()}" : "";
            }
            else
            {
                if (_showCurrentClass)
                {
                    NextClassName = "Ingen pågående time";
                }
                else
                {
                    NextClassName = "Ingen flere timer";
                }
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

            // DEBUG: Log what we're receiving
            _loggingService?.LogInfo($"[DASHBOARD WEEKLY UPDATE] Total: {data.TotalSessions}, Registered: {data.TotalRegistered}");
            
            if (data.DailyAttendance != null)
            {
                foreach (var day in data.DailyAttendance)
                {
                    _loggingService?.LogInfo($"[DASHBOARD] {day.DayOfWeek}: {day.RegisteredSessions}/{day.TotalSessions}");
                }
            }

            WeeklyDays = data.DailyAttendance ?? new List<DailyAttendance>();
        }

        private string GetSubjectDisplayName(Services.ScheduleItem item)
        {
            // Use the DisplayName from the API if available (contains the full subject name)
            if (!string.IsNullOrEmpty(item.DisplayName))
            {
                return item.DisplayName;
            }

            // Fallback to manual mapping if DisplayName is not available (for backward compatibility)
            var subjectMap = new Dictionary<string, string>
            {
                // Core subjects
                { "NOR", "Norsk" },
                { "MAT", "Matematikk" },
                { "ENG", "Engelsk" },
                { "NAT", "Naturfag" },
                { "SAM", "Samfunnsfag" },
                { "HIS", "Historie" },
                { "GEO", "Geografi" },
                { "REL", "Religion og etikk" },
                { "KRO", "Kroppsøving" },
                { "MUS", "Musikk" },
                { "KHV", "Kunst og håndverk" },
                { "MAH", "Mat og helse" },
        
                // Math variants
                { "2PY", "Matematikk 2P-Y" },
                { "MR1", "Matematikk R1" },
                { "MR2", "Matematikk R2" },
                { "MS1", "Matematikk S1" },
                { "MS2", "Matematikk S2" },
                { "M1P", "Matematikk 1P" },
                { "M2P", "Matematikk 2P" },
                { "M1T", "Matematikk 1T" },
                { "M2T", "Matematikk 2T" },
        
                // Science variants
                { "FYS", "Fysikk" },
                { "FY1", "Fysikk 1" },
                { "FY2", "Fysikk 2" },
                { "KJE", "Kjemi" },
                { "KJ1", "Kjemi 1" },
                { "KJ2", "Kjemi 2" },
                { "BIO", "Biologi" },
                { "BI1", "Biologi 1" },
                { "BI2", "Biologi 2" },
        
                // Languages
                { "TYS", "Tysk" },
                { "FRA", "Fransk" },
                { "SPA", "Spansk" },
        
                // IT/Tech
                { "INF", "Informatikk" },
                { "IKT", "IKT" },
                { "PRO", "Programmering" },
        
                // Economy/Business
                { "OKO", "Økonomi" },
                { "BED", "Bedriftsøkonomi" },
                { "MAR", "Markedsføring" },
        
                // Other
                { "STU", "Studietid" },
                { "MOM", "Morgenmøte" },
                { "FDA", "Fagdag" },
                { "FDS", "Fagdag Studietid" },
                { "SOS", "Sosiologi" },
                { "PSY", "Psykologi" },
                { "POL", "Politikk" }
            };

            if (item.KNavn != null && subjectMap.ContainsKey(item.KNavn))
            {
                return subjectMap[item.KNavn];
            }

            return item.KNavn ?? "Ingen time";
        }

        private string FormatTime(string time)
        {
            if (string.IsNullOrEmpty(time))
                return time;

            // Handle old format "HHmm" (e.g., "0815")
            if (time.Length == 4 && time.All(char.IsDigit))
            {
                return $"{time.Substring(0, 2)}:{time.Substring(2, 2)}";
            }
            
            // Handle new format "HH:mm" (already formatted)
            if (time.Contains(':'))
            {
                return time;
            }
            
            // Fallback - return as-is
            return time;
        }

        private void UpdateOvertimeDisplay(AttendanceSummary summary)
        {
            double saldo = summary.Saldo;

            // Use exact formatting without rounding - show up to 2 decimals but remove trailing zeros
            string formattedSaldo = saldo.ToString("0.##");
            OvertimeValue = saldo >= 0 ? $"+{formattedSaldo}" : formattedSaldo;

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