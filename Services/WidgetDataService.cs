using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Services.DependencyInjection;
using AkademiTrack.ViewModels;

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
        private readonly INotificationService? _notificationService;
        private WidgetData? _lastWidgetData;

        public WidgetDataService(ILoggingService? loggingService = null, INotificationService? notificationService = null)
        {
            _loggingService = loggingService;
            _notificationService = notificationService;
            
            // Try App Group first (for sandboxed scenarios)
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var groupContainersDir = Path.Combine(homeDir, "Library", "Group Containers");
            var widgetDir = Path.Combine(groupContainersDir, "6SF4T9DUN4.com.CyberBrothers.akademitrack");
            
            if (Directory.Exists(groupContainersDir))
            {
                try
                {
                    if (!Directory.Exists(widgetDir))
                    {
                        Directory.CreateDirectory(widgetDir);
                    }
                    
                    // Test if we can actually write to this directory
                    var testFile = Path.Combine(widgetDir, ".write-test");
                    try
                    {
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        _widgetDataPath = Path.Combine(widgetDir, "widget-data.json");
                        _loggingService?.LogInfo($"✅ Using App Group path: {_widgetDataPath}");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _loggingService?.LogError("❌ App Group permission denied - user needs to grant access");
                        _widgetDataPath = GetFallbackPath(homeDir);
                        
                        // Write a permission error file that the widget can detect
                        WritePermissionErrorAsync().Wait();
                        
                        // Show notification to user
                        try
                        {
                            _ = _notificationService?.ShowNotificationAsync(
                                "Widget krever tillatelse",
                                "Lukk appen helt (Cmd+Q), åpne på nytt, og klikk Tillat når du blir spurt.",
                                NotificationLevel.Warning
                            );
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Failed to use App Group: {ex.Message}");
                    _widgetDataPath = GetFallbackPath(homeDir);
                }
            }
            else
            {
                _widgetDataPath = GetFallbackPath(homeDir);
            }
        }
        
        private async Task WritePermissionErrorAsync()
        {
            try
            {
                // Write error state to the App Group location (if we can)
                // This tells the widget that permission was denied
                var errorData = new
                {
                    DailyRegistered = 0,
                    DailyTotal = 0,
                    DailyBalance = 0.0,
                    WeeklyRegistered = 0,
                    WeeklyTotal = 0,
                    WeeklyBalance = 0.0,
                    MonthlyRegistered = 0,
                    MonthlyTotal = 0,
                    MonthlyBalance = 0.0,
                    CurrentClassName = "Ingen tilgang",
                    CurrentClassTime = "Lukk appen helt og åpne på nytt, klikk Tillat",
                    CurrentClassRoom = (string?)null,
                    NextClassName = (string?)null,
                    NextClassTime = (string?)null,
                    NextClassRoom = (string?)null,
                    LastUpdated = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
                
                var json = JsonSerializer.Serialize(errorData, new JsonSerializerOptions { WriteIndented = true });
                
                // Try to write to App Group location
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var groupContainersDir = Path.Combine(homeDir, "Library", "Group Containers");
                var widgetDir = Path.Combine(groupContainersDir, "6SF4T9DUN4.com.CyberBrothers.akademitrack");
                var errorPath = Path.Combine(widgetDir, "widget-data.json");
                
                try
                {
                    await File.WriteAllTextAsync(errorPath, json);
                    _loggingService?.LogInfo("✅ Wrote permission error to widget file");
                }
                catch
                {
                    // If we can't write to App Group, the widget will show "Venter på data"
                    // which is fine - it indicates something is wrong
                    _loggingService?.LogWarning("❌ Cannot write permission error - App Group not accessible at all");
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Failed to write permission error: {ex.Message}");
            }
        }
        
        private string GetFallbackPath(string homeDir)
        {
            var fallbackDir = Path.Combine(homeDir, ".akademitrack");
            Directory.CreateDirectory(fallbackDir);
            var path = Path.Combine(fallbackDir, "widget-data.json");
            _loggingService?.LogInfo($"Using fallback path: {path}");
            return path;
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

                try
                {
                    await File.WriteAllTextAsync(_widgetDataPath, json);
                    
                    // Cache the widget data for heartbeat updates
                    _lastWidgetData = widgetData;
                    
                    _loggingService?.LogInfo($"Widget data updated successfully at {_widgetDataPath}");
                    _loggingService?.LogInfo($"Widget data: Daily={widgetData.DailyRegistered}/{widgetData.DailyTotal}, Next={widgetData.NextClassName}");
                    
                    // Force widget to reload immediately
                    ForceWidgetReload();
                }
                catch (UnauthorizedAccessException ex)
                {
                    _loggingService?.LogError($"⚠️ PERMISSION DENIED: Cannot write widget data: {ex.Message}");
                    _loggingService?.LogError("This usually means App Group entitlements are not properly configured.");
                }
                catch (Exception ex)
                {
                    _loggingService?.LogError($"Failed to write widget data: {ex.Message}");
                }
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
                // Use Darwin notifications to signal the widget to reload
                // This is the proper way to communicate between app and widget
                var notifyProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/notifyutil",
                        Arguments = "-p com.CyberBrothers.akademitrack.widget.reload",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                notifyProcess.Start();
                notifyProcess.WaitForExit(500);
                
                _loggingService?.LogDebug("Sent widget reload notification");
                
                // Also kill WidgetKit as backup
                var killProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/bin/killall",
                        Arguments = "-9 WidgetKit",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                killProcess.Start();
                killProcess.WaitForExit(500);
                
                _loggingService?.LogDebug("Killed WidgetKit for immediate refresh");
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

        /// <summary>
        /// Writes an "app closed" state to the widget data file immediately.
        /// This allows the widget to show the error state without waiting for staleness detection.
        /// </summary>
        public async Task WriteAppClosedStateAsync()
        {
            try
            {
                var closedData = new WidgetData
                {
                    DailyRegistered = 0,
                    DailyTotal = 0,
                    DailyBalance = 0,
                    WeeklyRegistered = 0,
                    WeeklyTotal = 0,
                    WeeklyBalance = 0,
                    MonthlyRegistered = 0,
                    MonthlyTotal = 0,
                    MonthlyBalance = 0,
                    CurrentClassName = "Åpne appen",
                    CurrentClassTime = "Appen må være åpen",
                    CurrentClassRoom = null,
                    NextClassName = null,
                    NextClassTime = null,
                    NextClassRoom = null,
                    LastUpdated = DateTime.Now.AddYears(-1) // Very old timestamp to trigger immediate error
                };

                var lastUpdated = closedData.LastUpdated.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                
                var json = JsonSerializer.Serialize(new
                {
                    DailyRegistered = closedData.DailyRegistered,
                    DailyTotal = closedData.DailyTotal,
                    DailyBalance = closedData.DailyBalance,
                    WeeklyRegistered = closedData.WeeklyRegistered,
                    WeeklyTotal = closedData.WeeklyTotal,
                    WeeklyBalance = closedData.WeeklyBalance,
                    MonthlyRegistered = closedData.MonthlyRegistered,
                    MonthlyTotal = closedData.MonthlyTotal,
                    MonthlyBalance = closedData.MonthlyBalance,
                    CurrentClassName = closedData.CurrentClassName,
                    CurrentClassTime = closedData.CurrentClassTime,
                    CurrentClassRoom = closedData.CurrentClassRoom,
                    NextClassName = closedData.NextClassName,
                    NextClassTime = closedData.NextClassTime,
                    NextClassRoom = closedData.NextClassRoom,
                    LastUpdated = lastUpdated
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_widgetDataPath, json);
                _loggingService?.LogInfo("[WIDGET] Wrote app closed state");
                
                // Force widget reload
                ForceWidgetReload();
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"[WIDGET] Failed to write closed state: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the widget data file with a fresh timestamp to maintain heartbeat.
        /// Uses cached data if available, otherwise creates minimal data.
        /// </summary>
        public async Task RefreshHeartbeatAsync()
        {
            try
            {
                if (_lastWidgetData == null)
                {
                    // No cached data yet - skip heartbeat until first real update
                    return;
                }

                // Update the timestamp on the cached data
                _lastWidgetData.LastUpdated = DateTime.Now;

                var lastUpdated = _lastWidgetData.LastUpdated.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                
                var json = JsonSerializer.Serialize(new
                {
                    DailyRegistered = _lastWidgetData.DailyRegistered,
                    DailyTotal = _lastWidgetData.DailyTotal,
                    DailyBalance = _lastWidgetData.DailyBalance,
                    WeeklyRegistered = _lastWidgetData.WeeklyRegistered,
                    WeeklyTotal = _lastWidgetData.WeeklyTotal,
                    WeeklyBalance = _lastWidgetData.WeeklyBalance,
                    MonthlyRegistered = _lastWidgetData.MonthlyRegistered,
                    MonthlyTotal = _lastWidgetData.MonthlyTotal,
                    MonthlyBalance = _lastWidgetData.MonthlyBalance,
                    CurrentClassName = _lastWidgetData.CurrentClassName,
                    CurrentClassTime = _lastWidgetData.CurrentClassTime,
                    CurrentClassRoom = _lastWidgetData.CurrentClassRoom,
                    NextClassName = _lastWidgetData.NextClassName,
                    NextClassTime = _lastWidgetData.NextClassTime,
                    NextClassRoom = _lastWidgetData.NextClassRoom,
                    LastUpdated = lastUpdated
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_widgetDataPath, json);
                _loggingService?.LogDebug($"[WIDGET HEARTBEAT] Timestamp updated");
            }
            catch (Exception ex)
            {
                _loggingService?.LogWarning($"[WIDGET HEARTBEAT] Failed to update timestamp: {ex.Message}");
            }
        }
    }
}
