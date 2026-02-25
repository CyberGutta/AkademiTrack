using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Services;
using AkademiTrack.Services.Interfaces;
using Avalonia.Threading;

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
                if (IsWeekView)
                {
                    var monday = GetMonday(_currentDate);
                    var friday = monday.AddDays(4);
                    return $"Uke {GetWeekNumber(_currentDate)} ({monday:dd.MMM} - {friday:dd.MMM})";
                }
                else if (IsDayView)
                {
                    return _currentDate.ToString("dddd dd. MMMM yyyy", new System.Globalization.CultureInfo("nb-NO"));
                }
                else // Month
                {
                    return _currentDate.ToString("MMMM yyyy", new System.Globalization.CultureInfo("nb-NO"));
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
                
                // Calculate max sessions per hour across all days
                OnPropertyChanged(nameof(MaxHeight08));
                OnPropertyChanged(nameof(MaxHeight09));
                OnPropertyChanged(nameof(MaxHeight10));
                OnPropertyChanged(nameof(MaxHeight11));
                OnPropertyChanged(nameof(MaxHeight12));
                OnPropertyChanged(nameof(MaxHeight13));
                OnPropertyChanged(nameof(MaxHeight14));
                OnPropertyChanged(nameof(MaxHeight15));
            }
        }
        
        // Calculate maximum row heights across all days (each session = 54px)
        public double MaxHeight08 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt08.Count) * 54, 40) : 40;
        public double MaxHeight09 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt09.Count) * 54, 40) : 40;
        public double MaxHeight10 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt10.Count) * 54, 40) : 40;
        public double MaxHeight11 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt11.Count) * 54, 40) : 40;
        public double MaxHeight12 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt12.Count) * 54, 40) : 40;
        public double MaxHeight13 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt13.Count) * 54, 40) : 40;
        public double MaxHeight14 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt14.Count) * 54, 40) : 40;
        public double MaxHeight15 => _calendarDays.Any() ? Math.Max(_calendarDays.Max(d => d.SessionsAt15.Count) * 54, 40) : 40;

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
                    var firstDay = new DateTime(_currentDate.Year, _currentDate.Month, 1);
                    var lastDay = firstDay.AddMonths(1).AddDays(-1);
                    _loggingService?.LogInfo($"[CALENDAR] Fetching month data from {firstDay:yyyy-MM-dd} to {lastDay:yyyy-MM-dd}");
                    scheduleItems = await _attendanceService.GetScheduleRangeAsync(firstDay, lastDay);
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
                // For month view, group by date
                if (items != null && items.Any())
                {
                    var groupedByDate = items.GroupBy(i => i.Dato).OrderBy(g => g.Key);
                    
                    foreach (var dayGroup in groupedByDate)
                    {
                        var date = ParseDate(dayGroup.Key);
                        var sessions = dayGroup.OrderBy(s => s.StartKl).ToList();
                        
                        var sessionsWithSpacing = sessions.Select(s => new ScheduleItemWithSpacing
                        {
                            Item = s,
                            TopMargin = 2
                        }).ToList();
                        
                        _loggingService?.LogInfo($"[CALENDAR] Date {date:yyyy-MM-dd}: {sessions.Count} sessions");
                        
                        calendarDays.Add(new CalendarDayData
                        {
                            Date = date,
                            Sessions = sessionsWithSpacing
                        });
                    }
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
            
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
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
            var culture = new System.Globalization.CultureInfo("nb-NO");
            return culture.Calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
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
        
        public string DayName => Date.ToString("ddd", new System.Globalization.CultureInfo("nb-NO"));
        public string DayNumber => Date.Day.ToString();
        
        // Sessions grouped by hour
        public List<ScheduleItem> SessionsAt08 => Sessions.Where(s => s.HourSlot == 0).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt09 => Sessions.Where(s => s.HourSlot == 1).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt10 => Sessions.Where(s => s.HourSlot == 2).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt11 => Sessions.Where(s => s.HourSlot == 3).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt12 => Sessions.Where(s => s.HourSlot == 4).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt13 => Sessions.Where(s => s.HourSlot == 5).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt14 => Sessions.Where(s => s.HourSlot == 6).Select(s => s.Item).ToList();
        public List<ScheduleItem> SessionsAt15 => Sessions.Where(s => s.HourSlot == 7).Select(s => s.Item).ToList();
        
        // Calculate row heights based on number of sessions (each session = 54px including margin)
        public double Height08 => Math.Max(SessionsAt08.Count * 54, 40);
        public double Height09 => Math.Max(SessionsAt09.Count * 54, 40);
        public double Height10 => Math.Max(SessionsAt10.Count * 54, 40);
        public double Height11 => Math.Max(SessionsAt11.Count * 54, 40);
        public double Height12 => Math.Max(SessionsAt12.Count * 54, 40);
        public double Height13 => Math.Max(SessionsAt13.Count * 54, 40);
        public double Height14 => Math.Max(SessionsAt14.Count * 54, 40);
        public double Height15 => Math.Max(SessionsAt15.Count * 54, 40);
    }

    public class ScheduleItemWithSpacing
    {
        public ScheduleItem Item { get; set; } = null!;
        public double TopMargin { get; set; } // Minutes from 08:00 or from previous session end
        public int HourSlot { get; set; } // Grid row: 0=08:00, 1=09:00, etc.
    }
}
