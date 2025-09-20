using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class TutorialWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private bool _dontShowAgain;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ContinueRequested;
        public event EventHandler ExitRequested;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public TutorialWindowViewModel()
        {
            ContinueCommand = new SimpleCommand(ContinueAsync);
            ExitCommand = new SimpleCommand(ExitAsync);
        }

        public bool DontShowAgain
        {
            get => _dontShowAgain;
            set
            {
                _dontShowAgain = value;
                OnPropertyChanged();
            }
        }

        public ICommand ContinueCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task ContinueAsync()
        {
            if (DontShowAgain)
            {
                await SaveTutorialSettingsAsync();
            }
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task ExitAsync()
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task SaveTutorialSettingsAsync()
        {
            try
            {
                // Get the AppData\Roaming folder path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");

                // Create the directory if it doesn't exist
                Directory.CreateDirectory(appFolderPath);

                // Create the full file path
                var filePath = Path.Combine(appFolderPath, "tutorial_settings.json");

                var settings = new TutorialSettings
                {
                    DontShowTutorial = true,
                    LastUpdated = DateTime.Now
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                // Silently fail - not critical
                System.Diagnostics.Debug.WriteLine($"Failed to save tutorial settings: {ex.Message}");
            }
        }
    }

    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}