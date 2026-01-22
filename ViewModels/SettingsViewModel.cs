using AkademiTrack.Services;
using AkademiTrack.Services.Configuration;
using AkademiTrack.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using Velopack;
using Velopack.Sources;
using static AkademiTrack.ExportLogsDialog;

namespace AkademiTrack.ViewModels
{


    public class AppSettings
    {
        public bool ShowActivityLog { get; set; } = false;
        public bool ShowDetailedLogs { get; set; } = true;
        public bool StartWithSystem { get; set; } = true;
        public bool AutoStartAutomation { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool EnableNotifications { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public bool InitialSetupCompleted { get; set; } = false;
    }

    public static class AutoStartManager
    {
        private static readonly string AppName = "AkademiTrack";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return IsAutoStartEnabledWindows();
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return IsAutoStartEnabledMacOS();
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return IsAutoStartEnabledLinux();
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
                    return SetAutoStartWindows(enable);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return SetAutoStartMacOS(enable);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return SetAutoStartLinux(enable);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting auto-start: {ex.Message}");
            }
            return false;
        }

#if WINDOWS
using Microsoft.Win32;
#endif

        private static bool IsAutoStartEnabledWindows()
        {
#if WINDOWS
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        var value = key?.GetValue(AppName)?.ToString();
        return !string.IsNullOrEmpty(value);
    }
    catch { return false; }
#else
            return false; // Not supported on non-Windows
#endif
        }

        private static bool SetAutoStartWindows(bool enable)
        {
#if WINDOWS
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return false;

        if (enable)
        {
            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
            key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
        return true;
    }
    catch { return false; }
#else
            return false;
#endif
        }

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
                if (!Directory.Exists(launchAgentsDir))
                    Directory.CreateDirectory(launchAgentsDir!);

                // Get the path to the .app bundle, not the executable inside
                var exePath = Assembly.GetExecutingAssembly().Location;
                string appBundlePath;

                // If we're inside a .app bundle, get the bundle path
                if (exePath.Contains(".app/Contents/MacOS"))
                {
                    // Extract the .app bundle path
                    var appIndex = exePath.IndexOf(".app/Contents/MacOS");
                    appBundlePath = exePath.Substring(0, appIndex + 4); // Include ".app"
                }
                else
                {
                    // Fallback to the executable itself
                    if (exePath.EndsWith(".dll"))
                    {
                        var appDir = Path.GetDirectoryName(exePath);
                        var appName = Path.GetFileNameWithoutExtension(exePath);
                        appBundlePath = Path.Combine(appDir!, appName);
                    }
                    else
                    {
                        appBundlePath = exePath;
                    }
                }

                Debug.WriteLine($"[AutoStart] App bundle path: {appBundlePath}");

                // Create the LaunchAgent plist with the display name as Label
                var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.CyberBrothers.akademitrack</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/bin/open</string>
        <string>-a</string>
        <string>{appBundlePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>";

                Debug.WriteLine($"[AutoStart] Writing plist to: {plistPath}");
                File.WriteAllText(plistPath, plistContent);

                // Load the LaunchAgent
                try
                {
                    // Unload first if it exists
                    var unloadProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "launchctl",
                        Arguments = $"unload \"{plistPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    unloadProcess?.WaitForExit();

                    var loadProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "launchctl",
                        Arguments = $"load \"{plistPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });

                    if (loadProcess != null)
                    {
                        loadProcess.WaitForExit();
                        var output = loadProcess.StandardOutput.ReadToEnd();
                        var error = loadProcess.StandardError.ReadToEnd();

                        Debug.WriteLine($"[AutoStart] launchctl load exit code: {loadProcess.ExitCode}");
                        if (!string.IsNullOrEmpty(output))
                            Debug.WriteLine($"[AutoStart] Output: {output}");
                        if (!string.IsNullOrEmpty(error))
                            Debug.WriteLine($"[AutoStart] Error: {error}");

                        if (loadProcess.ExitCode == 0)
                        {
                            Debug.WriteLine("[AutoStart] ✓ LaunchAgent loaded successfully");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoStart] Error loading LaunchAgent: {ex.Message}");
                }
            }
            else
            {
                if (File.Exists(plistPath))
                {
                    try
                    {
                        // Unload the LaunchAgent
                        var unloadProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = "launchctl",
                            Arguments = $"unload \"{plistPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        });

                        if (unloadProcess != null)
                        {
                            unloadProcess.WaitForExit();
                            Debug.WriteLine($"[AutoStart] Unload exit code: {unloadProcess.ExitCode}");
                        }

                        // Delete the plist file
                        File.Delete(plistPath);
                        Debug.WriteLine("[AutoStart] ✓ LaunchAgent removed successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AutoStart] Error removing LaunchAgent: {ex.Message}");
                    }
                }
            }
            return true;
        }

