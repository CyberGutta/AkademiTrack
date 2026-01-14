using System;
using System.Threading;
using System.Threading.Tasks;

namespace AkademiTrack.Services.Interfaces
{
    public interface IAutomationService
    {
        event EventHandler<AutomationStatusChangedEventArgs>? StatusChanged;
        event EventHandler<AutomationProgressEventArgs>? ProgressUpdated;
        event EventHandler<SessionRegisteredEventArgs>? SessionRegistered;
        
        bool IsRunning { get; }
        string CurrentStatus { get; }
        
        Task<AutomationResult> StartAsync(CancellationToken cancellationToken = default);
        Task<AutomationResult> StopAsync();
        
        Task<AutomationResult> RefreshAuthenticationAsync();
        Task<bool> CheckSchoolHoursAsync();
    }

    public class AutomationResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public Exception? Exception { get; set; }
        
        public static AutomationResult Successful(string? message = null) => 
            new() { Success = true, Message = message };
            
        public static AutomationResult Failed(string message, Exception? exception = null) => 
            new() { Success = false, Message = message, Exception = exception };
    }

    public class AutomationStatusChangedEventArgs : EventArgs
    {
        public bool IsRunning { get; }
        public string Status { get; }
        
        public AutomationStatusChangedEventArgs(bool isRunning, string status)
        {
            IsRunning = isRunning;
            Status = status;
        }
    }

    public class AutomationProgressEventArgs : EventArgs
    {
        public string Message { get; }
        public int? CycleCount { get; }
        public DateTime Timestamp { get; }
        
        public AutomationProgressEventArgs(string message, int? cycleCount = null)
        {
            Message = message;
            CycleCount = cycleCount;
            Timestamp = DateTime.Now;
        }
    }

    public class SessionRegisteredEventArgs : EventArgs
    {
        public string SessionTime { get; set; } = "";
        public DateTime RegistrationTime { get; set; }
    }
}