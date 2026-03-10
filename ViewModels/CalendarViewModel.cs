using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Services;
using AkademiTrack.Services.Interfaces;
using Avalonia.Threading;
using System.Globalization;

namespace AkademiTrack.ViewModels
{
    public class CalendarViewModel : INotifyPropertyChanged
    {
        private readonly AttendanceDataService _attendanceService;
        private readonly ILoggingService? _loggingService;

        private string _selectedView = "Uke"; // Måned, Uke, Dag
        private DateTime _currentDate = DateTime.Now;
        private List<CalendarDayData> _calendarDays = new();
        private bool _isLoading = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Commands
        public ICommand? SetViewCommand { get; set; }
        public ICommand? NavigatePreviousCommand { get; set; }
        public ICommand? NavigateNextCommand { get; set; }
        public ICommand? NavigateTodayCommand { get; set; }

        public CalendarViewModel(AttendanceDataService attendanceService, ILoggingService? loggingService)
        {
            _attendanceService = attendanceService;
            _loggingService = loggingService;
        }

        public string SelectedView
        {
            get => _selectedView;
            set
            {
                if (_selectedView != value)
                {
                    _selectedView = value;
                    OnPropertyChanged(nameof(SelectedView));
                    OnPropertyChanged(nameof(IsMonthView));
                    OnPropertyChanged(nameof(IsWeekView));
                    OnPropertyChanged(nameof(IsDayView));
                    OnPropertyChanged(nameof(DisplayDateRange)); // Update the date display when view changes
                    _ = LoadCalendarDataAsync();
                }
            }
        }

        public bool IsMonthView => _selectedView == "Måned";
        public bool IsWeekView => _selectedView == "Uke";
        public bool IsDayView => _selectedView == "Dag";

        public DateTime CurrentDate
        {
            get => _currentDate;
            set
            {
                _currentDate = value;
                OnPropertyChanged(nameof(CurrentDate));
                OnPropertyChanged(nameof(DisplayDateRange));
            }
        }

        public string DisplayDateRange
        {
            get
            {
                var culture = new CultureInfo("nb-NO");
                if (IsWeekView)
                {
                    var monday = GetMonday(_currentDate);
                    var friday = monday.AddDays(4);
                    return $"Uke {GetWeekNumber(_currentDate)} ({monday:dd.MMM} - {friday:dd.MMM})";
                }
                else if (IsDayView)
                {
                    return _currentDate.ToString("dddd dd. MMMM yyyy", culture);
                }
                else // Month
                {
                    return _currentDate.ToString("MMMM yyyy", culture);
                }
            }
        }

