using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class FeideWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private string _schoolName = string.Empty;
        private string _feideUsername = string.Empty;
        private string _feidePassword = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading = false;

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FeideSetupCompletedEventArgs>? SetupCompleted;

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FeideWindowViewModel()
        {
            SaveCommand = new RelayCommand(async () => await SaveFeideCredentialsAsync(), () => CanSave);
            ExitCommand = new RelayCommand(() => ExitApplication());

            Schools = new ObservableCollection<string>
            {
                "Akademiet Drammen AS",
                "Akademiet Fredrikstad AS",
                "Akademiet Kristiansand AS",
                "Akademiet Norsk Restaurantskole",
                "Akademiet Privatist og Nettstudier AS",
                "Akademiet Realfagsgymnas Sandvika AS",
                "Akademiet Realfagsskole Drammen",
                "Akademiet Realfagsskole Oslo",
                "Akademiet ungdomsskole Lier",
                "Akademiet VGS Bergen",
                "Akademiet VGS Bislett",
                "Akademiet VGS Heltberg Drammen",
                "Akademiet VGS Kongsberg",
                "Akademiet VGS Oslo",
                "Akademiet VGS Sandnes",
                "Akademiet VGS Ypsilon",
                "Akademiet vgs Ålesund"
            };
        }

        public ObservableCollection<string> Schools { get; }

        public string SchoolName
        {
            get => _schoolName;
            set
            {
                _schoolName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string FeideUsername
        {
            get => _feideUsername;
            set
            {
                _feideUsername = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string FeidePassword
        {
            get => _feidePassword;
            set
            {
                _feidePassword = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool CanSave => !IsLoading &&
                               !string.IsNullOrWhiteSpace(SchoolName) &&
                               !string.IsNullOrWhiteSpace(FeideUsername) &&
                               !string.IsNullOrWhiteSpace(FeidePassword);

        public string SaveButtonText => IsLoading ? "Lagrer..." : "Lagre og fortsett";

        public ICommand SaveCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task SaveFeideCredentialsAsync()
        {
            if (!CanSave) return;

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(SchoolName))
                {
                    ErrorMessage = "Vennligst velg en skole";
                    return;
                }

                if (FeideUsername.Length < 3)
                {
                    ErrorMessage = "Feide brukernavn må være minst 3 tegn";
                    return;
                }

                if (FeidePassword.Length < 4)
                {
                    ErrorMessage = "Passord må være minst 4 tegn";
                    return;
                }

                await SaveCredentialsToSettingsAsync();

                System.Diagnostics.Debug.WriteLine($"Feide credentials saved successfully for user: {FeideUsername}");

                SetupCompleted?.Invoke(this, new FeideSetupCompletedEventArgs
                {
                    Success = true,
                    UserEmail = FeideUsername
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Feil ved lagring: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error saving Feide credentials: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task SaveCredentialsToSettingsAsync()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
                Directory.CreateDirectory(appFolderPath);

                var settingsPath = Path.Combine(appFolderPath, "settings.json");

                AppSettings? settings = null;

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var existingJson = await File.ReadAllTextAsync(settingsPath);
                        settings = JsonSerializer.Deserialize<AppSettings>(existingJson);

                        System.Diagnostics.Debug.WriteLine("Successfully loaded existing settings");
                    }
                    catch (JsonException jsonEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Corrupted settings.json detected: {jsonEx.Message}");
                        System.Diagnostics.Debug.WriteLine("Deleting corrupted file and creating fresh settings");

                        try
                        {
                            File.Delete(settingsPath);
                            System.Diagnostics.Debug.WriteLine("Corrupted settings file deleted successfully");
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Warning: Could not delete corrupted file: {deleteEx.Message}");
                        }

                        settings = null; 
                    }
                }

                if (settings == null)
                {
                    settings = new AppSettings
                    {
                        ShowActivityLog = false,
                        ShowDetailedLogs = true,
                        StartWithSystem = true
                    };
                    System.Diagnostics.Debug.WriteLine("Created fresh settings object");
                }

                settings.EncryptedLoginEmail = CredentialEncryption.Encrypt(FeideUsername);
                settings.EncryptedLoginPassword = CredentialEncryption.Encrypt(FeidePassword);
                settings.EncryptedSchoolName = CredentialEncryption.Encrypt(SchoolName);
                settings.LastUpdated = DateTime.Now;

                settings.InitialSetupCompleted = true;

                try
                {
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(settingsPath, json);

                    System.Diagnostics.Debug.WriteLine($"Credentials saved successfully to: {settingsPath}");
                    System.Diagnostics.Debug.WriteLine($"InitialSetupCompleted set to: true");
                }
                catch (Exception writeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write settings file: {writeEx.Message}");
                    throw new Exception($"Kunne ikke lagre innstillinger: {writeEx.Message}", writeEx);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save credentials: {ex.Message}");
                throw;
            }
        }

        private void ExitApplication()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }

    public class FeideSetupCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public required string UserEmail { get; set; }
    }
}