using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;
using Avalonia.Threading;

namespace AkademiTrack.Services
{
    public class LoggingService : ILoggingService
    {
        private readonly ObservableCollection<LogEntry> _logEntries;
        private bool _showDetailedLogs = true;
        private const int MaxLogEntries = 100;

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
        }

        public void LogInfo(string message) => AddLogEntry(message, "INFO");
        public void LogSuccess(string message) => AddLogEntry(message, "SUCCESS");
        public void LogWarning(string message) => AddLogEntry(message, "WARNING");
        public void LogError(string message) => AddLogEntry(message, "ERROR");
        public void LogDebug(string message)
        {
            if (_showDetailedLogs)
            {
                AddLogEntry(message, "DEBUG");
            }
        }

        public void ClearLogs()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _logEntries.Clear();
                LogInfo("Logger tømt");
            });
        }

        public IReadOnlyList<LogEntry> GetLogs(LogLevel? filterLevel = null)
        {
            if (filterLevel.HasValue)
            {
                var levelString = filterLevel.Value.ToString().ToUpper();
                return _logEntries.Where(entry => entry.Level == levelString).ToList();
            }
            return _logEntries.ToList();
        }

        private void AddLogEntry(string message, string level)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            };

            Dispatcher.UIThread.Post(() =>
            {
                _logEntries.Add(logEntry);

                // Maintain max entries limit
                while (_logEntries.Count > MaxLogEntries)
                {
                    _logEntries.RemoveAt(0);
                }

                LogEntryAdded?.Invoke(this, new LogEntryEventArgs(logEntry));
            }, DispatcherPriority.Background);
        }
    }
}