        private static string GetMacOSPlistPath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // Use the bundle identifier as filename, but Label inside will be "AkademiTrack"
            return Path.Combine(homeDir, "Library", "LaunchAgents", "com.CyberBrothers.akademitrack.plist");
        }

        private static bool IsAutoStartEnabledLinux()
        {
            return File.Exists(GetLinuxDesktopFilePath());
        }

        private static bool SetAutoStartLinux(bool enable)
        {
            var desktopFilePath = GetLinuxDesktopFilePath();
            var autostartDir = Path.GetDirectoryName(desktopFilePath);

            if (enable)
            {
                if (!Directory.Exists(autostartDir))
                    Directory.CreateDirectory(autostartDir!);

                var exePath = Environment.ProcessPath ?? "dotnet " + Assembly.GetExecutingAssembly().Location;
                var desktopContent = $@"[Desktop Entry]
Type=Application
Name={AppName}
Exec={exePath}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Terminal=false
";
                File.WriteAllText(desktopFilePath, desktopContent);
            }
            else
            {
                if (File.Exists(desktopFilePath))
                    File.Delete(desktopFilePath);
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

    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2) return boolValue ? parts[1] : parts[0];
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StringEqualityConverter : IValueConverter
    {
        public static readonly StringEqualityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string paramString)
                return string.Equals(stringValue, paramString, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToIntConverter : IValueConverter
    {
        public static readonly BoolToIntConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int falseValue) &&
                    int.TryParse(parts[1], out int trueValue))
                {
                    return boolValue ? trueValue : falseValue;
                }
            }
            return 600;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToGridLengthConverter : IValueConverter
    {
        public static readonly BoolToGridLengthConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    var lengthStr = boolValue ? parts[1] : parts[0];
                    if (lengthStr == "0") return new Avalonia.Controls.GridLength(0);
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

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

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

    public class ApplicationInfo
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }
        public string NameAndDescription { get; } 

        public ApplicationInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Name = assembly.GetName().Name ?? "AkademiTrack";
            var version = assembly.GetName().Version;
            Version = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            Description = "Akademiet automatisk fremmøte registerings program";
            NameAndDescription = $"{Name}\n{Description}"; 
        }

        public override string ToString() => $"{Name} v{Version}\n{Description}";
    }

    public static class SafeSettingsLoader
    {
        public static string GetSettingsFilePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
            return Path.Combine(appFolderPath, "settings.json");
        }

        public static async Task<AppSettings> LoadSettingsWithAutoRepairAsync()
        {
            var filePath = GetSettingsFilePath();
            var defaultSettings = new AppSettings();

            if (!File.Exists(filePath))
            {
                await SaveSettingsSafelyAsync(defaultSettings);
                return defaultSettings;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? defaultSettings;
            }
            catch
            {
                return defaultSettings;  
            }
        }

        public static async Task<bool> SaveSettingsSafelyAsync(AppSettings settings)
        {
            try
            {
                var appDataPath   = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
                Directory.CreateDirectory(appFolderPath);

                var filePath = GetSettingsFilePath();
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch { return false; }
        }
    }

    public class SettingsViewModel : INotifyPropertyChanged
    {
        private bool _showActivityLog = false;
        private bool _showDetailedLogs = true;
        private bool _startWithSystem = true;
        private bool _autoStartAutomation = true;

        private ObservableCollection<LogEntry> _allLogEntries = new();
        private ObservableCollection<LogEntry> _displayedLogEntries = new();
        private string _loginEmail = "";
        private SecureString? _loginPasswordSecure;
        private string _schoolName = "";

        // Save button state tracking
        private bool _hasUnsavedLoginChanges = false;
        private string _originalLoginEmail = "";
        private string _originalSchoolName = "";
        private string _originalLoginPassword = "";

        public ObservableCollection<string> Schools { get; }

        private string _updateStatus = "Klikk for å sjekke etter oppdateringer";
        private bool _isCheckingForUpdates = false;
        private bool _updateAvailable = false;
        private string _availableVersion = "";
        private bool _isDeleting = false;
        private bool _startMinimized = false;

        private bool _enableNotifications = true;
        public bool EnableNotifications
        {
            get => _enableNotifications;
            set
            {
                if (_enableNotifications != value)
                {
                    _enableNotifications = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync();
                }
            }
        }

        private SchoolHoursSettings _schoolHours = new SchoolHoursSettings();

        // Monday
        private bool _mondayEnabled = true;
        private TimeSpan _mondayStart = new TimeSpan(9, 0, 0);
        private TimeSpan _mondayEnd = new TimeSpan(15, 15, 0);

        private const int LOG_RETENTION_DAYS = 7;

        public bool MondayEnabled
        {
            get => _mondayEnabled;
            set { if (_mondayEnabled != value) { _mondayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan MondayStart
        {
            get => _mondayStart;
            set { if (_mondayStart != value) { _mondayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan MondayEnd
        {
            get => _mondayEnd;
            set { if (_mondayEnd != value) { _mondayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Tuesday
        private bool _tuesdayEnabled = true;
        private TimeSpan _tuesdayStart = new TimeSpan(8, 15, 0);
        private TimeSpan _tuesdayEnd = new TimeSpan(15, 15, 0);

        public bool TuesdayEnabled
        {
            get => _tuesdayEnabled;
            set { if (_tuesdayEnabled != value) { _tuesdayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan TuesdayStart
        {
            get => _tuesdayStart;
            set { if (_tuesdayStart != value) { _tuesdayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan TuesdayEnd
        {
            get => _tuesdayEnd;
            set { if (_tuesdayEnd != value) { _tuesdayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Wednesday
        private bool _wednesdayEnabled = true;
        private TimeSpan _wednesdayStart = new TimeSpan(8, 15, 0);
        private TimeSpan _wednesdayEnd = new TimeSpan(15, 15, 0);

        public bool WednesdayEnabled
        {
            get => _wednesdayEnabled;
            set { if (_wednesdayEnabled != value) { _wednesdayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan WednesdayStart
        {
            get => _wednesdayStart;
            set { if (_wednesdayStart != value) { _wednesdayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan WednesdayEnd
        {
            get => _wednesdayEnd;
            set { if (_wednesdayEnd != value) { _wednesdayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Thursday
        private bool _thursdayEnabled = true;
        private TimeSpan _thursdayStart = new TimeSpan(8, 15, 0);
        private TimeSpan _thursdayEnd = new TimeSpan(15, 15, 0);

        public bool ThursdayEnabled
        {
            get => _thursdayEnabled;
            set { if (_thursdayEnabled != value) { _thursdayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan ThursdayStart
        {
            get => _thursdayStart;
            set { if (_thursdayStart != value) { _thursdayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan ThursdayEnd
        {
            get => _thursdayEnd;
            set { if (_thursdayEnd != value) { _thursdayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Friday
        private bool _fridayEnabled = true;
        private TimeSpan _fridayStart = new TimeSpan(8, 15, 0);
        private TimeSpan _fridayEnd = new TimeSpan(15, 15, 0);

        public bool FridayEnabled
        {
            get => _fridayEnabled;
            set { if (_fridayEnabled != value) { _fridayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan FridayStart
        {
            get => _fridayStart;
            set { if (_fridayStart != value) { _fridayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan FridayEnd
        {
            get => _fridayEnd;
            set { if (_fridayEnd != value) { _fridayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Saturday
        private bool _saturdayEnabled = false;
        private TimeSpan _saturdayStart = new TimeSpan(8, 0, 0);
        private TimeSpan _saturdayEnd = new TimeSpan(15, 0, 0);

        public bool SaturdayEnabled
        {
            get => _saturdayEnabled;
            set { if (_saturdayEnabled != value) { _saturdayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan SaturdayStart
        {
            get => _saturdayStart;
            set { if (_saturdayStart != value) { _saturdayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan SaturdayEnd
        {
            get => _saturdayEnd;
            set { if (_saturdayEnd != value) { _saturdayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        // Sunday
        private bool _sundayEnabled = false;
        private TimeSpan _sundayStart = new TimeSpan(8, 0, 0);
        private TimeSpan _sundayEnd = new TimeSpan(15, 0, 0);

        public bool SundayEnabled
        {
            get => _sundayEnabled;
            set { if (_sundayEnabled != value) { _sundayEnabled = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan SundayStart
        {
            get => _sundayStart;
            set { if (_sundayStart != value) { _sundayStart = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        public TimeSpan SundayEnd
        {
            get => _sundayEnd;
            set { if (_sundayEnd != value) { _sundayEnd = value; OnPropertyChanged(); SaveSchoolHours(); } }
        }

        private bool _isRunningDiagnostics;
        public bool IsRunningDiagnostics
        {
            get => _isRunningDiagnostics;
            set
            {
                if (_isRunningDiagnostics != value)
                {
                    _isRunningDiagnostics = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _diagnosticsCompleted;
        public bool DiagnosticsCompleted
        {
            get => _diagnosticsCompleted;
            set
            {
                if (_diagnosticsCompleted != value)
                {
                    _diagnosticsCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<HealthCheckResult> _healthCheckResults = new();
        public ObservableCollection<HealthCheckResult> HealthCheckResults
        {
            get => _healthCheckResults;
            set
            {
                if (_healthCheckResults != value)
                {
                    _healthCheckResults = value;
                    OnPropertyChanged(nameof(HealthCheckResults));
                }
            }
        }

        private DateTime _lastDiagnosticsRun = DateTime.MinValue;
        private const int DIAGNOSTICS_COOLDOWN_SECONDS = 5;


        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (_startMinimized != value)
                {
                    _startMinimized = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync();

                    _ = RefreshMainWindowSettings();
                }
            }
        }

        private async Task RefreshMainWindowSettings()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var mainWindow = desktop.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            await mainWindow.RefreshSettingsAsync();
                            Debug.WriteLine("✓ MainWindow settings cache refreshed");
                        }
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing MainWindow settings: {ex.Message}");
            }
        }

        public bool IsDeleting
        {
            get => _isDeleting;
            set { if (_isDeleting != value) { _isDeleting = value; OnPropertyChanged(); } }
        }

        private bool _isExporting = false;
        private string _lastExportPath = "";

        public bool IsExporting
        {
            get => _isExporting;
            set { if (_isExporting != value) { _isExporting = value; OnPropertyChanged(); } }
        }

        public string LastExportPath
        {
            get => _lastExportPath;
            set { if (_lastExportPath != value) { _lastExportPath = value; OnPropertyChanged(); } }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;
        public event EventHandler? SchoolHoursChanged;

        public ApplicationInfo ApplicationInfo { get; }
        public ObservableCollection<LogEntry> LogEntries => _displayedLogEntries;

        public ICommand CloseCommand { get; }
        public ICommand BackToDashboardCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand ToggleActivityLogCommand { get; }
        public ICommand ExportLogsCommand { get; }
        public ICommand ToggleAutoStartCommand { get; }
        public ICommand ToggleAutoStartAutomationCommand { get; }
        public ICommand OpenWebsiteCommand { get; }
        public ICommand OpenEmailCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand DownloadAndInstallUpdateCommand { get; }
        public ICommand DeleteLocalDataCommand { get; }
        public ICommand DeleteAccountCompletelyCommand { get; }
        public ICommand ClearSecureStorageCommand { get; }
        public ICommand ExportDataAsJsonCommand { get; }
        public ICommand ExportDataAsCsvCommand { get; }
        public ICommand ToggleStartMinimizedCommand { get; }
        public ICommand ToggleNotificationsCommand { get; }
        public ICommand SaveLoginCredentialsCommand { get; }

        public ICommand ResetSchoolHoursCommand { get; }
        public ICommand RunDiagnosticsCommand { get; }

        public ICommand UninstallCompletelyCommand { get; }


        public string UpdateStatus
        {
            get => _updateStatus;
            set { if (_updateStatus != value) { _updateStatus = value; OnPropertyChanged(); } }
        }

        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set { if (_isCheckingForUpdates != value) { _isCheckingForUpdates = value; OnPropertyChanged(); } }
        }

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set
            {
                if (_updateAvailable != value)
                {
                    _updateAvailable = value;
                    OnPropertyChanged();

                    (DownloadAndInstallUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string AvailableVersion
        {
            get => _availableVersion;
            set { if (_availableVersion != value) { _availableVersion = value; OnPropertyChanged(); } }
        }

        public bool AutoStartAutomation
        {
            get => _autoStartAutomation;
            set
            {
                if (_autoStartAutomation != value)
                {
                    _autoStartAutomation = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync();

                    _ = NotifyMainWindowAutoStartChanged();
                }
            }
        }

        private void ResetSchoolHoursToDefaults()
        {
            var defaults = SchoolHoursSettings.GetDefault();

            MondayEnabled = defaults.WeekSchedule[DayOfWeek.Monday].IsEnabled;
            MondayStart = defaults.WeekSchedule[DayOfWeek.Monday].StartTime;
            MondayEnd = defaults.WeekSchedule[DayOfWeek.Monday].EndTime;

            TuesdayEnabled = defaults.WeekSchedule[DayOfWeek.Tuesday].IsEnabled;
            TuesdayStart = defaults.WeekSchedule[DayOfWeek.Tuesday].StartTime;
            TuesdayEnd = defaults.WeekSchedule[DayOfWeek.Tuesday].EndTime;

            WednesdayEnabled = defaults.WeekSchedule[DayOfWeek.Wednesday].IsEnabled;
            WednesdayStart = defaults.WeekSchedule[DayOfWeek.Wednesday].StartTime;
            WednesdayEnd = defaults.WeekSchedule[DayOfWeek.Wednesday].EndTime;

            ThursdayEnabled = defaults.WeekSchedule[DayOfWeek.Thursday].IsEnabled;
            ThursdayStart = defaults.WeekSchedule[DayOfWeek.Thursday].StartTime;
            ThursdayEnd = defaults.WeekSchedule[DayOfWeek.Thursday].EndTime;

            FridayEnabled = defaults.WeekSchedule[DayOfWeek.Friday].IsEnabled;
            FridayStart = defaults.WeekSchedule[DayOfWeek.Friday].StartTime;
            FridayEnd = defaults.WeekSchedule[DayOfWeek.Friday].EndTime;

            SaturdayEnabled = defaults.WeekSchedule[DayOfWeek.Saturday].IsEnabled;
            SaturdayStart = defaults.WeekSchedule[DayOfWeek.Saturday].StartTime;
            SaturdayEnd = defaults.WeekSchedule[DayOfWeek.Saturday].EndTime;

            SundayEnabled = defaults.WeekSchedule[DayOfWeek.Sunday].IsEnabled;
            SundayStart = defaults.WeekSchedule[DayOfWeek.Sunday].StartTime;
            SundayEnd = defaults.WeekSchedule[DayOfWeek.Sunday].EndTime;

            // Ensure cache is invalidated immediately after reset
            SchoolTimeChecker.InvalidateSchoolHoursCache();
            
            // Notify that school hours have changed
            SchoolHoursChanged?.Invoke(this, EventArgs.Empty);
            
            Debug.WriteLine("✓ School hours reset to defaults and cache invalidated");
        }

        private async Task LoadSchoolHoursAsync()
        {
            try
            {
                var filePath = GetSchoolHoursFilePath();

                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _schoolHours = JsonSerializer.Deserialize<SchoolHoursSettings>(json) ?? new SchoolHoursSettings();
                }
                else
                {
                    _schoolHours = SchoolHoursSettings.GetDefault();
                }

                LoadDayScheduleToUI(DayOfWeek.Monday, ref _mondayEnabled, ref _mondayStart, ref _mondayEnd);
                LoadDayScheduleToUI(DayOfWeek.Tuesday, ref _tuesdayEnabled, ref _tuesdayStart, ref _tuesdayEnd);
                LoadDayScheduleToUI(DayOfWeek.Wednesday, ref _wednesdayEnabled, ref _wednesdayStart, ref _wednesdayEnd);
                LoadDayScheduleToUI(DayOfWeek.Thursday, ref _thursdayEnabled, ref _thursdayStart, ref _thursdayEnd);
                LoadDayScheduleToUI(DayOfWeek.Friday, ref _fridayEnabled, ref _fridayStart, ref _fridayEnd);
                LoadDayScheduleToUI(DayOfWeek.Saturday, ref _saturdayEnabled, ref _saturdayStart, ref _saturdayEnd);
                LoadDayScheduleToUI(DayOfWeek.Sunday, ref _sundayEnabled, ref _sundayStart, ref _sundayEnd);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(MondayEnabled));
                    OnPropertyChanged(nameof(MondayStart));
                    OnPropertyChanged(nameof(MondayEnd));
                    OnPropertyChanged(nameof(TuesdayEnabled));
                    OnPropertyChanged(nameof(TuesdayStart));
                    OnPropertyChanged(nameof(TuesdayEnd));
                    OnPropertyChanged(nameof(WednesdayEnabled));
                    OnPropertyChanged(nameof(WednesdayStart));
                    OnPropertyChanged(nameof(WednesdayEnd));
                    OnPropertyChanged(nameof(ThursdayEnabled));
                    OnPropertyChanged(nameof(ThursdayStart));
                    OnPropertyChanged(nameof(ThursdayEnd));
                    OnPropertyChanged(nameof(FridayEnabled));
                    OnPropertyChanged(nameof(FridayStart));
                    OnPropertyChanged(nameof(FridayEnd));
                    OnPropertyChanged(nameof(SaturdayEnabled));
                    OnPropertyChanged(nameof(SaturdayStart));
                    OnPropertyChanged(nameof(SaturdayEnd));
                    OnPropertyChanged(nameof(SundayEnabled));
                    OnPropertyChanged(nameof(SundayStart));
                    OnPropertyChanged(nameof(SundayEnd));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading school hours: {ex.Message}");
                _schoolHours = SchoolHoursSettings.GetDefault();
            }
        }

        private void LoadDayScheduleToUI(DayOfWeek day, ref bool enabled, ref TimeSpan start, ref TimeSpan end)
        {
            if (_schoolHours.WeekSchedule.ContainsKey(day))
            {
                var schedule = _schoolHours.WeekSchedule[day];
                enabled = schedule.IsEnabled;
                start = schedule.StartTime;
                end = schedule.EndTime;
            }
        }

        private void SaveSchoolHours()
        {
            _ = SaveSchoolHoursAsync();
        }

        private async Task SaveSchoolHoursAsync()
        {
            try
            {
                _schoolHours.WeekSchedule[DayOfWeek.Monday] = new DaySchedule(_mondayEnabled, _mondayStart, _mondayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Tuesday] = new DaySchedule(_tuesdayEnabled, _tuesdayStart, _tuesdayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Wednesday] = new DaySchedule(_wednesdayEnabled, _wednesdayStart, _wednesdayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Thursday] = new DaySchedule(_thursdayEnabled, _thursdayStart, _thursdayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Friday] = new DaySchedule(_fridayEnabled, _fridayStart, _fridayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Saturday] = new DaySchedule(_saturdayEnabled, _saturdayStart, _saturdayEnd);
                _schoolHours.WeekSchedule[DayOfWeek.Sunday] = new DaySchedule(_sundayEnabled, _sundayStart, _sundayEnd);

                var filePath = GetSchoolHoursFilePath();
                var json = JsonSerializer.Serialize(_schoolHours, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);

                // Invalidate the cache so SchoolTimeChecker picks up the new times immediately
                SchoolTimeChecker.InvalidateSchoolHoursCache();

                // Notify that school hours have changed
                SchoolHoursChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine("✓ School hours saved successfully and cache invalidated");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving school hours: {ex.Message}");
            }
        }

        private string GetSchoolHoursFilePath()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "school_hours.json");
        }

        private async Task NotifyMainWindowAutoStartChanged()
        {
            try
            {
                await Task.Delay(100);

                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var mainWindow = desktop.MainWindow as MainWindow;
                        if (mainWindow?.DataContext is RefactoredMainWindowViewModel viewModel)
                        {
                            Debug.WriteLine("[SETTINGS] Auto-start setting changed - triggering immediate refresh");
                            await viewModel.RefreshAutoStartStatusAsync();
                        }
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
                
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error notifying MainWindow of auto-start change: {ex.Message}");
            }
        }

        public bool HasUnsavedLoginChanges
        {
            get => _hasUnsavedLoginChanges;
            set
            {
                if (_hasUnsavedLoginChanges != value)
                {
                    _hasUnsavedLoginChanges = value;
                    OnPropertyChanged();
                    (SaveLoginCredentialsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string LoginEmail
        {
            get => _loginEmail;
            set 
            { 
                if (_loginEmail != value) 
                { 
                    _loginEmail = value; 
                    OnPropertyChanged(); 
                    CheckForLoginChanges();
                    _ = SaveSettingsAsync(); 
                } 
            }
        }

        public string LoginPassword
        {
            get => SecureStringToString(_loginPasswordSecure);
            set
            {
                var currentValue = SecureStringToString(_loginPasswordSecure);
                if (currentValue != value)
                {
                    _loginPasswordSecure?.Dispose();
                    _loginPasswordSecure = StringToSecureString(value);
                    OnPropertyChanged();
                    CheckForLoginChanges();
                    _ = SaveSettingsAsync();
                }
            }
        }

        public string SchoolName
        {
            get => _schoolName;
            set 
            { 
                if (_schoolName != value) 
                { 
                    _schoolName = value; 
                    OnPropertyChanged(); 
                    CheckForLoginChanges();
                    _ = SaveSettingsAsync(); 
                } 
            }
        }

        public bool ShowActivityLog
        {
            get => _showActivityLog;
            set { if (_showActivityLog != value) { _showActivityLog = value; OnPropertyChanged(); _ = SaveSettingsAsync(); } }
        }

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set { if (_showDetailedLogs != value) { _showDetailedLogs = value; OnPropertyChanged(); RefreshDisplayedLogs(); _ = SaveSettingsAsync(); } }
        }

        public bool StartWithSystem
        {
            get => _startWithSystem;
            set { if (_startWithSystem != value) { _startWithSystem = value; OnPropertyChanged(); AutoStartManager.SetAutoStart(value); _ = SaveSettingsAsync(); } }
        }

        public SettingsViewModel()
        {
            ApplicationInfo = new ApplicationInfo();
            
            Schools = new ObservableCollection<string>
            {
                 "Akademiet Drammen AS",
                /* "Akademiet Fagskole AS",
                "Akademiet Kristiansand AS",
                "Akademiet Næringsakademi AS",
                "Akademiet Oslo AS",
                "Akademiet Stavanger AS",
                "Akademiet Trondheim AS",
                "Akademiet Ålesund AS",
                "Akademiet Bergen AS",
                "Akademiet Bodø AS",
                "Akademiet Tromsø AS",
                "Akademiet Haugesund AS",
                "Akademiet Fredrikstad AS",
                "Akademiet Tønsberg AS",
                "Akademiet Lillehammer AS",
                "Akademiet Hamar AS",
                "Akademiet Moss AS",
                "Akademiet Sarpsborg AS",
                "Akademiet Sandefjord AS",
                "Akademiet Skien AS" */
            };
            
            CloseCommand = new RelayCommand(CloseWindow);
            BackToDashboardCommand = new RelayCommand(BackToDashboard);  
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            ClearLogsCommand = new RelayCommand(ClearLogs);
            ToggleDetailedLogsCommand = new RelayCommand(ToggleDetailedLogs);
            ToggleActivityLogCommand = new RelayCommand(ToggleActivityLog);
            ExportLogsCommand = new RelayCommand(async () => await ExportLogsAsync());
            ToggleAutoStartCommand = new RelayCommand(ToggleAutoStart);
            ToggleAutoStartAutomationCommand = new RelayCommand(ToggleAutoStartAutomation);
            OpenWebsiteCommand = new RelayCommand(OpenWebsite);
            OpenEmailCommand = new RelayCommand(OpenEmail);
            CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            DownloadAndInstallUpdateCommand = new RelayCommand(async () => await DownloadAndInstallUpdateAsync(), () => UpdateAvailable);
            DeleteLocalDataCommand = new RelayCommand(async () => await DeleteLocalDataAsync());
            DeleteAccountCompletelyCommand = new RelayCommand(async () => await DeleteAccountCompletelyAsync());
            ClearSecureStorageCommand = new RelayCommand(async () => await ClearSecureStorageAsync());
            UninstallCompletelyCommand = new RelayCommand(async () => await UninstallCompletelyAsync());
            ExportDataAsJsonCommand = new RelayCommand(async () => await ExportDataAsync("json"));
            ExportDataAsCsvCommand = new RelayCommand(async () => await ExportDataAsync("csv"));
            ToggleStartMinimizedCommand = new RelayCommand(ToggleStartMinimized);
            ToggleNotificationsCommand = new RelayCommand(ToggleNotifications);
            ResetSchoolHoursCommand = new RelayCommand(ResetSchoolHoursToDefaults);
            RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync);
            SaveLoginCredentialsCommand = new RelayCommand(SaveLoginCredentials, () => HasUnsavedLoginChanges);
    
              _ = CleanOldLogsAsync();

            _ = LoadSchoolHoursAsync();

            _ = LoadSettingsAsync();

            Services.ResourceMonitor.Instance.StartMonitoring();
            Debug.WriteLine("✓ Resource monitoring started for Settings window");
        }

        private async Task CleanOldLogsAsync()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-LOG_RETENTION_DAYS);
                var logsToRemove = _allLogEntries
                    .Where(log => log.Timestamp < cutoffDate)
                    .ToList();

                if (logsToRemove.Any())
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var log in logsToRemove)
                        {
                            _allLogEntries.Remove(log);
                        }
                    });

                    Debug.WriteLine($"✓ Cleaned {logsToRemove.Count} old logs (older than {LOG_RETENTION_DAYS} days)");
                    
                    // Optionally notify user
                    /* if (EnableNotifications && logsToRemove.Count > 100)
                    {
                        NativeNotificationService.Show(
                            "Logger ryddet",
                            $"Fjernet {logsToRemove.Count} gamle loggoppføringer",
                            "INFO"
                        );
                    }
                    */
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning old logs: {ex.Message}");
            }
        }

        private void ToggleNotifications() => EnableNotifications = !EnableNotifications;


        public void ConnectToMainViewModel(RefactoredMainWindowViewModel mainViewModel)
        {
            if (mainViewModel?.LogEntries != null)
            {
                SetLogEntries(mainViewModel.LogEntries);
                Debug.WriteLine($"✓ Connected to RefactoredMainViewModel - {mainViewModel.LogEntries.Count} log entries");

                // Force refresh the displayed logs
                RefreshDisplayedLogs();
            }
            else
            {
                Debug.WriteLine("⚠️ RefactoredMainViewModel or LogEntries is null");
            }
        }

        private void BackToDashboard()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ToggleTheme()
        {
            Services.ThemeManager.Instance.ToggleTheme();
            Debug.WriteLine($"Theme changed to {(Services.ThemeManager.Instance.IsDarkMode ? "dark" : "light")} mode");
        }

        private async Task RunDiagnosticsAsync()
        {
            if (IsRunningDiagnostics)
            {
                //Debug.WriteLine("Diagnostics already running - ignoring duplicate request");
                return;
            }

            var timeSinceLastRun = (DateTime.Now - _lastDiagnosticsRun).TotalSeconds;
            if (timeSinceLastRun < DIAGNOSTICS_COOLDOWN_SECONDS)
            {
                var waitTime = DIAGNOSTICS_COOLDOWN_SECONDS - (int)timeSinceLastRun;
                //Debug.WriteLine($"Diagnostics on cooldown - wait {waitTime} more seconds");
                return;
            }

            IsRunningDiagnostics = true;
            DiagnosticsCompleted = false;
            HealthCheckResults.Clear();

            _lastDiagnosticsRun = DateTime.Now;

            // (RunDiagnosticsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();

            try
            {
                var healthCheck = new SystemHealthCheck();
                var results = await healthCheck.RunFullHealthCheckAsync();

                foreach (var result in results)
                {
                    HealthCheckResults.Add(result);
                }

                DiagnosticsCompleted = true;
                //Debug.WriteLine("✓ Diagnostics completed successfully");
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Diagnostics error: {ex.Message}");

                HealthCheckResults.Add(new HealthCheckResult
                {
                    ComponentName = "Diagnose",
                    Status = HealthStatus.Error,
                    Message = "Feil under diagnose",
                    Details = ex.Message
                });

                DiagnosticsCompleted = true;
            }
            finally
            {
                IsRunningDiagnostics = false;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // (RunDiagnosticsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                });
            }
        }

        private void ToggleStartMinimized() => StartMinimized = !StartMinimized;

        private async Task ExportDataAsync(string format)
        {
            if (IsExporting) return;

            try
            {
                IsExporting = true;
                Debug.WriteLine($"=== STARTING LOCAL DATA EXPORT ({format.ToUpper()}) ===");

                string userEmail = LoginEmail;

                if (string.IsNullOrEmpty(userEmail))
                {
                    await ShowErrorDialog(
                        "Eksport feilet",
                        "Kunne ikke finne bruker-e-post. Vennligst logg inn på nytt."
                    );
                    return;
                }

                var proceed = await ShowConfirmationDialog(
                    "Eksporter lokal data",
                    $"Dette vil eksportere all LOKAL data som {format.ToUpper()}.\n\n" +
                    "⚠️ VIKTIG:\n" +
                    "Dette eksporterer data lagret på din datamaskin:\n" +
                    "• Appinnstillinger\n" +
                    "• Aktiveringsinformasjon\n" +
                    "• Lokale filer og cookies\n\n" +
                    "Vil du fortsette med lokal eksport?",
                    false
                );

                if (!proceed)
                {
                    Debug.WriteLine("User cancelled export");
                    return;
                }

                Debug.WriteLine("Collecting local user data...");
                var exportData = await AkademiTrack.Services.DataExportService.CollectAllDataAsync(
                    userEmail,
                    ApplicationInfo.Version
                );

                Debug.WriteLine($"Local data collected successfully");
                Debug.WriteLine($"- Local files: {exportData.Local.Files.Count}");

                // Export based on format
                string filePath;
                if (format.ToLower() == "json")
                {
                    filePath = await AkademiTrack.Services.DataExportService.ExportAsJsonAsync(exportData);
                }
                else
                {
                    filePath = await AkademiTrack.Services.DataExportService.ExportAsCsvAsync(exportData);
                }

                LastExportPath = filePath;
                Debug.WriteLine($"✓ Export completed: {filePath}");

                var openFolder = await ShowConfirmationDialog(
                    "Lokal eksport fullført",
                    $"Din LOKALE data er eksportert til:\n\n{filePath}\n\n" +
                    "Vil du åpne mappen der filen er lagret?",
                    false
                );

                if (openFolder)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = directory,
                                UseShellExecute = true
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error opening folder: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"=== EXPORT FAILED ===");
                Debug.WriteLine($"Error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                await ShowErrorDialog(
                    "Eksport feilet",
                    $"Kunne ikke eksportere lokal data:\n\n{ex.Message}\n\n" +
                    "Vennligst prøv igjen eller kontakt support hvis problemet vedvarer."
                );
            }
            finally
            {
                IsExporting = false;
                Debug.WriteLine("=== EXPORT PROCESS ENDED ===");
            }
        }

        private async Task DeleteLocalDataAsync()
        {
            if (IsDeleting) return;

            try
            {
                var result = await ShowConfirmationDialog(
                    "Slett lokal data",
                    "Er du sikker på at du vil slette all lokal data?\n\n" +
                    "Dette vil fjerne:\n" +
                    "• Lagrede innloggingsopplysninger (fra sikker lagring)\n" +
                    "• Cookies og tokens\n" +
                    "• Appinnstillinger\n" +
                    "• Cache-data\n\n" +
                    "Programmet starter på nytt etter sletting.",
                    false
                );

                if (!result) return;

                IsDeleting = true;
                Debug.WriteLine("=== DELETING LOCAL DATA ===");

                // Track user deletion in analytics BEFORE deleting any data
                // This ensures we use the current user_id and session_id
                try
                {
                    Debug.WriteLine("Tracking user deletion in analytics...");
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    
                    // Make sure we have a valid session before tracking
                    var currentUserId = analyticsService.GetPersistentUserId();
                    var currentSessionId = analyticsService.GetSessionId();
                    
                    Debug.WriteLine($"Current User ID: {currentUserId}");
                    Debug.WriteLine($"Current Session ID: {currentSessionId}");
                    
                    if (!string.IsNullOrEmpty(currentSessionId))
                    {
                        await analyticsService.TrackUserDeletedDataAsync();
                        Debug.WriteLine("✓ User deletion tracked in analytics");
                        
                        // Give it time to send the request
                        await Task.Delay(2000);
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ No active session - cannot track deletion");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Could not track deletion in analytics: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }

                Debug.WriteLine("Performing comprehensive secure storage cleanup...");
                await SecureCredentialStorage.ClearAllDataAsync();
                Debug.WriteLine("✓ Complete secure storage cleanup finished");

                // Then delete local files
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                if (Directory.Exists(appDataDir))
                {
                    try
                    {
                        Directory.Delete(appDataDir, true);
                        Debug.WriteLine("✓ AkademiTrack directory deleted");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Could not delete directory: {ex.Message}");
                    }
                }

                Debug.WriteLine("=== LOCAL DATA DELETED SUCCESSFULLY ===");

                CloseRequested?.Invoke(this, EventArgs.Empty);
                await Task.Delay(300);
                RestartApplication();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                await ShowErrorDialog("Feil ved sletting", $"Kunne ikke slette all data: {ex.Message}");
            }
            finally
            {
                IsDeleting = false;
            }
        }

        private void RestartApplication()
        {
            try
            {
                Debug.WriteLine("=== RESTARTING APPLICATION ===");

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                Debug.WriteLine($"Executable path: {exePath}");

                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath)
                    });

                    Debug.WriteLine("New instance started, shutting down current instance...");
                }
                else
                {
                    Debug.WriteLine("Could not find executable path");
                }

                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error restarting application: {ex.Message}");
                Debug.WriteLine($"Falling back to simple shutdown...");

                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
            }
        }

        private async Task ClearSecureStorageAsync()
        {
            if (IsDeleting) return;

            try
            {
                var result = await ShowConfirmationDialog(
                    "Rens sikker lagring",
                    "Dette vil fjerne ALL AkademiTrack-data fra:\n\n" +
                    "• macOS Keychain\n" +
                    "• Windows Credential Manager\n" +
                    "• Linux Secret Service\n" +
                    "• Alle backup-filer\n\n" +
                    "Dette inkluderer innloggingsdata, cookies og tokens.\n\n" +
                    "Er du sikker?",
                    false
                );

                if (!result) return;

                IsDeleting = true;
                Debug.WriteLine("=== CLEARING SECURE STORAGE ===");

                await SecureCredentialStorage.ClearAllDataAsync();

                Debug.WriteLine("✓ Secure storage cleanup complete");

                await ShowInfoDialog(
                    "Sikker lagring renset",
                    "All AkademiTrack-data er fjernet fra sikker lagring.\n\n" +
                    "Du må logge inn på nytt neste gang du starter appen."
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing secure storage: {ex.Message}");
                await ShowErrorDialog("Feil", $"Kunne ikke rense sikker lagring: {ex.Message}");
            }
            finally
            {
                IsDeleting = false;
            }
        }

        private async Task DeleteAccountCompletelyAsync()
        {
            if (IsDeleting) return;

            try
            {
                var result = await ShowConfirmationDialog(
                    "⚠️ SLETT KONTO PERMANENT",
                    "ADVARSEL: Dette sletter kontoen din permanent!\n\n" +
                    "• All lokal data\n" +
                    "• All brukerdata\n\n" +
                    "Dette kan IKKE angres!\n\n" +
                    "Er du sikker?",
                    true
                );

                if (!result) return;

                var doubleCheck = await ShowConfirmationDialog(
                    "Siste bekreftelse",
                    "Dette er din siste sjanse!\n\n" +
                    "Trykk Ja for å slette kontoen permanent,\n" +
                    "eller Avbryt for å beholde den.",
                    true
                );

                if (!doubleCheck) return;

                IsDeleting = true;
                Debug.WriteLine("=== DELETING ACCOUNT COMPLETELY ===");

                Debug.WriteLine("Performing comprehensive secure storage cleanup...");
                await SecureCredentialStorage.ClearAllDataAsync();
                Debug.WriteLine("✓ Complete secure storage cleanup finished");

                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                if (Directory.Exists(appDataDir))
                {
                    try
                    {
                        Directory.Delete(appDataDir, true);
                        Debug.WriteLine("✓ AkademiTrack directory deleted");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Could not delete directory: {ex.Message}");
                    }
                }

                Debug.WriteLine("=== ACCOUNT COMPLETELY DELETED ===");

                CloseRequested?.Invoke(this, EventArgs.Empty);
                await Task.Delay(300);

                RestartApplication();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                await ShowErrorDialog("Feil", $"Det oppstod en feil: {ex.Message}");
            }
            finally
            {
                IsDeleting = false;
            }
        }

        private async Task UninstallCompletelyAsync()
        {
            if (IsDeleting) return;

            try
            {
                var result = await ShowConfirmationDialog(
                    "AVINSTALLER AKADEMITRACK FULLSTENDIG",
                    "ADVARSEL: Dette vil:\n\n" +
                    "• Slette ALL data (innstillinger, cookies, logger)\n" +
                    "• Slette ALL brukerinformasjon permanent\n" +
                    "• Fjerne applikasjonen fra datamaskinen\n" +
                    "• Fjerne autostart-oppsettet\n\n" +
                    "Dette kan IKKE angres!\n\n" +
                    "Er du helt sikker?",
                    true
                );

                if (!result) return;

                var doubleCheck = await ShowConfirmationDialog(
                    "Siste bekreftelse",
                    "Dette er din absolutt siste sjanse!\n\n" +
                    "Applikasjonen vil bli fullstendig fjernet.\n\n" +
                    "Trykk Ja for å avinstallere permanent,\n" +
                    "eller Avbryt for å beholde applikasjonen.",
                    true
                );

                if (!doubleCheck) return;

                IsDeleting = true;
                Debug.WriteLine("=== UNINSTALLING AKADEMITRACK COMPLETELY ===");

                // 1. Track uninstall in analytics (before deleting data)
                try
                {
                    Debug.WriteLine("Tracking user deletion in analytics...");
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    
                    // Make sure we have a valid session before tracking
                    var currentUserId = analyticsService.GetPersistentUserId();
                    var currentSessionId = analyticsService.GetSessionId();
                    
                    Debug.WriteLine($"Current User ID: {currentUserId}");
                    Debug.WriteLine($"Current Session ID: {currentSessionId}");
                    
                    if (!string.IsNullOrEmpty(currentSessionId))
                    {
                        await analyticsService.TrackUserDeletedDataAsync();
                        Debug.WriteLine("✓ User deletion tracked in analytics");
                        
                        // Give it time to send the request
                        await Task.Delay(2000);
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ No active session - cannot track deletion");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Could not track deletion in analytics: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }

                // 2. Remove from auto-start
                try
                {
                    Debug.WriteLine("Removing from auto-start...");
                    AutoStartManager.SetAutoStart(false);
                    Debug.WriteLine("✓ Auto-start removed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Could not remove auto-start: {ex.Message}");
                }

                Debug.WriteLine("Performing comprehensive secure storage cleanup...");
                await SecureCredentialStorage.ClearAllDataAsync();
                Debug.WriteLine("✓ Complete secure storage cleanup finished");

                // 4. Delete all local data files
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                if (Directory.Exists(appDataDir))
                {
                    try
                    {
                        Directory.Delete(appDataDir, true);
                        Debug.WriteLine("✓ AkademiTrack directory deleted");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Could not delete directory: {ex.Message}");
                    }
                }

                // 6. Create uninstall script to delete the app itself
                var appPath = GetApplicationBundlePath();
                Debug.WriteLine($"Application path: {appPath}");

                if (string.IsNullOrEmpty(appPath))
                {
                    Debug.WriteLine("⚠️ Could not determine app path - cannot delete app bundle");
                    await ShowInfoDialog(
                        "Data slettet",
                        "All data er slettet, men applikasjonen må slettes manuelt.\n\n" +
                        "Dra AkademiTrack.app til papirkurven for å fullføre avinstallasjonen."
                    );
                    
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown(0);
                    }
                    return;
                }

                // Create platform-specific uninstall script
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await UninstallOnMacOSAsync(appPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await UninstallOnWindowsAsync(appPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await UninstallOnLinuxAsync(appPath);
                }

                Debug.WriteLine("=== UNINSTALL SCRIPT EXECUTED ===");

                // Close the app - script will delete it after we exit
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                {
                    desktopLifetime.Shutdown(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"=== UNINSTALL FAILED ===");
                Debug.WriteLine($"Error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                await ShowErrorDialog(
                    "Avinstallasjon feilet",
                    $"Kunne ikke fullføre avinstallasjonen:\n\n{ex.Message}\n\n" +
                    "Vennligst slett applikasjonen manuelt fra Applications-mappen."
                );
            }
            finally
            {
                IsDeleting = false;
            }
        }

        private string GetApplicationBundlePath()
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                Debug.WriteLine($"Executable path: {exePath}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // On macOS, find the .app bundle
                    if (exePath.Contains(".app/Contents/MacOS"))
                    {
                        var appIndex = exePath.IndexOf(".app/Contents/MacOS");
                        var appBundlePath = exePath.Substring(0, appIndex + 4); // Include ".app"
                        Debug.WriteLine($"macOS app bundle: {appBundlePath}");
                        return appBundlePath;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (exePath.EndsWith(".dll"))
                    {
                        var appDir = Path.GetDirectoryName(exePath);
                        var appName = Path.GetFileNameWithoutExtension(exePath);
                        exePath = Path.Combine(appDir!, appName + ".exe");
                    }
                    return exePath;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return exePath;
                }

                return exePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting app path: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task UninstallOnMacOSAsync(string appBundlePath)
        {
            try
            {
                Debug.WriteLine("=== CREATING MACOS UNINSTALL SCRIPT ===");
                
                var tempDir = Path.GetTempPath();
                var scriptPath = Path.Combine(tempDir, "akademitrack_uninstall.sh");

                var scriptContent = $@"#!/bin/bash
        # AkademiTrack Uninstall Script - Silent Background Execution

        # Wait for app to close (max 10 seconds)
        for i in {{1..20}}; do
            if ! pgrep -f ""AkademiTrack"" > /dev/null; then
                break
            fi
            sleep 0.5
        done

        # Delete the app bundle
        rm -rf ""{appBundlePath}"" 2>/dev/null

        # Delete this script
        rm -f ""$0"" 2>/dev/null
        ";

                await File.WriteAllTextAsync(scriptPath, scriptContent);
                
                // Make script executable
                var chmodProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmodProcess?.WaitForExit();

                Debug.WriteLine($"✓ Uninstall script created: {scriptPath}");

                // Run the script silently in the background - no terminal window needed
                var backgroundProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                });

                Debug.WriteLine("✓ Uninstall script launched in Terminal");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating macOS uninstall script: {ex.Message}");
                throw;
            }
        }

        private async Task UninstallOnWindowsAsync(string exePath)
        {
            try
            {
                Debug.WriteLine("=== CREATING WINDOWS UNINSTALL SCRIPT ===");
                
                var tempDir = Path.GetTempPath();
                var scriptPath = Path.Combine(tempDir, "akademitrack_uninstall.bat");

                // Get the app directory (everything in the installation folder)
                var appDir = Path.GetDirectoryName(exePath);

                var scriptContent = $@"@echo off
        REM AkademiTrack Uninstall Script - Silent Background Execution

        REM Wait for app to close
        timeout /t 3 /nobreak >nul 2>nul

        REM Delete the entire application directory
        rd /s /q ""{appDir}"" >nul 2>nul

        REM Delete this script
        del ""%~f0"" >nul 2>nul
        ";

                await File.WriteAllTextAsync(scriptPath, scriptContent);
                
                Debug.WriteLine($"✓ Uninstall script created: {scriptPath}");

                // Execute the batch file silently
                var batchProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Debug.WriteLine("✓ Uninstall script launched");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating Windows uninstall script: {ex.Message}");
                throw;
            }
        }

        private async Task UninstallOnLinuxAsync(string appPath)
        {
            try
            {
                Debug.WriteLine("=== CREATING LINUX UNINSTALL SCRIPT ===");
                
                var tempDir = Path.GetTempPath();
                var scriptPath = Path.Combine(tempDir, "akademitrack_uninstall.sh");

                var appDir = Path.GetDirectoryName(appPath);

                var scriptContent = $@"#!/bin/bash
        # AkademiTrack Uninstall Script - Silent Background Execution

        # Wait for app to close
        sleep 3

        # Delete the application directory
        rm -rf ""{appDir}"" 2>/dev/null

        # Delete this script
        rm -f ""$0"" 2>/dev/null
        ";

                await File.WriteAllTextAsync(scriptPath, scriptContent);
                
                // Make script executable
                var chmodProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmodProcess?.WaitForExit();

                Debug.WriteLine($"✓ Uninstall script created: {scriptPath}");

                // Execute silently in background
                var terminalProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                });

                Debug.WriteLine("✓ Uninstall script launched");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating Linux uninstall script: {ex.Message}");
                throw;
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "Windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "macOS";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "Linux";
            else
                return "Unknown";
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message, bool isDangerous = false)
        {
            try
            {
                var window = GetParentWindow();

                if (window == null)
                {
                    Debug.WriteLine("Could not find parent window for dialog");
                    return false;
                }

                return await ConfirmationDialog.ShowAsync(window, title, message, isDangerous);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing confirmation dialog: {ex.Message}");
                return false;
            }
        }

        private async Task ShowInfoDialog(string title, string message)
        {
            await ShowConfirmationDialog(title, message, false);
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            try
            {
                var window = GetParentWindow();

                if (window == null)
                {
                    Debug.WriteLine("Could not find parent window for dialog");
                    return;
                }

                await ErrorDialog.ShowAsync(window, title, message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing error dialog: {ex.Message}");
            }
        }

        private Avalonia.Controls.Window? GetParentWindow()
        {
            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    // First try to find MainWindow
                    var mainWindow = desktop.MainWindow;
                    if (mainWindow != null && mainWindow.IsVisible)
                    {
                        return mainWindow;
                    }

                    // Otherwise get any active window
                    var activeWindow = desktop.Windows.FirstOrDefault(w => w.IsActive);
                    if (activeWindow != null)
                    {
                        return activeWindow;
                    }

                    // Fallback to first visible window
                    return desktop.Windows.FirstOrDefault(w => w.IsVisible);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding parent window: {ex.Message}");
            }

            return null;
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                IsCheckingForUpdates = true;
                UpdateStatus = "Sjekker etter oppdateringer...";
                UpdateAvailable = false;

                using var httpClient = new System.Net.Http.HttpClient();
                var jsonResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/update.json");
                var updateInfo = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                var latestVersion = updateInfo.GetProperty("latest_version").GetString();
                var currentVersion = ApplicationInfo.Version;

                if (string.IsNullOrEmpty(latestVersion))
                {
                    UpdateStatus = "Kunne ikke hente versjonsinformasjon";
                    UpdateAvailable = false;
                    return;
                }

                var latestVersionClean = latestVersion.TrimStart('v');

                if (IsNewerVersion(currentVersion, latestVersionClean))
                {
                    AvailableVersion = latestVersionClean;
                    UpdateStatus = $"Ny versjon tilgjengelig: v{AvailableVersion}";
                    UpdateAvailable = true;

                    Dispatcher.UIThread.Post(() =>
                    {
                        NativeNotificationService.Show(
                            "Oppdatering tilgjengelig",
                            $"En ny versjon ({AvailableVersion}) er klar for nedlasting.",
                            "INFO"
                        );
                    });
                }
                else
                {
                    UpdateStatus = $"Du har den nyeste versjonen (v{ApplicationInfo.Version})";
                    UpdateAvailable = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
                UpdateStatus = "Kunne ikke sjekke etter oppdateringer";
                UpdateAvailable = false;
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        private bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            try
            {
                var current = new Version(currentVersion);
                var latest = new Version(latestVersion);
                return latest > current;
            }
            catch
            {
                return false;
            }
        }
        private void LogInfo(string message)
        {
            Debug.WriteLine($"[UPDATE] {message}");
        }

        private async Task DownloadAndInstallUpdateAsync()
        {
            try
            {
                IsCheckingForUpdates = true;
                UpdateStatus = "Forbereder oppdatering...";
                LogInfo("=== UPDATE PROCESS STARTED ===");

                LogInfo($"Current app location: {Environment.ProcessPath}");
                LogInfo($"Current version: {ApplicationInfo.Version}");
                LogInfo($"Target version: {AvailableVersion}");


                string platformSuffix = "";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    platformSuffix = "-osx";
                    LogInfo("Platform detected: macOS");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    platformSuffix = "-linux";
                    LogInfo("Platform detected: Linux");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    platformSuffix = "";
                    LogInfo("Platform detected: Windows");
                }

                UpdateManager? mgr = null;
                UpdateInfo? updateInfo = null;
                bool updateFound = false;

                // Strategy 1: Try GithubSource
                try
                {
                    LogInfo("Strategy 1: Attempting GithubSource...");
                    LogInfo("URL: https://github.com/CyberGutta/AkademiTrack");
                    UpdateStatus = "Kobler til GitHub...";

                    // GithubSource expects repo URL without /releases
                    var githubSource = new GithubSource("https://github.com/CyberGutta/AkademiTrack", null, false);
                    mgr = new UpdateManager(githubSource);

                    LogInfo("Calling CheckForUpdatesAsync()...");
                    updateInfo = await mgr.CheckForUpdatesAsync();

                    if (updateInfo != null)
                    {
                        LogInfo($"Strategy 1 SUCCESS: Found version {updateInfo.TargetFullRelease.Version}");
                        LogInfo($"Package name: {updateInfo.TargetFullRelease.FileName}");
                        LogInfo($"Package size: {updateInfo.TargetFullRelease.Size} bytes");
                        updateFound = true;
                    }
                    else
                    {
                        LogInfo("Strategy 1: CheckForUpdatesAsync returned null");
                        LogInfo("This usually means no newer version was found");
                    }
                }
                catch (Exception ex)
                {
                    LogInfo($"Strategy 1 FAILED: {ex.GetType().Name}: {ex.Message}");
                    LogInfo($"Stack trace: {ex.StackTrace}");
                }

                // Strategy 2: Try direct URL with version tag
                if (!updateFound && !string.IsNullOrEmpty(AvailableVersion))
                {
                    try
                    {
                        LogInfo($"Strategy 2: Attempting direct URL with version v{AvailableVersion}...");
                        var directUrl = $"https://github.com/CyberGutta/AkademiTrack/releases/download/v{AvailableVersion}";
                        LogInfo($"URL: {directUrl}");
                        UpdateStatus = "Prøver alternativ nedlasting...";

                        mgr = new UpdateManager(directUrl);

                        LogInfo("Calling CheckForUpdatesAsync()...");
                        updateInfo = await mgr.CheckForUpdatesAsync();

                        if (updateInfo != null)
                        {
                            LogInfo($"Strategy 2 SUCCESS: Found version {updateInfo.TargetFullRelease.Version}");
                            LogInfo($"Package name: {updateInfo.TargetFullRelease.FileName}");
                            updateFound = true;
                        }
                        else
                        {
                            LogInfo("Strategy 2: CheckForUpdatesAsync returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogInfo($"Strategy 2 FAILED: {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                            LogInfo($"Inner exception: {ex.InnerException.Message}");
                    }
                }

                // Strategy 3: Try with platform-specific package name hint
                if (!updateFound && !string.IsNullOrEmpty(AvailableVersion) && !string.IsNullOrEmpty(platformSuffix))
                {
                    try
                    {
                        LogInfo($"Strategy 3: Attempting platform-specific URL ({platformSuffix})...");
                        var platformUrl = $"https://github.com/CyberGutta/AkademiTrack/releases/download/v{AvailableVersion}";
                        LogInfo($"URL: {platformUrl}");
                        UpdateStatus = "Søker etter plattform-spesifikk pakke...";

                        mgr = new UpdateManager(platformUrl);

                        LogInfo("Calling CheckForUpdatesAsync()...");
                        updateInfo = await mgr.CheckForUpdatesAsync();

                        if (updateInfo != null)
                        {
                            LogInfo($"Strategy 3 SUCCESS: Found version {updateInfo.TargetFullRelease.Version}");
                            updateFound = true;
                        }
                        else
                        {
                            LogInfo("Strategy 3: CheckForUpdatesAsync returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogInfo($"Strategy 3 FAILED: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Strategy 4: Try /releases/latest path
                if (!updateFound)
                {
                    try
                    {
                        LogInfo("Strategy 4: Attempting /releases/latest...");
                        var latestUrl = "https://github.com/CyberGutta/AkademiTrack/releases/latest/download";
                        LogInfo($"URL: {latestUrl}");
                        UpdateStatus = "Prøver siste utgivelse...";

                        mgr = new UpdateManager(latestUrl);

                        LogInfo("Calling CheckForUpdatesAsync()...");
                        updateInfo = await mgr.CheckForUpdatesAsync();

                        if (updateInfo != null)
                        {
                            LogInfo($"Strategy 4 SUCCESS: Found version {updateInfo.TargetFullRelease.Version}");
                            updateFound = true;
                        }
                        else
                        {
                            LogInfo("Strategy 4: CheckForUpdatesAsync returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogInfo($"Strategy 4 FAILED: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // If no update found after all strategies
                if (!updateFound || updateInfo == null || mgr == null)
                {
                    LogInfo("=== ALL STRATEGIES FAILED ===");
                    LogInfo("No update could be found through any method");
                    LogInfo("Possible reasons:");
                    LogInfo("1. Missing RELEASES file in GitHub release");
                    LogInfo("2. Incorrect package naming");
                    LogInfo("3. Network/connectivity issues");
                    LogInfo("4. GitHub API rate limiting");
                    UpdateStatus = "Kunne ikke finne oppdatering";

                    // Fallback: Open browser to releases page
                    try
                    {
                        LogInfo("Opening browser as fallback...");
                        UpdateStatus = "Åpner nedlastingsside...";
                        await Task.Delay(1000);

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/CyberGutta/AkademiTrack/releases/latest",
                            UseShellExecute = true
                        });

                        UpdateStatus = "Vennligst last ned manuelt fra nettleseren";
                        LogInfo("Browser opened successfully");
                    }
                    catch (Exception browserEx)
                    {
                        LogInfo($"Browser fallback failed: {browserEx.Message}");
                        UpdateStatus = "Kunne ikke åpne nettleser. Besøk GitHub manuelt.";
                    }
                    return;
                }

                // Download the update
                LogInfo($"=== STARTING DOWNLOAD ===");
                LogInfo($"Version: {updateInfo.TargetFullRelease.Version}");
                LogInfo($"Package: {updateInfo.TargetFullRelease.FileName}");
                LogInfo($"Size: {updateInfo.TargetFullRelease.Size / 1024 / 1024} MB");
                UpdateStatus = "Laster ned oppdatering";

                await mgr.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateStatus = $"Laster ned {progress}%";
                        if (progress % 10 == 0) // Log every 10%
                            LogInfo($"Download progress: {progress}%");
                    });
                });

                LogInfo("✓ Download completed successfully");
                UpdateStatus = "Installerer oppdatering...";
                await Task.Delay(500);

                // Apply update and restart
                LogInfo("=== APPLYING UPDATE ===");
                LogInfo("Calling ApplyUpdatesAndRestart()...");
                UpdateStatus = "Starter på nytt...";
                await Task.Delay(1000);

                mgr.ApplyUpdatesAndRestart(updateInfo);
                LogInfo("ApplyUpdatesAndRestart() called - app should restart now");
            }
            catch (Exception ex)
            {
                LogInfo("=== CRITICAL ERROR ===");
                LogInfo($"Exception type: {ex.GetType().Name}");
                LogInfo($"Error message: {ex.Message}");
                LogInfo($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    LogInfo($"Inner exception: {ex.InnerException.GetType().Name}");
                    LogInfo($"Inner message: {ex.InnerException.Message}");
                }

                UpdateStatus = $"Feil ved oppdatering: {ex.Message}";
                UpdateAvailable = false;

                // Final fallback: Open browser
                try
                {
                    LogInfo("Attempting final browser fallback...");
                    await Task.Delay(2000);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/CyberGutta/AkademiTrack/releases/latest",
                        UseShellExecute = true
                    });

                    UpdateStatus = "Åpnet nedlastingsside - installer manuelt";
                    LogInfo("Final browser fallback successful");
                }
                catch (Exception browserEx)
                {
                    LogInfo($"Final browser fallback failed: {browserEx.Message}");
                    UpdateStatus = "Besøk: github.com/CyberGutta/AkademiTrack/releases";
                }
            }
            finally
            {
                IsCheckingForUpdates = false;
                LogInfo("=== UPDATE PROCESS ENDED ===");
            }
        }

        public async Task CheckForUpdatesOnStartupAsync()
        {
            await Task.Delay(3000);
            await CheckForUpdatesAsync();
        }

        public void UseCredentialsSecurely(Action<string, string, string> action)
        {
            var passwordPlain = SecureStringToString(_loginPasswordSecure);
            try
            {
                action(_loginEmail, passwordPlain, _schoolName);
            }
            finally
            {
                // Immediately clear the plaintext password
                passwordPlain = null;
            }
        }
        public void SetLogEntries(ObservableCollection<LogEntry> logEntries)
        {
            if (_allLogEntries != null) _allLogEntries.CollectionChanged -= OnAllLogEntriesChanged;
            _allLogEntries = logEntries ?? new ObservableCollection<LogEntry>();
            _allLogEntries.CollectionChanged += OnAllLogEntriesChanged;
            RefreshDisplayedLogs();
        }

        private void OnAllLogEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(RefreshDisplayedLogs);
        }

        private void RefreshDisplayedLogs()
        {
            _displayedLogEntries.Clear();
            var logsToShow = _showDetailedLogs ? _allLogEntries : _allLogEntries.Where(log => log.Level != "DEBUG");
            foreach (var log in logsToShow) _displayedLogEntries.Add(log);
        }

        private void ToggleActivityLog() => ShowActivityLog = !ShowActivityLog;
        private void ToggleAutoStart() => StartWithSystem = !StartWithSystem;
        private void CloseWindow() => CloseRequested?.Invoke(this, EventArgs.Empty);
        private void ToggleAutoStartAutomation() => AutoStartAutomation = !AutoStartAutomation;
        private void OpenEmail()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "mailto:cyberbrothershq@gmail.com", UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Error opening email: {ex.Message}"); }
        }

        private void OpenWebsite()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://cybergutta.github.io/AkademietTrack/", UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Error opening webiste: {ex.Message}"); }
        }

        private void ClearLogs()
        {
            _allLogEntries?.Clear();
            _displayedLogEntries.Clear();
        }

        private async Task ExportLogsAsync()
        {
            try
            {
                if (_allLogEntries == null || _allLogEntries.Count == 0)
                {
                    await ShowErrorDialog("Ingen logger", "Det finnes ingen logger å eksportere.");
                    return;
                }

                var window = GetParentWindow();

                if (window == null)
                {
                    Debug.WriteLine("Could not find parent window");
                    return;
                }

                var dialog = new ExportLogsDialog();
                var result = await dialog.ShowDialog<ExportLogsResult?>(window);

                if (result == null || result.Cancelled)
                {
                    Debug.WriteLine("Export cancelled by user");
                    return;
                }

                string content;
                string defaultFileName;
                string fileExtension;

                switch (result.Format)
                {
                    case "json":
                        content = GenerateJsonExport();
                        defaultFileName = $"akademitrack_logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                        fileExtension = "json";
                        break;

                    case "report":
                        content = GenerateReportExport();
                        defaultFileName = $"akademitrack_rapport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                        fileExtension = "txt";
                        break;

                    default: // "text"
                        content = GenerateTextExport();
                        defaultFileName = $"akademitrack_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                        fileExtension = "txt";
                        break;
                }

                // Handle destination
                string? filePath = null;

                if (result.Destination == "desktop")
                {
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    filePath = Path.Combine(desktopPath, defaultFileName);
                }
                else if (result.Destination == "documents")
                {
                    var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var appFolder = Path.Combine(documentsPath, "AkademiTrack", "Logs");
                    Directory.CreateDirectory(appFolder);
                    filePath = Path.Combine(appFolder, defaultFileName);
                }
                else if (result.Destination == "clipboard")
                {
                    await SetClipboardTextAsync(content);
                    NativeNotificationService.Show(
                        "Logger kopiert",
                        "Aktivitetsloggen er kopiert til utklippstavlen",
                        "SUCCESS"
                    );
                    Debug.WriteLine("✓ Logs copied to clipboard");
                    return;
                }
                else // custom
                {
                    var storageProvider = window.StorageProvider;

                    var fileTypeChoice = new Avalonia.Platform.Storage.FilePickerFileType(fileExtension.ToUpper() + " filer")
                    {
                        Patterns = new[] { $"*.{fileExtension}" }
                    };

                    var allFilesChoice = new Avalonia.Platform.Storage.FilePickerFileType("Alle filer")
                    {
                        Patterns = new[] { "*.*" }
                    };

                    var saveOptions = new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = "Lagre logger",
                        SuggestedFileName = defaultFileName,
                        DefaultExtension = fileExtension,
                        FileTypeChoices = new[] { fileTypeChoice, allFilesChoice },
                        ShowOverwritePrompt = true
                    };

                    var file = await storageProvider.SaveFilePickerAsync(saveOptions);

                    if (file == null)
                    {
                        Debug.WriteLine("Save cancelled by user");
                        return;
                    }

                    filePath = file.Path.LocalPath;
                }

                // Save file
                if (!string.IsNullOrEmpty(filePath))
                {
                    await File.WriteAllTextAsync(filePath, content);
                    Debug.WriteLine($"✓ Logs exported to: {filePath}");

                    var openFile = await ShowConfirmationDialog(
                        "Logger eksportert! ✓",
                        $"Loggene er lagret til:\n\n{filePath}\n\n" +
                        "Vil du åpne mappen der filen er lagret?",
                        false
                    );

                    if (openFile)
                    {
                        try
                        {
                            var directory = Path.GetDirectoryName(filePath);
                            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = directory,
                                    UseShellExecute = true
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error opening folder: {ex.Message}");
                        }
                    }

                    NativeNotificationService.Show(
                        "Logger eksportert",
                        $"Filen er lagret som {Path.GetFileName(filePath)}",
                        "SUCCESS"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting logs: {ex.Message}");
                await ShowErrorDialog(
                    "Eksport feilet",
                    $"Kunne ikke eksportere logger:\n\n{ex.Message}"
                );
            }
        }

        private string GenerateTextExport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AkademiTrack Aktivitetslogg ===");
            sb.AppendLine($"Eksportert: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Versjon: {ApplicationInfo.Version}");
            sb.AppendLine($"Totalt antall logger: {_allLogEntries.Count}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            foreach (var log in _allLogEntries)
            {
                sb.AppendLine(log.FormattedMessage);
            }

            return sb.ToString();
        }

        private string GenerateJsonExport()
        {
            var exportData = new
            {
                exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = ApplicationInfo.Version,
                totalLogs = _allLogEntries.Count,
                logs = _allLogEntries.Select(log => new
                {
                    timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    level = log.Level,
                    message = log.Message
                }).ToList()
            };

            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private string GenerateReportExport()
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("╔═══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║          AKADEMITRACK DIAGNOSTIKKRAPPORT                      ║");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // System info
            sb.AppendLine("📊 SYSTEMINFORMASJON");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"Versjon:        {ApplicationInfo.Version}");
            sb.AppendLine($"Plattform:      {Environment.OSVersion}");
            sb.AppendLine($"Arkitektur:     {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Rapport dato:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Totalt logger:  {_allLogEntries.Count}");
            sb.AppendLine();

            // Log statistics
            var errorCount = _allLogEntries.Count(l => l.Level == "ERROR");
            var successCount = _allLogEntries.Count(l => l.Level == "SUCCESS");
            var infoCount = _allLogEntries.Count(l => l.Level == "INFO");
            var debugCount = _allLogEntries.Count(l => l.Level == "DEBUG");

            sb.AppendLine("📈 LOGGSTATISTIKK");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"❌ Feil:        {errorCount}");
            sb.AppendLine($"✅ Suksess:     {successCount}");
            sb.AppendLine($"ℹ️  Info:        {infoCount}");
            sb.AppendLine($"🔧 Debug:       {debugCount}");
            sb.AppendLine();

            // Recent errors (if any)
            var recentErrors = _allLogEntries.Where(l => l.Level == "ERROR").Take(10).ToList();
            if (recentErrors.Any())
            {
                sb.AppendLine("🚨 SISTE FEIL (maks 10)");
                sb.AppendLine(new string('-', 60));
                foreach (var error in recentErrors)
                {
                    sb.AppendLine($"[{error.Timestamp:HH:mm:ss}] {error.Message}");
                }
                sb.AppendLine();
            }

            // Full log
            sb.AppendLine("📜 FULLSTENDIG LOGG");
            sb.AppendLine(new string('-', 60));
            foreach (var log in _allLogEntries)
            {
                var icon = log.Level switch
                {
                    "ERROR" => "❌",
                    "SUCCESS" => "✅",
                    "INFO" => "ℹ️",
                    "DEBUG" => "🔧",
                    _ => "•"
                };
                sb.AppendLine($"{icon} {log.FormattedMessage}");
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("Slutt på rapport");

            return sb.ToString();
        }

        private async Task SetClipboardTextAsync(string text)
        {
            try
            {
                var window = GetParentWindow();
                if (window?.Clipboard != null)
                {
                    await window.Clipboard.SetTextAsync(text);
                    return;
                }

                Debug.WriteLine("Could not access clipboard");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting clipboard text: {ex.Message}");
                throw;
            }
        }

        private void ToggleDetailedLogs() => ShowDetailedLogs = !ShowDetailedLogs;

        public async Task LoadSettingsAsync()
        {
            try
            {
                var settings = await SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();

                _showActivityLog      = settings.ShowActivityLog;
                _showDetailedLogs     = settings.ShowDetailedLogs;
                _startWithSystem      = settings.StartWithSystem;
                _autoStartAutomation  = settings.AutoStartAutomation;
                _startMinimized       = settings.StartMinimized;
                _enableNotifications = settings.EnableNotifications;


                var email   = await SecureCredentialStorage.GetCredentialAsync("LoginEmail")     ?? "";
                var pass    = await SecureCredentialStorage.GetCredentialAsync("LoginPassword")  ?? "";
                var school  = await SecureCredentialStorage.GetCredentialAsync("SchoolName")     ?? "";

                LoginEmail   = email;
                
                // Convert password to SecureString immediately
                _loginPasswordSecure?.Dispose();
                _loginPasswordSecure = StringToSecureString(pass);
                
                // Clear the plaintext password from memory
                pass = null;
                
                SchoolName   = school;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(ShowActivityLog));
                    OnPropertyChanged(nameof(ShowDetailedLogs));
                    OnPropertyChanged(nameof(StartWithSystem));
                    OnPropertyChanged(nameof(AutoStartAutomation));
                    OnPropertyChanged(nameof(StartMinimized));
                    OnPropertyChanged(nameof(EnableNotifications));
                    OnPropertyChanged(nameof(LoginEmail));
                    OnPropertyChanged(nameof(LoginPassword));
                    OnPropertyChanged(nameof(SchoolName));
                });

                RefreshDisplayedLogs();
                
                // Initialize original values for change tracking
                InitializeOriginalLoginValues();
                
                _ = Task.Run(() =>
                {
                    try
                    {
                        AutoStartManager.SetAutoStart(_startWithSystem);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SettingsViewModel] AutoStart setup failed: {ex.Message}");
                    }
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        Debug.WriteLine($"[SettingsViewModel] AutoStart task failed: {t.Exception.GetBaseException().Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var settings = new AppSettings
                {
                    ShowActivityLog      = _showActivityLog,
                    ShowDetailedLogs     = _showDetailedLogs,
                    StartWithSystem      = _startWithSystem,
                    AutoStartAutomation  = _autoStartAutomation,
                    StartMinimized       = _startMinimized,
                    EnableNotifications = _enableNotifications,
                    InitialSetupCompleted = true,
                    LastUpdated          = DateTime.Now
                };

                await SafeSettingsLoader.SaveSettingsSafelyAsync(settings);

                if (!string.IsNullOrEmpty(_loginEmail))
                {
                    await SecureCredentialStorage.SaveCredentialAsync("LoginEmail", _loginEmail);
                    // Also save to legacy key for backwards compatibility
                    await SecureCredentialStorage.SaveCredentialAsync("feide_username", _loginEmail);
                }

                var passwordPlain = SecureStringToString(_loginPasswordSecure);
                try
                {
                    if (!string.IsNullOrEmpty(passwordPlain))
                    {
                        await SecureCredentialStorage.SaveCredentialAsync("LoginPassword", passwordPlain);
                    }
                }
                finally
                {
                    // Immediately clear the temporary plaintext password
                    passwordPlain = null;
                }

                if (!string.IsNullOrEmpty(_schoolName))
                    await SecureCredentialStorage.SaveCredentialAsync("SchoolName", _schoolName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void CheckForLoginChanges()
        {
            var currentEmail = _loginEmail ?? "";
            var currentPassword = SecureStringToString(_loginPasswordSecure) ?? "";
            var currentSchool = _schoolName ?? "";

            bool hasChanges = currentEmail != _originalLoginEmail ||
                             currentPassword != _originalLoginPassword ||
                             currentSchool != _originalSchoolName;

            HasUnsavedLoginChanges = hasChanges;
        }

        private void SaveLoginCredentials()
        {
            try
            {
                // Update the original values to match current values
                _originalLoginEmail = _loginEmail ?? "";
                _originalLoginPassword = SecureStringToString(_loginPasswordSecure) ?? "";
                _originalSchoolName = _schoolName ?? "";

                // Reset the unsaved changes flag
                HasUnsavedLoginChanges = false;

                Debug.WriteLine("[Settings] Login credentials saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Error saving login credentials: {ex.Message}");
            }
        }

        private void InitializeOriginalLoginValues()
        {
            _originalLoginEmail = _loginEmail ?? "";
            _originalLoginPassword = SecureStringToString(_loginPasswordSecure) ?? "";
            _originalSchoolName = _schoolName ?? "";
            HasUnsavedLoginChanges = false;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static SecureString StringToSecureString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new SecureString();

            var secure = new SecureString();
            foreach (char c in str)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();
            return secure;
        }

        private static string SecureStringToString(SecureString? secure)
        {
            if (secure == null || secure.Length == 0)
                return string.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(secure);
                return Marshal.PtrToStringBSTR(ptr) ?? string.Empty;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
        }
        public void Dispose()
        {
            _loginPasswordSecure?.Dispose();
            _loginPasswordSecure = null;
            
            Services.ResourceMonitor.Instance.StopMonitoring();
            
            Debug.WriteLine("SettingsViewModel disposed - cleanup timer stopped");
        }
    }  
}