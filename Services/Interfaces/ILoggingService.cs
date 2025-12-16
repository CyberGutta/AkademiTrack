using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services.Interfaces
{
    public interface ILoggingService
    {
        event EventHandler<LogEntryEventArgs>? LogEntryAdded;
        
        ObservableCollection<LogEntry> LogEntries { get; }
        bool ShowDetailedLogs { get; set; }
        
        void LogInfo(string message);
        void LogSuccess(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogDebug(string message);
        
        void ClearLogs();
        IReadOnlyList<LogEntry> GetLogs(LogLevel? filterLevel = null);
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error
    }



    public class LogEntryEventArgs : EventArgs
    {
        public LogEntry LogEntry { get; }
        
        public LogEntryEventArgs(LogEntry logEntry)
        {
            LogEntry = logEntry;
        }
    }
}