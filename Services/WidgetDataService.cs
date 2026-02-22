using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    public class WidgetData
    {
        // Daily data
        public int DailyRegistered { get; set; }
        public int DailyTotal { get; set; }
        public double DailyBalance { get; set; }
        
        // Weekly data
        public int WeeklyRegistered { get; set; }
        public int WeeklyTotal { get; set; }
        public double WeeklyBalance { get; set; }
        
        // Monthly data
        public int MonthlyRegistered { get; set; }
        public int MonthlyTotal { get; set; }
        public double MonthlyBalance { get; set; }
        
        // Current class
        public string? CurrentClassName { get; set; }
        public string? CurrentClassTime { get; set; }
        public string? CurrentClassRoom { get; set; }
        
        // Next class
        public string? NextClassName { get; set; }
        public string? NextClassTime { get; set; }
        public string? NextClassRoom { get; set; }
        
        public DateTime LastUpdated { get; set; }
    }

    public class WidgetDataService
    {
        private readonly string _widgetDataPath;
        private readonly ILoggingService? _loggingService;

        public WidgetDataService(ILoggingService? loggingService = null)
        {
            _loggingService = loggingService;
            
            // Use App Group container - macOS will automatically create this when app has proper entitlements
            // The path is deterministic based on the group identifier
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var groupContainersDir = Path.Combine(homeDir, "Library", "Group Containers");
            var widgetDir = Path.Combine(groupContainersDir, "group.com.akademitrack.widget");
            
            // Only try to create if the Group Containers directory exists (means we have entitlements)
            if (Directory.Exists(groupContainersDir))
            {
                if (!Directory.Exists(widgetDir))
                {
                    try
                    {
                        Directory.CreateDirectory(widgetDir);
                        _loggingService?.LogInfo($"Created widget directory: {widgetDir}");
                    }
                    catch (Exception ex)
                    {
                        _loggingService?.LogError($"Failed to create widget directory: {ex.Message}");
                    }
                }
                
                _widgetDataPath = Path.Combine(widgetDir, "widget-data.json");
                _loggingService?.LogInfo($"Widget data path: {_widgetDataPath}");
            }
            else
            {
                // Fallback for development/unsigned builds
                _loggingService?.LogWarning("Group Containers not available - app may not be properly signed");
                var fallbackDir = Path.Combine(homeDir, ".akademitrack");
                Directory.CreateDirectory(fallbackDir);
                _widgetDataPath = Path.Combine(fallbackDir, "widget-data.json");
                _loggingService?.LogInfo($"Using fallback widget data path: {_widgetDataPath}");
            }
        }

        public async Task UpdateWidgetDataAsync(AttendanceSummary? summary, WeeklyAttendanceData? weeklyData, MonthlyAttendanceData? monthlyData, TodayScheduleData? todaySchedule = null)
        {
            try
            {
                if (summary == null)
                {
                    _loggingService?.LogWarning("Cannot update widget: summary is null");
                    return;
                }

                var (weeklyRegistered, weeklyTotal, weeklySaldo) = CalculateWeeklyData(weeklyData);
                var (monthlyRegistered, monthlyTotal, monthlySaldo) = CalculateMonthlyData(monthlyData);

                var widgetData = new WidgetData
                {
                    // Daily data - from today schedule (STU sessions)
                    DailyRegistered = todaySchedule?.RegisteredStuSessions ?? 0,
                    DailyTotal = todaySchedule?.TotalStuSessions ?? 0,
                    DailyBalance = summary.Saldo,
                    
                    // Weekly data
                    WeeklyRegistered = weeklyRegistered,
                    WeeklyTotal = weeklyTotal,
                    WeeklyBalance = weeklySaldo,
                    
                    // Monthly data
                    MonthlyRegistered = monthlyRegistered,
                    MonthlyTotal = monthlyTotal,
                    MonthlyBalance = monthlySaldo,
                    
                    // Current class
                    CurrentClassName = GetClassName(todaySchedule?.CurrentClass),
                    CurrentClassTime = GetClassTime(todaySchedule?.CurrentClass),
                    CurrentClassRoom = GetClassRoom(todaySchedule?.CurrentClass),
                    
                    // Next class
                    NextClassName = GetClassName(todaySchedule?.NextClass),
                    NextClassTime = GetClassTime(todaySchedule?.NextClass),
                    NextClassRoom = GetClassRoom(todaySchedule?.NextClass),
                    
                    LastUpdated = DateTime.Now
                };

                var lastUpdated = widgetData.LastUpdated.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                
                var json = JsonSerializer.Serialize(new
                {
                    DailyRegistered = widgetData.DailyRegistered,
                    DailyTotal = widgetData.DailyTotal,
                    DailyBalance = widgetData.DailyBalance,
                    WeeklyRegistered = widgetData.WeeklyRegistered,
                    WeeklyTotal = widgetData.WeeklyTotal,
                    WeeklyBalance = widgetData.WeeklyBalance,
                    MonthlyRegistered = widgetData.MonthlyRegistered,
                    MonthlyTotal = widgetData.MonthlyTotal,
                    MonthlyBalance = widgetData.MonthlyBalance,
                    CurrentClassName = widgetData.CurrentClassName,
                    CurrentClassTime = widgetData.CurrentClassTime,
                    CurrentClassRoom = widgetData.CurrentClassRoom,
                    NextClassName = widgetData.NextClassName,
                    NextClassTime = widgetData.NextClassTime,
                    NextClassRoom = widgetData.NextClassRoom,
                    LastUpdated = lastUpdated
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_widgetDataPath, json);
                _loggingService?.LogInfo($"Widget data updated successfully at {_widgetDataPath}");
                _loggingService?.LogInfo($"Widget data: Daily={widgetData.DailyRegistered}/{widgetData.DailyTotal}, Next={widgetData.NextClassName}");
                
                // Force widget to reload immediately
                ForceWidgetReload();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Failed to update widget data: {ex.Message}");
            }
        }

        private void ForceWidgetReload()
        {
            try
            {
                // Tell WidgetKit to reload all timelines immediately
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/killall",
                        Arguments = "WidgetKit",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(500);
                _loggingService?.LogInfo("Triggered widget refresh");
            }
            catch (Exception ex)
            {
                _loggingService?.LogWarning($"Failed to trigger widget refresh: {ex.Message}");
            }
        }

        private (int registered, int total, double saldo) CalculateWeeklyData(WeeklyAttendanceData? weeklyData)
        {
            if (weeklyData?.DailyAttendance == null || weeklyData.DailyAttendance.Count == 0)
                return (0, 0, 0);

            double totalAttended = 0;
            double totalRequired = 0;

            foreach (var day in weeklyData.DailyAttendance)
            {
                totalAttended += day.RegisteredSessions;
                totalRequired += day.TotalSessions;
            }

            return ((int)totalAttended, (int)totalRequired, totalAttended - totalRequired);
        }

        private (int registered, int total, double saldo) CalculateMonthlyData(MonthlyAttendanceData? monthlyData)
        {
            if (monthlyData == null)
                return (0, 0, 0);

            return ((int)monthlyData.RegisteredSessions, (int)monthlyData.TotalSessions, monthlyData.RegisteredSessions - monthlyData.TotalSessions);
        }

        private string GetClassName(ScheduleItem? scheduleItem)
        {
            if (scheduleItem == null)
                return "Ingen time";

            // Use DisplayName if available, otherwise use Fag
            return !string.IsNullOrEmpty(scheduleItem.DisplayName) 
                ? scheduleItem.DisplayName 
                : scheduleItem.Fag ?? "Ukjent fag";
        }

        private string GetClassTime(ScheduleItem? scheduleItem)
        {
            if (scheduleItem == null || string.IsNullOrEmpty(scheduleItem.StartKl) || string.IsNullOrEmpty(scheduleItem.SluttKl))
                return "--:-- - --:--";

            string startTime = FormatTime(scheduleItem.StartKl);
            string endTime = FormatTime(scheduleItem.SluttKl);

            // Check if the class is tomorrow
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (scheduleItem.Dato != today)
            {
                return $"I morgen {startTime} - {endTime}";
            }

            return $"{startTime} - {endTime}";
        }

        private string GetClassRoom(ScheduleItem? scheduleItem)
        {
            if (scheduleItem == null || string.IsNullOrEmpty(scheduleItem.Romnr))
                return "";

            return $"Rom {scheduleItem.Romnr.Trim()}";
        }

        private string FormatTime(string time)
        {
            // Time comes in format like "0830" or "1415"
            if (string.IsNullOrEmpty(time) || time.Length < 4)
                return time;

            // Insert colon: "0830" -> "08:30"
            return time.PadLeft(4, '0').Insert(2, ":");
        }
    }
}
