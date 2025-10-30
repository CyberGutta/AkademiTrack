using AkademiTrack.Services;
using AkademiTrack.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Microsoft.Win32;
using System;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using Velopack;
using Velopack.Sources;

namespace AkademiTrack.ViewModels
{
    public class AppSettings
    {
        public bool ShowActivityLog { get; set; } = false;
        public bool ShowDetailedLogs { get; set; } = true;
        public bool StartWithSystem { get; set; } = true;
        public bool AutoStartAutomation { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
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

                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
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
</dict>
</plist>";

                File.WriteAllText(plistPath, plistContent);
                Process.Start(new ProcessStartInfo { FileName = "launchctl", Arguments = $"load \"{plistPath}\"", UseShellExecute = false, CreateNoWindow = true });
            }
            else
            {
                if (File.Exists(plistPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = "launchctl", Arguments = $"unload \"{plistPath}\"", UseShellExecute = false, CreateNoWindow = true });
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
                return defaultSettings;   // corrupt file → use defaults, will be overwritten later
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
        private bool _autoStartAutomation = false;
        
        private ObservableCollection<LogEntry> _allLogEntries = new();
        private ObservableCollection<LogEntry> _displayedLogEntries = new();
        private string _loginEmail = "";
        private string _loginPassword = "";
        private string _schoolName = "";

        private string _updateStatus = "Klikk for å sjekke etter oppdateringer";
        private bool _isCheckingForUpdates = false;
        private bool _updateAvailable = false;
        private string _availableVersion = "";
        private bool _isDeleting = false;
        private bool _startMinimized = false;

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
                }
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

        public ApplicationInfo ApplicationInfo { get; }
        public ObservableCollection<LogEntry> LogEntries => _displayedLogEntries;

        public ICommand CloseCommand { get; }
        public ICommand OpenProgramFolderCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand ToggleActivityLogCommand { get; }
        public ICommand ToggleAutoStartCommand { get; }
        public ICommand ToggleAutoStartAutomationCommand { get; }
        public ICommand OpenWebsiteCommand { get; }
        public ICommand OpenEmailCommand { get; }
        public ICommand OpenPrivacyPolicyCommand { get; }
        public ICommand OpenTermsofUseCommand { get; }

        public ICommand CheckForUpdatesCommand { get; }
        public ICommand DownloadAndInstallUpdateCommand { get; }
        public ICommand DeleteLocalDataCommand { get; }
        public ICommand DeleteAccountCompletelyCommand { get; }
        public ICommand ExportDataAsJsonCommand { get; }
        public ICommand ExportDataAsCsvCommand { get; }
        public ICommand ToggleStartMinimizedCommand { get; }


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
            set { if (_autoStartAutomation != value) { _autoStartAutomation = value; OnPropertyChanged(); _ = SaveSettingsAsync(); } }
        }

        public string LoginEmail
        {
            get => _loginEmail;
            set { if (_loginEmail != value) { _loginEmail = value; OnPropertyChanged(); _ = SaveSettingsAsync(); } }
        }

        public string LoginPassword
        {
            get => _loginPassword;
            set { if (_loginPassword != value) { _loginPassword = value; OnPropertyChanged(); _ = SaveSettingsAsync(); } }
        }

