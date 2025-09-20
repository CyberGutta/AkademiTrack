using Avalonia.Data.Converters;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace AkademiTrack.ViewModels
{
    // Settings data class
    public class AppSettings
    {
        public bool ShowActivityLog { get; set; } = false; // Hidden by default
        public bool ShowDetailedLogs { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    // Converters
    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[1] : parts[0];
                }
            }
            return value?.ToString() ?? string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringEqualityConverter : IValueConverter
    {
        public static readonly StringEqualityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string paramString)
            {
                return string.Equals(stringValue, paramString, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToIntConverter : IValueConverter
    {
        public static readonly BoolToIntConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2 && int.TryParse(parts[0], out int falseValue) && int.TryParse(parts[1], out int trueValue))
                {
                    return boolValue ? trueValue : falseValue;
                }
            }
            return 600; // Default width
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToGridLengthConverter : IValueConverter
    {
        public static readonly BoolToGridLengthConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    var lengthStr = boolValue ? parts[1] : parts[0];
                    if (lengthStr == "0")
                        return new Avalonia.Controls.GridLength(0);
                    if (lengthStr.EndsWith("*"))
                    {
                        var multiplier = lengthStr.TrimEnd('*');
                        if (string.IsNullOrEmpty(multiplier) || multiplier == "1")
                            return new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
                        if (double.TryParse(multiplier, out double mult))
                            return new Avalonia.Controls.GridLength(mult, Avalonia.Controls.GridUnitType.Star);
                    }
                    if (double.TryParse(lengthStr, out double pixels))
                        return new Avalonia.Controls.GridLength(pixels, Avalonia.Controls.GridUnitType.Pixel);
                }
            }
            return new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Simple Command implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    // Application info class
    public class ApplicationInfo
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }

        public ApplicationInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Name = assembly.GetName().Name ?? "AkademiTrack";
            Version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            Description = "Akademiet automatisk fremmøte registerings program";
        }

        public override string ToString()
        {
            return $"{Name} v{Version}\n{Description}";
        }
    }

    // ViewModel
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private bool _showActivityLog = false; // Hidden by default
        private bool _showDetailedLogs = true;
        private ObservableCollection<LogEntry> _allLogEntries = new();
        private ObservableCollection<LogEntry> _displayedLogEntries = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;

        public ApplicationInfo ApplicationInfo { get; }
        public ICommand CloseCommand { get; }
        public ICommand OpenProgramFolderCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand ToggleActivityLogCommand { get; }

        // This is what the UI binds to
        public ObservableCollection<LogEntry> LogEntries => _displayedLogEntries;

        public bool ShowActivityLog
        {
            get => _showActivityLog;
            set
            {
                if (_showActivityLog != value)
                {
                    _showActivityLog = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync(); // Save settings when changed
                }
            }
        }

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set
            {
                if (_showDetailedLogs != value)
                {
                    _showDetailedLogs = value;
                    OnPropertyChanged();
                    RefreshDisplayedLogs();
                    _ = SaveSettingsAsync(); // Save settings when changed
                }
            }
        }

        public SettingsViewModel()
        {
            ApplicationInfo = new ApplicationInfo();
            CloseCommand = new RelayCommand(CloseWindow);
            OpenProgramFolderCommand = new RelayCommand(OpenProgramFolder);
            ClearLogsCommand = new RelayCommand(ClearLogs);
            ToggleDetailedLogsCommand = new RelayCommand(ToggleDetailedLogs);
            ToggleActivityLogCommand = new RelayCommand(ToggleActivityLog);

            // Load settings on initialization
            _ = LoadSettingsAsync();
        }

        // Method to set the log entries from the main view model
        public void SetLogEntries(ObservableCollection<LogEntry> logEntries)
        {
            // Disconnect from old collection
            if (_allLogEntries != null)
            {
                _allLogEntries.CollectionChanged -= OnAllLogEntriesChanged;
            }

            _allLogEntries = logEntries ?? new ObservableCollection<LogEntry>();

            // Connect to new collection
            _allLogEntries.CollectionChanged += OnAllLogEntriesChanged;

            // Refresh display
            RefreshDisplayedLogs();
        }

        private void OnAllLogEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // When the main log collection changes, refresh our display
            Dispatcher.UIThread.Post(RefreshDisplayedLogs);
        }

        private void RefreshDisplayedLogs()
        {
            _displayedLogEntries.Clear();

            var logsToShow = _showDetailedLogs
                ? _allLogEntries
                : _allLogEntries.Where(log => log.Level != "DEBUG");

            foreach (var log in logsToShow)
            {
                _displayedLogEntries.Add(log);
            }
        }

        private void ToggleActivityLog()
        {
            ShowActivityLog = !ShowActivityLog;
        }

        private void CloseWindow()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenProgramFolder()
        {
            try
            {
                var programPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(programPath) && Directory.Exists(programPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = programPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening program folder: {ex.Message}");
            }
        }

        private void ClearLogs()
        {
            _allLogEntries?.Clear();
            _displayedLogEntries.Clear();
        }

        private void ToggleDetailedLogs()
        {
            ShowDetailedLogs = !ShowDetailedLogs;
        }

        private string GetSettingsFilePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
            return Path.Combine(appFolderPath, "settings.json");
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                var filePath = GetSettingsFilePath();

                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);

                    if (settings != null)
                    {
                        _showActivityLog = settings.ShowActivityLog;
                        _showDetailedLogs = settings.ShowDetailedLogs;

                        // Notify UI of changes
                        OnPropertyChanged(nameof(ShowActivityLog));
                        OnPropertyChanged(nameof(ShowDetailedLogs));

                        RefreshDisplayedLogs();
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fail - not critical
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");

                // Create the directory if it doesn't exist
                Directory.CreateDirectory(appFolderPath);

                var filePath = GetSettingsFilePath();

                var settings = new AppSettings
                {
                    ShowActivityLog = _showActivityLog,
                    ShowDetailedLogs = _showDetailedLogs,
                    LastUpdated = DateTime.Now
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                // Silently fail - not critical
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}