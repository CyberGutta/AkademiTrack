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
using System.Security.Cryptography;
using System.Text;
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
        public bool ShowActivityLog { get; set; } = false;
        public bool ShowDetailedLogs { get; set; } = true;
        public bool StartWithSystem { get; set; } = true;
        public bool AutoStartAutomation { get; set; } = false; // ADD THIS LINE
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        // Encrypted credentials
        public string EncryptedLoginEmail { get; set; } = "";
        public string EncryptedLoginPassword { get; set; } = "";
        public string EncryptedSchoolName { get; set; } = "";
        public bool InitialSetupCompleted { get; set; } = false;
    }

    // Simple encryption helper class
    public static class CredentialEncryption
    {
        private static readonly string EncryptionKey = GenerateKey();

        private static string GenerateKey()
        {
            try
            {
                // Create a consistent key based on machine/user info
                var machineInfo = Environment.MachineName + Environment.UserName + "AkademiTrack2025";
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
                    var base64 = Convert.ToBase64String(hash);
                    // Ensure we have exactly 32 bytes for AES-256
                    var key = base64.Length >= 32 ? base64.Substring(0, 32) : base64.PadRight(32, '0');
                    Debug.WriteLine($"Generated encryption key length: {key.Length}");
                    return key;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating encryption key: {ex.Message}");
                // Fallback key
                return "AkademiTrack2025DefaultKey12345";
            }
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    using (var msEncrypt = new MemoryStream())
                    {
                        // Prepend IV to the encrypted data
                        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }

                        return Convert.ToBase64String(msEncrypt.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Encryption failed: {ex.Message}");
                return "";
            }
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return "";

            try
            {
                var fullCipher = Convert.FromBase64String(encryptedText);

                using (var aes = Aes.Create())
                {
                    aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);

                    // Extract IV from the beginning
                    var iv = new byte[aes.BlockSize / 8];
                    var cipher = new byte[fullCipher.Length - iv.Length];

                    Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                    Array.Copy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var msDecrypt = new MemoryStream(cipher))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Decryption failed: {ex.Message}");
                return "";
            }
        }
    }

    // Auto-start manager for cross-platform support (keeping existing code)
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

        // Improved Linux implementation with better debugging and path handling
        private static bool SetAutoStartLinux(bool enable)
        {
            var desktopFilePath = GetLinuxDesktopFilePath();
            var autostartDir = Path.GetDirectoryName(desktopFilePath);

            Debug.WriteLine($"Desktop file path: {desktopFilePath}");
            Debug.WriteLine($"Autostart directory: {autostartDir}");

            if (enable)
            {
                // Create autostart directory if it doesn't exist
                if (!Directory.Exists(autostartDir))
                {
                    Directory.CreateDirectory(autostartDir!);
                    Debug.WriteLine($"Created autostart directory: {autostartDir}");
                }

                var exePath = GetLinuxExecutablePath();
                Debug.WriteLine($"Executable path determined: {exePath}");

                var desktopContent = $@"[Desktop Entry]
Type=Application
Name={AppName}
Exec={exePath}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
StartupNotify=false
Terminal=false
Comment=AkademiTrack automatisk fremmøte registerings program
Categories=Utility;
";

                File.WriteAllText(desktopFilePath, desktopContent);
                Debug.WriteLine($"Desktop file created with content:\n{desktopContent}");

                // Make the file executable
                try
                {
                    var chmodProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{desktopFilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });

                    if (chmodProcess != null)
                    {
                        chmodProcess.WaitForExit();
                        Debug.WriteLine($"chmod exit code: {chmodProcess.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error making desktop file executable: {ex.Message}");
                }

                // Verify the file was created and is readable
                if (File.Exists(desktopFilePath))
                {
                    var fileInfo = new FileInfo(desktopFilePath);
                    Debug.WriteLine($"Desktop file size: {fileInfo.Length} bytes");
                    Debug.WriteLine($"Desktop file permissions: {GetFilePermissions(desktopFilePath)}");
                }
            }
            else
            {
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    Debug.WriteLine($"Desktop file deleted: {desktopFilePath}");
                }
            }
            return true;
        }

        private static string GetLinuxExecutablePath()
        {
            try
            {
                // First try to get the actual process path
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    Debug.WriteLine($"Using process path: {processPath}");
                    return $"\"{processPath}\"";
                }

                // Get assembly location
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                Debug.WriteLine($"Assembly location: {assemblyLocation}");

                if (assemblyLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // For .NET applications on Linux, we need to use dotnet
                    var dotnetPath = GetDotnetPath();
                    if (!string.IsNullOrEmpty(dotnetPath))
                    {
                        Debug.WriteLine($"Using dotnet: {dotnetPath}");
                        return $"\"{dotnetPath}\" \"{assemblyLocation}\"";
                    }
                    else
                    {
                        Debug.WriteLine("dotnet not found in PATH, using default");
                        return $"dotnet \"{assemblyLocation}\"";
                    }
                }
                else
                {
                    // Direct executable
                    return $"\"{assemblyLocation}\"";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Linux executable path: {ex.Message}");
                return $"dotnet \"{Assembly.GetExecutingAssembly().Location}\"";
            }
        }

        private static string GetDotnetPath()
        {
            try
            {
                var whichProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (whichProcess != null)
                {
                    whichProcess.WaitForExit();
                    if (whichProcess.ExitCode == 0)
                    {
                        var output = whichProcess.StandardOutput.ReadToEnd().Trim();
                        Debug.WriteLine($"Found dotnet at: {output}");
                        return output;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding dotnet path: {ex.Message}");
            }
            return "";
        }

        private static string GetFilePermissions(string filePath)
        {
            try
            {
                var statProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "stat",
                    Arguments = $"-c %a \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });

                if (statProcess != null)
                {
                    statProcess.WaitForExit();
                    if (statProcess.ExitCode == 0)
                    {
                        return statProcess.StandardOutput.ReadToEnd().Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting file permissions: {ex.Message}");
            }
            return "unknown";
        }

        private static string GetLinuxDesktopFilePath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(homeDir, ".config");
            return Path.Combine(configDir, "autostart", $"{AppName}.desktop");
        }
    }

    // Converters (keeping existing code)
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

            var version = assembly.GetName().Version;
            if (version != null)
            {
                // Format as Major.Minor.Build (3 parts instead of 4)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";
            }
            else
            {
                Version = "1.0.0";
            }

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

        // New credential fields
        private string _loginEmail = "";
        private string _loginPassword = "";
        private string _schoolName = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;

        public ApplicationInfo ApplicationInfo { get; }
        public ICommand CloseCommand { get; }
        public ICommand OpenProgramFolderCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand ToggleActivityLogCommand { get; }
        public ICommand ToggleAutoStartCommand { get; }
        public ICommand ToggleAutoStartAutomationCommand { get; }



        // This is what the UI binds to
        public ObservableCollection<LogEntry> LogEntries => _displayedLogEntries;

        private bool _autoStartAutomation = false;

        public bool AutoStartAutomation
        {
            get => _autoStartAutomation;
            set
            {
                if (_autoStartAutomation != value)
                {
                    Debug.WriteLine($"AutoStartAutomation changing from {_autoStartAutomation} to {value}");
                    _autoStartAutomation = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync();
                }
            }
        }


        // Credential properties
        public string LoginEmail
        {
            get => _loginEmail;
            set
            {
                if (_loginEmail != value)
                {
                    _loginEmail = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync(); // Auto-save when changed
                }
            }
        }

        public string LoginPassword
        {
            get => _loginPassword;
            set
            {
                if (_loginPassword != value)
                {
                    _loginPassword = value;
                    OnPropertyChanged();
                    _ = SaveSettingsAsync(); // Auto-save when changed
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
                    _ = SaveSettingsAsync(); // Auto-save when changed
                }
            }
        }

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
            ToggleAutoStartAutomationCommand = new RelayCommand(ToggleAutoStartAutomation);


            // Initialize credential fields to empty strings
            _loginEmail = "";
            _loginPassword = "";
            _schoolName = "";

            // Load settings on initialization
            _ = LoadSettingsAsync();
        }

        private void ToggleAutoStartAutomation()
        {
            AutoStartAutomation = !AutoStartAutomation;
        }

        // Method to get decrypted credentials for use in MainWindowViewModel
        public (string email, string password, string school) GetDecryptedCredentials()
        {
            return (_loginEmail, _loginPassword, _schoolName);
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
                        _autoStartAutomation = settings.AutoStartAutomation; // ADD THIS LINE

                        // Decrypt credentials after loading
                        _loginEmail = CredentialEncryption.Decrypt(settings.EncryptedLoginEmail);
                        _loginPassword = CredentialEncryption.Decrypt(settings.EncryptedLoginPassword);
                        _schoolName = CredentialEncryption.Decrypt(settings.EncryptedSchoolName);

                        // Notify UI of changes for all properties
                        OnPropertyChanged(nameof(ShowActivityLog));
                        OnPropertyChanged(nameof(ShowDetailedLogs));
                        OnPropertyChanged(nameof(StartWithSystem));
                        OnPropertyChanged(nameof(AutoStartAutomation)); // ADD THIS LINE
                        OnPropertyChanged(nameof(LoginEmail));
                        OnPropertyChanged(nameof(LoginPassword));
                        OnPropertyChanged(nameof(SchoolName));

                        RefreshDisplayedLogs();

                        // Sync the auto-start setting with the system on load
                        _ = Task.Run(() => AutoStartManager.SetAutoStart(_startWithSystem));

                        Debug.WriteLine($"Settings loaded successfully from: {filePath}");
                        Debug.WriteLine($"AutoStartAutomation: {_autoStartAutomation}"); // ADD THIS LINE
                    }
                }
                else
                {
                    Debug.WriteLine("No settings file found, using defaults");
                    if (_startWithSystem)
                    {
                        _ = Task.Run(() => AutoStartManager.SetAutoStart(true));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        // Fixed SaveSettingsAsync for SettingsViewModel
        private async Task SaveSettingsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");

                Directory.CreateDirectory(appFolderPath);
                var filePath = GetSettingsFilePath();

                AppSettings settings;
                if (File.Exists(filePath))
                {
                    var existingJson = await File.ReadAllTextAsync(filePath);
                    settings = JsonSerializer.Deserialize<AppSettings>(existingJson) ?? new AppSettings();
                }
                else
                {
                    settings = new AppSettings();
                }

                settings.ShowActivityLog = _showActivityLog;
                settings.ShowDetailedLogs = _showDetailedLogs;
                settings.StartWithSystem = _startWithSystem;
                settings.AutoStartAutomation = _autoStartAutomation; 
                settings.LastUpdated = DateTime.Now;

                settings.EncryptedLoginEmail = CredentialEncryption.Encrypt(_loginEmail);
                settings.EncryptedLoginPassword = CredentialEncryption.Encrypt(_loginPassword);
                settings.EncryptedSchoolName = CredentialEncryption.Encrypt(_schoolName);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);

                Debug.WriteLine($"Settings saved successfully to: {filePath}");
                Debug.WriteLine($"AutoStartAutomation saved as: {settings.AutoStartAutomation}"); 
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}