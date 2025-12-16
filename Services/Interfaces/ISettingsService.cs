using System;
using System.Threading.Tasks;
using AkademiTrack.Common;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services.Interfaces
{
    public interface ISettingsService
    {
        event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        
        // General Settings
        bool ShowActivityLog { get; set; }
        bool ShowDetailedLogs { get; set; }
        bool StartWithSystem { get; set; }
        bool AutoStartAutomation { get; set; }
        bool StartMinimized { get; set; }
        bool EnableNotifications { get; set; }
        bool InitialSetupCompleted { get; set; }
        
        // School Hours Settings
        SchoolHoursSettings SchoolHours { get; }
        
        // Methods
        Task<Result> LoadSettingsAsync();
        Task<Result> SaveSettingsAsync();
        Task<Result> ResetToDefaultsAsync();
        
        // School Hours Methods
        Task<Result> SaveSchoolHoursAsync();
        Task<Result> ResetSchoolHoursToDefaultAsync();
    }

    public class SettingsChangedEventArgs : EventArgs
    {
        public string PropertyName { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }
        
        public SettingsChangedEventArgs(string propertyName, object? oldValue, object? newValue)
        {
            PropertyName = propertyName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }


}