        public List<CalendarDayData> CalendarDays
        {
            get => _calendarDays;
            set
            {
                _calendarDays = value;
                OnPropertyChanged(nameof(CalendarDays));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public async Task LoadCalendarDataAsync()
        {
            try
            {
                IsLoading = true;
                _loggingService?.LogInfo($"[CALENDAR] Loading {_selectedView} view for {_currentDate:yyyy-MM-dd}");
                List<ScheduleItem>? scheduleItems = null;

                if (IsWeekView)
                {
                    var monday = GetMonday(_currentDate);
                    var sunday = monday.AddDays(6);
                    _loggingService?.LogInfo($"[CALENDAR] Fetching week data from {monday:yyyy-MM-dd} to {sunday:yyyy-MM-dd}");
                    scheduleItems = await _attendanceService.GetScheduleRangeAsync(monday, sunday);
                    _loggingService?.LogInfo($"[CALENDAR] Received {scheduleItems?.Count ?? 0} items for week");
                }
                else if (IsDayView)
                {
                    _loggingService?.LogInfo($"[CALENDAR] Fetching day data for {_currentDate:yyyy-MM-dd}");
                    scheduleItems = await _attendanceService.GetScheduleRangeAsync(_currentDate, _currentDate);
                    _loggingService?.LogInfo($"[CALENDAR] Received {scheduleItems?.Count ?? 0} items for day");
                }
                else // Month
                {
                    var (startDate, endDate) = GetMonthRange();
                    _loggingService?.LogInfo($"[CALENDAR] Fetching month data from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
                    scheduleItems = await _attendanceService.GetScheduleRangeAsync(startDate, endDate);
                    _loggingService?.LogInfo($"[CALENDAR] Received {scheduleItems?.Count ?? 0} items for month");
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProcessScheduleData(scheduleItems);
                    _loggingService?.LogInfo($"[CALENDAR] Processed into {CalendarDays.Count} days");
                });
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[CALENDAR] Error loading calendar data: {ex.Message}");
                _loggingService?.LogError($"[CALENDAR] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private (DateTime startDate, DateTime endDate) GetMonthRange()
        {
            var firstDayOfMonth = new DateTime(_currentDate.Year, _currentDate.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            
            // For month view, we need to include days from previous/next month to fill the grid
            // Start from the Monday of the week containing the first day of the month
            var startOfWeek = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek + 1);
            if (startOfWeek > firstDayOfMonth) startOfWeek = startOfWeek.AddDays(-7);
            
            // End at the Sunday of the week containing the last day of the month
            var endOfWeek = lastDayOfMonth.AddDays(7 - (int)lastDayOfMonth.DayOfWeek);
            if (endOfWeek < lastDayOfMonth) endOfWeek = endOfWeek.AddDays(7);
            
            // Ensure we have complete weeks (42 days = 6 weeks)
            var totalDays = (endOfWeek - startOfWeek).Days + 1;
            if (totalDays < 42)
            {
                endOfWeek = startOfWeek.AddDays(41); // 42 days total (6 weeks)
            }
            
            return (startOfWeek, endOfWeek);
        }

        private void ProcessScheduleData(List<ScheduleItem>? items)
        {
            _loggingService?.LogInfo($"[CALENDAR] Processing {items?.Count ?? 0} schedule items");
            var calendarDays = new List<CalendarDayData>();

            if (IsWeekView)
            {
                // For week view, always show Monday-Friday
                var monday = GetMonday(_currentDate);
                for (int i = 0; i < 5; i++) // Monday to Friday
                {
                    var date = monday.AddDays(i);
                    var dateStr = date.ToString("yyyyMMdd");
                    
                    // Find sessions for this date
                    var sessions = items?
                        .Where(item => item.Dato == dateStr)
                        .OrderBy(s => s.StartKl)
                        .ToList() ?? new List<ScheduleItem>();
                    
                    // Calculate absolute position for each session from 08:00
                    var sessionsWithSpacing = new List<ScheduleItemWithSpacing>();
                    var startOfDay = new TimeSpan(8, 0, 0);
                    
                    foreach (var session in sessions)
                    {
                        var startTime = ParseTime(session.StartKl);
                        var topMargin = (startTime - startOfDay).TotalMinutes;
                        
                        // Calculate which hour slot (row) this session belongs to
                        // 08:00-08:59 = row 0, 09:00-09:59 = row 1, etc.
                        var hourSlot = startTime.Hours - 8; // 08:00 = 0, 09:00 = 1, etc.
                        if (hourSlot < 0) hourSlot = 0;
                        if (hourSlot > 7) hourSlot = 7;
                        
                        _loggingService?.LogDebug($"[CALENDAR] Session: {session.DisplayName}, StartKl: {session.StartKl}, ParsedTime: {startTime}, Hours: {startTime.Hours}, HourSlot: {hourSlot}");
                        
                        sessionsWithSpacing.Add(new ScheduleItemWithSpacing
                        {
                            Item = session,
                            TopMargin = topMargin, // Absolute position from 08:00
                            HourSlot = hourSlot
                        });
                    }
                    
                    _loggingService?.LogInfo($"[CALENDAR] Date {date:yyyy-MM-dd} ({date.DayOfWeek}): {sessions.Count} sessions");
                    
                    calendarDays.Add(new CalendarDayData
                    {
                        Date = date,
                        Sessions = sessionsWithSpacing
                    });
                }
            }
            else if (IsDayView)
            {
                // For day view, show just the current day
                var dateStr = _currentDate.ToString("yyyyMMdd");
                var sessions = items?
                    .Where(item => item.Dato == dateStr)
                    .OrderBy(s => s.StartKl)
                    .ToList() ?? new List<ScheduleItem>();
                
                var sessionsWithSpacing = new List<ScheduleItemWithSpacing>();
                var startOfDay = new TimeSpan(8, 0, 0);
                
                foreach (var session in sessions)
                {
                    var startTime = ParseTime(session.StartKl);
                    var topMargin = (startTime - startOfDay).TotalMinutes;
                    var hourSlot = startTime.Hours - 8;
                    if (hourSlot < 0) hourSlot = 0;
                    if (hourSlot > 7) hourSlot = 7;
                    
                    sessionsWithSpacing.Add(new ScheduleItemWithSpacing
                    {
                        Item = session,
                        TopMargin = topMargin,
                        HourSlot = hourSlot
                    });
                }
                
                _loggingService?.LogInfo($"[CALENDAR] Date {_currentDate:yyyy-MM-dd}: {sessions.Count} sessions");
                
                calendarDays.Add(new CalendarDayData
                {
                    Date = _currentDate,
                    Sessions = sessionsWithSpacing
                });
            }
            else // Month view
            {
                // For month view, create calendar grid with all days
                var (startDate, endDate) = GetMonthRange();
                var current = startDate;
                
                while (current <= endDate)
                {
                    var dateStr = current.ToString("yyyyMMdd");
                    var sessions = items?
                        .Where(item => item.Dato == dateStr)
                        .OrderBy(s => s.StartKl)
                        .ToList() ?? new List<ScheduleItem>();
                    
                    var sessionsWithSpacing = sessions.Select(s => new ScheduleItemWithSpacing
                    {
                        Item = s,
                        TopMargin = 2
                    }).ToList();
                    
                    calendarDays.Add(new CalendarDayData
                    {
                        Date = current,
                        Sessions = sessionsWithSpacing
                    });
                    
                    current = current.AddDays(1);
                }
            }

            CalendarDays = calendarDays;
            _loggingService?.LogInfo($"[CALENDAR] Created {calendarDays.Count} calendar days");
        }

        private TimeSpan ParseTime(string? timeStr)
        {
            if (string.IsNullOrEmpty(timeStr)) return TimeSpan.Zero;
            
            if (timeStr.Contains(':'))
            {
                if (TimeSpan.TryParse(timeStr, out var time))
                    return time;
            }
            else if (timeStr.Length == 4)
            {
                if (int.TryParse(timeStr.Substring(0, 2), out var hours) &&
                    int.TryParse(timeStr.Substring(2, 2), out var minutes))
                {
                    return new TimeSpan(hours, minutes, 0);
                }
            }
            
            return TimeSpan.Zero;
        }

        private DateTime ParseDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now;
            
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, DateTimeStyles.None, out var date))
            {
                return date;
            }
            return DateTime.Now;
        }

        private DateTime GetMonday(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            var daysUntilMonday = dayOfWeek == 0 ? -6 : -(dayOfWeek - 1);
            return date.AddDays(daysUntilMonday);
        }

        private int GetWeekNumber(DateTime date)
        {
            var culture = new CultureInfo("nb-NO");
            return culture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        public void NavigatePrevious()
        {
            if (IsWeekView)
            {
                CurrentDate = _currentDate.AddDays(-7);
            }
            else if (IsDayView)
            {
                CurrentDate = _currentDate.AddDays(-1);
            }
            else // Month
            {
                CurrentDate = _currentDate.AddMonths(-1);
            }
            _ = LoadCalendarDataAsync();
        }

        public void NavigateNext()
        {
            if (IsWeekView)
            {
                CurrentDate = _currentDate.AddDays(7);
            }
            else if (IsDayView)
            {
                CurrentDate = _currentDate.AddDays(1);
            }
            else // Month
            {
                CurrentDate = _currentDate.AddMonths(1);
            }
            _ = LoadCalendarDataAsync();
        }

        public void NavigateToday()
        {
            CurrentDate = DateTime.Now;
            _ = LoadCalendarDataAsync();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CalendarDayData
    {
        public DateTime Date { get; set; }
        public List<ScheduleItemWithSpacing> Sessions { get; set; } = new();
        
        public string DayName => Date.ToString("ddd", new CultureInfo("nb-NO"));
        public string DayNumber => Date.Day.ToString();
        public bool IsCurrentMonth => Date.Month == DateTime.Now.Month && Date.Year == DateTime.Now.Year;
        public bool IsToday => Date.Date == DateTime.Now.Date;
        
        // Group all sessions by exact time (start + end), maintaining order, with gap info
        public List<SessionGroup> GroupedSessions
        {
            get
            {
                var allSessions = Sessions.Select(s => s.Item).OrderBy(s => s.StartKl).ToList();
                var grouped = allSessions
                    .GroupBy(s => new { s.StartKl, s.SluttKl })
                    .Select(g => g.ToList())
                    .ToList();
                
                var result = new List<SessionGroup>();
                var dayStartTime = new TimeSpan(8, 0, 0); // Day starts at 08:00
                
                for (int i = 0; i < grouped.Count; i++)
                {
                    var group = grouped[i];
                    var gapMinutes = 0.0;
                    
                    if (i == 0)
                    {
                        // First session: calculate gap from start of day (08:00)
                        var firstStartTime = ParseTime(group[0].StartKl);
                        gapMinutes = (firstStartTime - dayStartTime).TotalMinutes;
                    }
                    else
                    {
                        // Calculate the gap in minutes between this group and the previous one
                        var prevGroup = grouped[i - 1];
                        var prevEndTime = ParseTime(prevGroup[0].SluttKl);
                        var currentStartTime = ParseTime(group[0].StartKl);
                        
                        gapMinutes = (currentStartTime - prevEndTime).TotalMinutes;
                    }
                    
                    result.Add(new SessionGroup
                    {
                        Sessions = group,
                        GapMinutes = gapMinutes
                    });
                }
                
                return result;
            }
        }
        
        private TimeSpan ParseTime(string? timeStr)
        {
            if (string.IsNullOrEmpty(timeStr)) return TimeSpan.Zero;
            
            if (timeStr.Contains(':'))
            {
                if (TimeSpan.TryParse(timeStr, out var time))
                    return time;
            }
            else if (timeStr.Length == 4)
            {
                if (int.TryParse(timeStr.Substring(0, 2), out var hours) &&
                    int.TryParse(timeStr.Substring(2, 2), out var minutes))
                {
                    return new TimeSpan(hours, minutes, 0);
                }
            }
            
            return TimeSpan.Zero;
        }
    }
    
    public class SessionGroup
    {
        public List<ScheduleItem> Sessions { get; set; } = new();
        public double GapMinutes { get; set; }
        
        // Convert gap minutes to pixel height based on session box scale
        // Each session box is 50px high and represents its duration
        // We calculate pixels per minute from the first session's duration
        public double GapHeight
        {
            get
            {
                if (GapMinutes <= 0) return 0;
                
                // Use a standard scale: assume 45-minute sessions are 50px
                // This gives us approximately 1.11 pixels per minute
                var pixelsPerMinute = 50.0 / 45.0;
                
                return GapMinutes * pixelsPerMinute;
            }
        }
    }

    public class ScheduleItemWithSpacing
    {
        public ScheduleItem Item { get; set; } = null!;
        public double TopMargin { get; set; } // Minutes from 08:00 or from previous session end
        public int HourSlot { get; set; } // Grid row: 0=08:00, 1=09:00, etc.
    }
}