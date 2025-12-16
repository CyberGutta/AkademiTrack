using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.Common;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services
{
    public class SettingsService : ISettingsService, INotifyPropertyChanged
    {
        private ViewModels.AppSettings _settings;
        private SchoolHoursSettings _schoolHours;
        private readonly string _settingsFilePath;
        private readonly string _schoolHoursFilePath;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public SettingsService()
        {
            _settings = new ViewModels.AppSettings();
            _schoolHours = SchoolHoursSettings.GetDefault();
            
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            
            _settingsFilePath = Path.Combine(appDataDir, "settings.json");
            _schoolHoursFilePath = Path.Combine(appDataDir, "school_hours.json");
        }

        #region Properties

        public bool ShowActivityLog
        {
            get => _settings.ShowActivityLog;
            set
            {
                if (_settings.ShowActivityLog != value)
                {
                    var oldValue = _settings.ShowActivityLog;
                    _settings.ShowActivityLog = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(ShowActivityLog), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool ShowDetailedLogs
        {
            get => _settings.ShowDetailedLogs;
            set
            {
                if (_settings.ShowDetailedLogs != value)
                {
                    var oldValue = _settings.ShowDetailedLogs;
                    _settings.ShowDetailedLogs = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(ShowDetailedLogs), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool StartWithSystem
        {
            get => _settings.StartWithSystem;
            set
            {
                if (_settings.StartWithSystem != value)
                {
                    var oldValue = _settings.StartWithSystem;
                    _settings.StartWithSystem = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(StartWithSystem), oldValue, value));
                    
                    // Update system auto-start when this changes
                    ViewModels.AutoStartManager.SetAutoStart(value);
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool AutoStartAutomation
        {
            get => _settings.AutoStartAutomation;
            set
            {
                if (_settings.AutoStartAutomation != value)
                {
                    var oldValue = _settings.AutoStartAutomation;
                    _settings.AutoStartAutomation = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(AutoStartAutomation), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool StartMinimized
        {
            get => _settings.StartMinimized;
            set
            {
                if (_settings.StartMinimized != value)
                {
                    var oldValue = _settings.StartMinimized;
                    _settings.StartMinimized = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(StartMinimized), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool EnableNotifications
        {
            get => _settings.EnableNotifications;
            set
            {
                if (_settings.EnableNotifications != value)
                {
                    var oldValue = _settings.EnableNotifications;
                    _settings.EnableNotifications = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(EnableNotifications), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public bool InitialSetupCompleted
        {
            get => _settings.InitialSetupCompleted;
            set
            {
                if (_settings.InitialSetupCompleted != value)
                {
                    var oldValue = _settings.InitialSetupCompleted;
                    _settings.InitialSetupCompleted = value;
                    OnPropertyChanged();
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(nameof(InitialSetupCompleted), oldValue, value));
                    _ = SaveSettingsAsync();
                }
            }
        }

        public SchoolHoursSettings SchoolHours => _schoolHours;

        #endregion

        #region Methods

        public async Task<Result> LoadSettingsAsync()
        {
            try
            {
                // Load main settings
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<ViewModels.AppSettings>(json);
                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                        NotifyAllPropertiesChanged();
                    }
                }

                // Load school hours
                if (File.Exists(_schoolHoursFilePath))
                {
                    var json = await File.ReadAllTextAsync(_schoolHoursFilePath);
                    var loadedSchoolHours = JsonSerializer.Deserialize<SchoolHoursSettings>(json);
                    if (loadedSchoolHours != null)
                    {
                        _schoolHours = loadedSchoolHours;
                    }
                }

                return Result.Successful();
            }
            catch (Exception ex)
            {
                return Result.Failed($"Failed to load settings: {ex.Message}", ex);
            }
        }

        public async Task<Result> SaveSettingsAsync()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;
                
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(_settingsFilePath, json);
                return Result.Successful();
            }
            catch (Exception ex)
            {
                return Result.Failed($"Failed to save settings: {ex.Message}", ex);
            }
        }

        public async Task<Result> SaveSchoolHoursAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_schoolHours, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(_schoolHoursFilePath, json);
                return Result.Successful();
            }
            catch (Exception ex)
            {
                return Result.Failed($"Failed to save school hours: {ex.Message}", ex);
            }
        }

        public async Task<Result> ResetToDefaultsAsync()
        {
            try
            {
                _settings = new ViewModels.AppSettings();
                NotifyAllPropertiesChanged();
                return await SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                return Result.Failed($"Failed to reset settings: {ex.Message}", ex);
            }
        }

        public async Task<Result> ResetSchoolHoursToDefaultAsync()
        {
            try
            {
                _schoolHours = SchoolHoursSettings.GetDefault();
                return await SaveSchoolHoursAsync();
            }
            catch (Exception ex)
            {
                return Result.Failed($"Failed to reset school hours: {ex.Message}", ex);
            }
        }

        #endregion

        #region Private Methods

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void NotifyAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(ShowActivityLog));
            OnPropertyChanged(nameof(ShowDetailedLogs));
            OnPropertyChanged(nameof(StartWithSystem));
            OnPropertyChanged(nameof(AutoStartAutomation));
            OnPropertyChanged(nameof(StartMinimized));
            OnPropertyChanged(nameof(EnableNotifications));
            OnPropertyChanged(nameof(InitialSetupCompleted));
        }

        #endregion
    }
}