        public string SchoolName
        {
            get => _schoolName;
            set { if (_schoolName != value) { _schoolName = value; OnPropertyChanged(); _ = SaveSettingsAsync(); } }
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
            CloseCommand = new RelayCommand(CloseWindow);
            OpenProgramFolderCommand = new RelayCommand(OpenProgramFolder);
            ClearLogsCommand = new RelayCommand(ClearLogs);
            ToggleDetailedLogsCommand = new RelayCommand(ToggleDetailedLogs);
            ToggleActivityLogCommand = new RelayCommand(ToggleActivityLog);
            ToggleAutoStartCommand = new RelayCommand(ToggleAutoStart);
            ToggleAutoStartAutomationCommand = new RelayCommand(ToggleAutoStartAutomation);
            OpenWebsiteCommand = new RelayCommand(OpenWebsite);
            OpenEmailCommand = new RelayCommand(OpenEmail);
            OpenPrivacyPolicyCommand = new RelayCommand(OpenPrivacyPolicy);
            OpenTermsofUseCommand = new RelayCommand(OpenTermsOfUse);
            CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            DownloadAndInstallUpdateCommand = new RelayCommand(async () => await DownloadAndInstallUpdateAsync(), () => UpdateAvailable);
            DeleteLocalDataCommand = new RelayCommand(async () => await DeleteLocalDataAsync());
            DeleteAccountCompletelyCommand = new RelayCommand(async () => await DeleteAccountCompletelyAsync());
            ExportDataAsJsonCommand = new RelayCommand(async () => await ExportDataAsync("json"));
            ExportDataAsCsvCommand = new RelayCommand(async () => await ExportDataAsync("csv"));
            ToggleStartMinimizedCommand = new RelayCommand(ToggleStartMinimized);


            _ = LoadSettingsAsync();
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
                    "Dette eksporterer KUN data lagret på din datamaskin:\n" +
                    "• Appinnstillinger\n" +
                    "• Aktiveringsinformasjon\n" +
                    "• Lokale filer og cookies\n\n" +
                    "For DATABASE-data (STU-registreringer, profil, etc.),\n" +
                    "besøk: https://cybergutta.github.io/AkademietTrack/index.html\n\n" +
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
                    "Lokal eksport fullført! ✓",
                    $"Din LOKALE data er eksportert til:\n\n{filePath}\n\n" +
                    "📋 HUSK: For database-data (STU-registreringer, etc.),\n" +
                    "besøk: https://cybergutta.github.io/AkademietTrack/index.html\n\n" +
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

            // Delete from secure storage first
            Debug.WriteLine("Deleting credentials from secure storage...");
            await SecureCredentialStorage.DeleteCredentialAsync("LoginEmail");
            await SecureCredentialStorage.DeleteCredentialAsync("LoginPassword");
            await SecureCredentialStorage.DeleteCredentialAsync("SchoolName");
            Debug.WriteLine("✓ Secure storage cleared");

            // Delete cookies from macOS Keychain (if on macOS)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    Debug.WriteLine("Deleting cookies from macOS Keychain...");
                    await KeychainService.DeleteFromKeychain();
                    Debug.WriteLine("✓ Keychain cookies cleared");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Could not delete from Keychain: {ex.Message}");
                }
            }

            // Then delete local files
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );

            if (Directory.Exists(appDataDir))
            {
                var allFiles = Directory.GetFiles(appDataDir, "*.*", SearchOption.AllDirectories);
                var allDirectories = Directory.GetDirectories(appDataDir, "*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    try
                    {
                        File.Delete(file);
                        Debug.WriteLine($"✓ Deleted file: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Could not delete {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                foreach (var dir in allDirectories.OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                    }
                    catch { }
                }

                try
                {
                    if (Directory.Exists(appDataDir))
                        Directory.Delete(appDataDir, true);
                }
                catch { }
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

        private async Task DeleteAccountCompletelyAsync()
        {
            if (IsDeleting) return;

            try
            {
                var result = await ShowConfirmationDialog(
                    "⚠️ SLETT KONTO PERMANENT",
                    "ADVARSEL: Dette sletter kontoen din permanent!\n\n" +
                    "• All lokal data\n" +
                    "• Din aktivering i databasen\n" +
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


                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                if (Directory.Exists(appDataDir))
                {
                    var allFiles = Directory.GetFiles(appDataDir, "*.*", SearchOption.AllDirectories);
                    var allDirectories = Directory.GetDirectories(appDataDir, "*", SearchOption.AllDirectories);

                    foreach (var file in allFiles)
                    {
                        try { File.Delete(file); Debug.WriteLine($"✓ Deleted: {Path.GetFileName(file)}"); }
                        catch (Exception ex) { Debug.WriteLine($"⚠️ Could not delete {Path.GetFileName(file)}: {ex.Message}"); }
                    }

                    foreach (var dir in allDirectories.OrderByDescending(d => d.Length))
                    {
                        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                        catch { }
                    }

                    try { if (Directory.Exists(appDataDir)) Directory.Delete(appDataDir, true); }
                    catch { }
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
        
        private async Task<bool> ShowConfirmationDialog(string title, string message, bool isDangerous = false)
        {
            try
            {
                var window = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.Windows.FirstOrDefault(w => w is SettingsWindow)
                    : null;

                if (window == null)
                {
                    Debug.WriteLine("Could not find settings window for dialog");
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
            await ShowConfirmationDialog(title, message, true);
        }


        private async Task CheckForUpdatesAsync()
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
                UpdateStatus = "Laster ned oppdatering...";

                await mgr.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateStatus = $"Laster ned... {progress}%";
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

        public (string email, string password, string school) GetDecryptedCredentials() => (_loginEmail, _loginPassword, _schoolName);

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

        private void OpenPrivacyPolicy()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://cybergutta.github.io/AkademietTrack/privacy-policy.html", UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Error opening privacy policy: {ex.Message}"); }
        }

        private void OpenTermsOfUse()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://cybergutta.github.io/AkademietTrack/terms-of-use.html", UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Error opening terms of use: {ex.Message}"); }
        }

        private void OpenWebsite()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://cybergutta.github.io/AkademietTrack/", UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"Error opening webiste: {ex.Message}"); }
        }

        private void OpenProgramFolder()
        {
            try
            {
                var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { Debug.WriteLine($"Error: {ex.Message}"); }
        }

        private void ClearLogs()
        {
            _allLogEntries?.Clear();
            _displayedLogEntries.Clear();
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

                var email   = await SecureCredentialStorage.GetCredentialAsync("LoginEmail")   ?? "";
                var pass    = await SecureCredentialStorage.GetCredentialAsync("LoginPassword") ?? "";
                var school  = await SecureCredentialStorage.GetCredentialAsync("SchoolName")   ?? "";

                LoginEmail   = email;
                LoginPassword = pass;
                SchoolName   = school;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(ShowActivityLog));
                    OnPropertyChanged(nameof(ShowDetailedLogs));
                    OnPropertyChanged(nameof(StartWithSystem));
                    OnPropertyChanged(nameof(AutoStartAutomation));
                    OnPropertyChanged(nameof(StartMinimized));
                    OnPropertyChanged(nameof(LoginEmail));
                    OnPropertyChanged(nameof(LoginPassword));
                    OnPropertyChanged(nameof(SchoolName));
                });

                RefreshDisplayedLogs();
                _ = Task.Run(() => AutoStartManager.SetAutoStart(_startWithSystem));
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
                    InitialSetupCompleted = true,
                    LastUpdated          = DateTime.Now
                };

                await SafeSettingsLoader.SaveSettingsSafelyAsync(settings);

                if (!string.IsNullOrEmpty(_loginEmail))
                    await SecureCredentialStorage.SaveCredentialAsync("LoginEmail", _loginEmail);

                if (!string.IsNullOrEmpty(_loginPassword))
                    await SecureCredentialStorage.SaveCredentialAsync("LoginPassword", _loginPassword);

                if (!string.IsNullOrEmpty(_schoolName))
                    await SecureCredentialStorage.SaveCredentialAsync("SchoolName", _schoolName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}