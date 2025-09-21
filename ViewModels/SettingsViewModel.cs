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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Win32;

namespace AkademiTrack.ViewModels
{
    // Settings data class
    public class AppSettings
    {
        public bool ShowActivityLog { get; set; } = false; // Hidden by default
        public bool ShowDetailedLogs { get; set; } = true;
        public bool StartWithSystem { get; set; } = true; // Default on
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    // Auto-start manager for cross-platform support
    public static class AutoStartManager
    {
        private static readonly string AppName = "AkademiTrack";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return IsAutoStartEnabledWindows();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return IsAutoStartEnabledMacOS();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return IsAutoStartEnabledLinux();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking auto-start status: {ex.Message}");
            }
            return false;
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return SetAutoStartWindows(enable);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return SetAutoStartMacOS(enable);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return SetAutoStartLinux(enable);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting auto-start: {ex.Message}");
            }
            return false;
        }

        // Windows implementation
        private static bool IsAutoStartEnabledWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                var value = key?.GetValue(AppName)?.ToString();
                Debug.WriteLine($"Registry value for {AppName}: {value}");
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking Windows auto-start: {ex.Message}");
                return false;
            }
        }

        private static bool SetAutoStartWindows(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null)
                {
                    Debug.WriteLine("Could not open registry key for writing");
                    return false;
                }

                if (enable)
                {
                    var exePath = GetExecutablePath();
                    Debug.WriteLine($"Setting auto-start with path: {exePath}");

                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        Debug.WriteLine($"Executable not found at: {exePath}");
                        return false;
                    }

                    key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
                    Debug.WriteLine($"Registry entry created: {AppName} = \"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    Debug.WriteLine($"Registry entry deleted: {AppName}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting Windows auto-start: {ex.Message}");
                return false;
            }
        }

        private static string GetExecutablePath()
        {
            try
            {
                // Get the current process executable path
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    Debug.WriteLine($"Using process path: {processPath}");
                    return processPath;
                }

                // Fallback to assembly location
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                Debug.WriteLine($"Assembly location: {assemblyLocation}");

                if (assemblyLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find the executable in the same directory
                    var directory = Path.GetDirectoryName(assemblyLocation);
                    var fileName = Path.GetFileNameWithoutExtension(assemblyLocation);
                    var exePath = Path.Combine(directory!, $"{fileName}.exe");

                    if (File.Exists(exePath))
                    {
                        Debug.WriteLine($"Found executable: {exePath}");
                        return exePath;
                    }

                    // Try common executable names
                    var commonNames = new[] { "AkademiTrack.exe", "AkademiTrack", $"{fileName}.exe" };
                    foreach (var name in commonNames)
                    {
                        var testPath = Path.Combine(directory!, name);
                        if (File.Exists(testPath))
                        {
                            Debug.WriteLine($"Found executable with common name: {testPath}");
                            return testPath;
                        }
                    }
                }

                return assemblyLocation;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting executable path: {ex.Message}");
                return Assembly.GetExecutingAssembly().Location;
            }
        }

        // macOS implementation
        private static bool IsAutoStartEnabledMacOS()
        {
            var plistPath = GetMacOSPlistPath();
            return File.Exists(plistPath);
        }

        private static bool SetAutoStartMacOS(bool enable)
        {
            var plistPath = GetMacOSPlistPath();
            var launchAgentsDir = Path.GetDirectoryName(plistPath);

            if (enable)
            {
                // Create LaunchAgents directory if it doesn't exist
                if (!Directory.Exists(launchAgentsDir))
                {
                    Directory.CreateDirectory(launchAgentsDir!);
                }

                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
                    // For .NET applications on macOS
                    var appDir = Path.GetDirectoryName(exePath);
                    var appName = Path.GetFileNameWithoutExtension(exePath);
                    exePath = Path.Combine(appDir!, appName);
                }

                var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.akademitrack.app</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>";

                File.WriteAllText(plistPath, plistContent);

                // Load the plist
                Process.Start(new ProcessStartInfo
                {
                    FileName = "launchctl",
                    Arguments = $"load \"{plistPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                if (File.Exists(plistPath))
                {
                    // Unload the plist
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "launchctl",
                        Arguments = $"unload \"{plistPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    File.Delete(plistPath);
                }
            }
            return true;
        }

        private static string GetMacOSPlistPath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, "Library", "LaunchAgents", "com.akademitrack.app.plist");
        }

        // Linux implementation
        private static bool IsAutoStartEnabledLinux()
        {
            var desktopFilePath = GetLinuxDesktopFilePath();
            return File.Exists(desktopFilePath);
        }

        private static bool SetAutoStartLinux(bool enable)
        {
            var desktopFilePath = GetLinuxDesktopFilePath();
            var autostartDir = Path.GetDirectoryName(desktopFilePath);

            if (enable)
            {
                // Create autostart directory if it doesn't exist
                if (!Directory.Exists(autostartDir))
                {
                    Directory.CreateDirectory(autostartDir!);
                }

                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
                    // For .NET applications on Linux, use dotnet to run
                    exePath = $"dotnet \"{exePath}\"";
                }
                else
                {
                    exePath = $"\"{exePath}\"";
                }

                var desktopContent = $@"[Desktop Entry]
Type=Application
Name={AppName}
Exec={exePath}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Comment=AkademiTrack automatisk fremmøte registerings program
";

                File.WriteAllText(desktopFilePath, desktopContent);

                // Make the file executable
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{desktopFilePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                }
            }
            return true;
        }

        private static string GetLinuxDesktopFilePath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(homeDir, ".config");
            return Path.Combine(configDir, "autostart", $"{AppName}.desktop");
        }
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
        private bool _startWithSystem = true; // Default on
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
        public ICommand ToggleAutoStartCommand { get; }

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

        public bool StartWithSystem
        {
            get => _startWithSystem;
            set
            {
                if (_startWithSystem != value)
                {
                    Debug.WriteLine($"StartWithSystem changing from {_startWithSystem} to {value}");
                    _startWithSystem = value;
                    OnPropertyChanged();

                    // Apply the auto-start setting immediately
                    var success = AutoStartManager.SetAutoStart(value);
                    Debug.WriteLine($"Auto-start setting result: {success}");

                    if (!success)
                    {
                        // If setting auto-start failed, revert the UI
                        Debug.WriteLine("Auto-start setting failed, reverting UI");
                        _startWithSystem = !value;
                        OnPropertyChanged();

                        // You might want to show an error message to the user here
                        Debug.WriteLine("Failed to update auto-start setting");
                        return;
                    }

                    // Verify the setting was applied
                    var actualState = AutoStartManager.IsAutoStartEnabled();
                    Debug.WriteLine($"Verified auto-start state: {actualState}");

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
            ToggleAutoStartCommand = new RelayCommand(ToggleAutoStart);

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

        private void ToggleAutoStart()
        {
            StartWithSystem = !StartWithSystem;
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
                        _startWithSystem = settings.StartWithSystem;

                        // Notify UI of changes
                        OnPropertyChanged(nameof(ShowActivityLog));
                        OnPropertyChanged(nameof(ShowDetailedLogs));
                        OnPropertyChanged(nameof(StartWithSystem));

                        RefreshDisplayedLogs();

                        // Sync the auto-start setting with the system on load
                        // This ensures the system setting matches our saved preference
                        _ = Task.Run(() => AutoStartManager.SetAutoStart(_startWithSystem));
                    }
                }
                else
                {
                    // First time running - set up auto-start if enabled by default
                    if (_startWithSystem)
                    {
                        _ = Task.Run(() => AutoStartManager.SetAutoStart(true));
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
                    StartWithSystem = _startWithSystem,
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