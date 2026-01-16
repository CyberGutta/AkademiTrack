using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;
using Avalonia.Threading;

namespace AkademiTrack.Services
{
    public class LoggingService : ILoggingService, IDisposable
    {
        private readonly ObservableCollection<LogEntry> _logEntries;
        private bool _showDetailedLogs = true;
        private Timer? _logCleanupTimer;
        
        private const int LOG_RETENTION_DAYS = 7;
        private const int MAX_LOG_ENTRIES = 10000; // Safety limit to prevent memory issues

        public event EventHandler<LogEntryEventArgs>? LogEntryAdded;

        public ObservableCollection<LogEntry> LogEntries => _logEntries;

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set => _showDetailedLogs = value;
        }

        public LoggingService()
        {
            _logEntries = new ObservableCollection<LogEntry>();
            
            // Start log cleanup timer (runs every 6 hours)
            StartLogCleanupTimer();
            
            LogInfo("✓ LoggingService initialized with 7-day retention policy");
        }

        private void StartLogCleanupTimer()
        {
            _logCleanupTimer = new Timer(
                async _ => await CleanOldLogsAsync(),
                null,
                TimeSpan.FromHours(6),  // First cleanup after 6 hours
                TimeSpan.FromHours(6)   // Then every 6 hours
            );
            
            Debug.WriteLine("✓ Log retention timer started (7-day rolling window, checks every 6 hours)");
        }

        private async System.Threading.Tasks.Task CleanOldLogsAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-LOG_RETENTION_DAYS);
                var logsToRemove = _logEntries
                    .Where(log => log.Timestamp < cutoffDate)
                    .ToList();

                if (logsToRemove.Any())
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var log in logsToRemove)
                        {
                            _logEntries.Remove(log);
                        }
                    });

                    Debug.WriteLine($"✓ Log retention: Cleaned {logsToRemove.Count} old logs (older than {LOG_RETENTION_DAYS} days)");
                }
                
                // Also enforce max entry limit as safety measure
                if (_logEntries.Count > MAX_LOG_ENTRIES)
                {
                    var excessCount = _logEntries.Count - MAX_LOG_ENTRIES;
                    var oldestLogs = _logEntries
                        .OrderBy(l => l.Timestamp)
                        .Take(excessCount)
                        .ToList();
                        
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var log in oldestLogs)
                        {
                            _logEntries.Remove(log);
                        }
                    });
                    
                    Debug.WriteLine($"✓ Log retention: Removed {excessCount} logs to stay under {MAX_LOG_ENTRIES} entry limit");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in log cleanup: {ex.Message}");
            }
        }

        public void LogInfo(string message)
        {
            AddLog(message, "INFO");
        }

        public void LogSuccess(string message)
        {
            AddLog(message, "SUCCESS");
        }

        public void LogWarning(string message)
        {
            AddLog(message, "WARNING");
        }

        public void LogError(string message)
        {
            AddLog(message, "ERROR");
        }

        public void LogDebug(string message)
        {
            AddLog(message, "DEBUG");
        }

        private void AddLog(string message, string level)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            Dispatcher.UIThread.Post(() =>
            {
                _logEntries.Add(logEntry);
                LogEntryAdded?.Invoke(this, new LogEntryEventArgs(logEntry));
            });
        }

        public void ClearLogs()
        {
            Dispatcher.UIThread.Post(() => _logEntries.Clear());
        }

        public IReadOnlyList<LogEntry> GetLogs(LogLevel? filterLevel = null)
        {
            if (filterLevel == null)
                return _logEntries.ToList();

            var levelString = filterLevel.ToString()?.ToUpper();
            return _logEntries.Where(log => log.Level == levelString).ToList();
        }

        public void Dispose()
        {
            _logCleanupTimer?.Dispose();
            _logCleanupTimer = null;
            Debug.WriteLine("LoggingService disposed - log cleanup timer stopped");
        }
    }
}