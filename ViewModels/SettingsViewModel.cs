using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class SettingsViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private string _applicationInfo;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler CloseRequested;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SettingsViewModel()
        {
            OpenProgramFolderCommand = new SimpleCommand(OpenProgramFolderAsync);
            CloseCommand = new SimpleCommand(CloseWindowAsync);

            InitializeApplicationInfo();
        }

        public string ApplicationInfo
        {
            get => _applicationInfo;
            private set
            {
                if (_applicationInfo != value)
                {
                    _applicationInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand OpenProgramFolderCommand { get; }
        public ICommand CloseCommand { get; }

        private void InitializeApplicationInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString() ?? "Unknown";
                var location = Path.GetDirectoryName(assembly.Location) ?? "Unknown";
                var buildDate = File.GetCreationTime(assembly.Location).ToString("yyyy-MM-dd HH:mm");

                ApplicationInfo = $"AkademiTrack v{version}\n" +
                               $"Location: {location}\n" +
                               $"Build Date: {buildDate}\n" +
                               $"Configuration files and cookies are stored in the program directory.";
            }
            catch (Exception ex)
            {
                ApplicationInfo = $"AkademiTrack\nError retrieving application info: {ex.Message}";
            }
        }

        private async Task OpenProgramFolderAsync()
        {
            try
            {
                var programPath = AppDomain.CurrentDomain.BaseDirectory;
                var fullPath = Path.GetFullPath(programPath);

                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", fullPath);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", fullPath);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", fullPath);
                }
                else
                {
                    // Fallback: just show the path
                    throw new PlatformNotSupportedException($"Cannot open folder on this platform. Path: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                // You might want to show a message box or update a status property here
                // For now, we'll just ignore the error
                System.Diagnostics.Debug.WriteLine($"Failed to open program folder: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task CloseWindowAsync()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            await Task.CompletedTask;
        }
    }
}