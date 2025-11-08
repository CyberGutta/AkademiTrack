using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Services;
using System.Diagnostics;

namespace AkademiTrack.ViewModels
{
    public class FeideWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private string _schoolName = string.Empty;
        private string _feideUsername = string.Empty;
        private string _feidePassword = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading = false;
        private bool _isPasswordVisible = false;

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FeideSetupCompletedEventArgs>? SetupCompleted;
        public event EventHandler? CloseRequested; // NEW: Event to close window

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FeideWindowViewModel()
        {
            SaveCommand = new RelayCommand(async () => await SaveFeideCredentialsAsync(), () => CanSave);
            ExitCommand = new RelayCommand(() => ExitApplication());
            TogglePasswordCommand = new RelayCommand(TogglePasswordVisibility);

            Schools = new ObservableCollection<string>
            {
                "Akademiet Drammen AS",
                //"Akademiet Fredrikstad AS",
                //"Akademiet Kristiansand AS",
                //"Akademiet Norsk Restaurantskole",
                //"Akademiet Privatist og Nettstudier AS",
                //"Akademiet Realfagsgymnas Sandvika AS",
                //"Akademiet Realfagsskole Drammen",
                //"Akademiet Realfagsskole Oslo",
                //"Akademiet ungdomsskole Lier",
                //"Akademiet VGS Bergen",
                //"Akademiet VGS Bislett",
                //"Akademiet VGS Heltberg Drammen",
                //"Akademiet VGS Kongsberg",
                //"Akademiet VGS Oslo",
                //"Akademiet VGS Sandnes",
                //"Akademiet VGS Ypsilon",
                //"Akademiet vgs Ålesund"
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

        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                _isPasswordVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PasswordChar));
            }
        }

        public char PasswordChar => IsPasswordVisible ? '\0' : '•';

        public ICommand TogglePasswordCommand { get; }

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
                // Validate inputs
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

                Debug.WriteLine($"[FeideWindow] Saving credentials for user: {FeideUsername}");
                
                // Save credentials
                await SaveCredentialsToSettingsAsync();

                Debug.WriteLine("[FeideWindow] Credentials saved successfully!");

                // Notify success
                SetupCompleted?.Invoke(this, new FeideSetupCompletedEventArgs
                {
                    Success = true,
                    UserEmail = FeideUsername
                });

                // Close this window and show main window
                Debug.WriteLine("[FeideWindow] Requesting window close...");
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Feil ved lagring: {ex.Message}";
                Debug.WriteLine($"[FeideWindow] Error saving credentials: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveCredentialsToSettingsAsync()
        {
            try
            {
                Debug.WriteLine("[FeideWindow] Starting credential save process...");
                
                // 1. Save to secure storage
                await SecureCredentialStorage.SaveCredentialAsync("LoginEmail", FeideUsername);
                Debug.WriteLine($"[FeideWindow] ✓ Saved LoginEmail: {FeideUsername}");
                
                await SecureCredentialStorage.SaveCredentialAsync("LoginPassword", FeidePassword);
                Debug.WriteLine("[FeideWindow] ✓ Saved LoginPassword (hidden)");
                
                await SecureCredentialStorage.SaveCredentialAsync("SchoolName", SchoolName);
                Debug.WriteLine($"[FeideWindow] ✓ Saved SchoolName: {SchoolName}");

                // 2. Update settings.json to mark setup as complete
                var settings = new AppSettings
                {
                    InitialSetupCompleted = true,
                    LastUpdated = DateTime.Now
                };

                await SafeSettingsLoader.SaveSettingsSafelyAsync(settings);
                Debug.WriteLine("[FeideWindow] ✓ Updated settings.json - setup marked complete");
                
                Debug.WriteLine("[FeideWindow] All credentials saved successfully!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FeideWindow] FAILED to save credentials: {ex}");
                throw;
            }
        }

        private void ExitApplication()
        {
            Debug.WriteLine("[FeideWindow] User requested application exit");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        
        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
            Debug.WriteLine($"[FeideWindow] Password visibility toggled: {IsPasswordVisible}");
        }
    }
    
    public class FeideSetupCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public required string UserEmail { get; set; }
    }
}