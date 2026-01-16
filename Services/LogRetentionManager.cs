using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using AkademiTrack.Services.Interfaces;
using Avalonia.Threading;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Manages log retention policy - keeps logs for 7 days
    /// </summary>
    public class LogRetentionManager : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private Timer? _cleanupTimer;
        
        private const int LOG_RETENTION_DAYS = 7;
        private const int MAX_LOG_ENTRIES = 10000; // Safety limit to prevent memory issues

        public LogRetentionManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Start the automatic log cleanup timer
        /// </summary>
        public void Start()
        {
            // Run cleanup every 6 hours
            _cleanupTimer = new Timer(
                async _ => await CleanOldLogsAsync(),
                null,
                TimeSpan.FromMinutes(5),  // First cleanup after 5 minutes (to catch any old logs on startup)
                TimeSpan.FromHours(6)     // Then every 6 hours
            );
            
            Debug.WriteLine("✓ Log retention manager started (7-day rolling window, checks every 6 hours)");
            _loggingService.LogInfo("✓ Log retention policy active: 7 days");
        }

        /// <summary>
        /// Clean logs older than the retention period
        /// </summary>
        private async System.Threading.Tasks.Task CleanOldLogsAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-LOG_RETENTION_DAYS);
                var logsToRemove = _loggingService.LogEntries
                    .Where(log => log.Timestamp < cutoffDate)
                    .ToList();

                if (logsToRemove.Any())
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var log in logsToRemove)
                        {
                            _loggingService.LogEntries.Remove(log);
                        }
                    });

                    Debug.WriteLine($"✓ Log retention: Cleaned {logsToRemove.Count} old logs (older than {LOG_RETENTION_DAYS} days)");
                }
                
                // Also enforce max entry limit as safety measure
                if (_loggingService.LogEntries.Count > MAX_LOG_ENTRIES)
                {
                    var excessCount = _loggingService.LogEntries.Count - MAX_LOG_ENTRIES;
                    var oldestLogs = _loggingService.LogEntries
                        .OrderBy(l => l.Timestamp)
                        .Take(excessCount)
                        .ToList();
                        
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var log in oldestLogs)
                        {
                            _loggingService.LogEntries.Remove(log);
                        }
                    });
                    
                    Debug.WriteLine($"✓ Log retention: Removed {excessCount} logs to stay under {MAX_LOG_ENTRIES} entry limit");
                    _loggingService.LogWarning($"Log limit reached - removed {excessCount} oldest entries");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in log retention cleanup: {ex.Message}");
                // Don't log to service here to avoid potential infinite loop
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            Debug.WriteLine("LogRetentionManager disposed - cleanup timer stopped");
        }
    }
}