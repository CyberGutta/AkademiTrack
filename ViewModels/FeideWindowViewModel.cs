using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Services;
using System.Diagnostics;
using System.Security;

namespace AkademiTrack.ViewModels
{
    public class FeideWindowViewModel : ViewModelBase, INotifyPropertyChanged, IDisposable
    {
        private readonly AnalyticsService _analyticsService;
        private string _schoolName = string.Empty;
        private string _feideUsername = string.Empty;
        private SecureString? _feidePasswordSecure = new SecureString();
        private string _errorMessage = string.Empty;
        private bool _isLoading = false;
        private bool _isPasswordVisible = false;
        private bool _isRestarting = false;

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FeideSetupCompletedEventArgs>? SetupCompleted;
        public event EventHandler? CloseRequested; // NEW: Event to close window

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FeideWindowViewModel()
        {
            // Get analytics service
            _analyticsService = ServiceLocator.Instance.GetService<AnalyticsService>();
            
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
            get => SecureStringToString(_feidePasswordSecure);
            set
            {
                _feidePasswordSecure?.Dispose();
                _feidePasswordSecure = StringToSecureString(value);
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

        public bool IsRestarting
        {
            get => _isRestarting;
            set
            {
                _isRestarting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SaveButtonText));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public char PasswordChar => IsPasswordVisible ? '\0' : '•';

        public ICommand TogglePasswordCommand { get; }

        public bool CanSave => !IsLoading && !IsRestarting &&
                               !string.IsNullOrWhiteSpace(SchoolName) &&
                               !string.IsNullOrWhiteSpace(FeideUsername) &&
                               !string.IsNullOrWhiteSpace(FeidePassword);

        public string SaveButtonText => IsRestarting ? "Starter appen på nytt..." : 
                                       IsLoading ? "Tester innlogging..." : 
                                       "Lagre og fortsett";

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

                Debug.WriteLine($"[FeideWindow] Testing credentials for user: {FeideUsername}");
                
                // Step 1: Save credentials temporarily (without marking setup as complete)
                await SaveCredentialsTemporarilyAsync();
                Debug.WriteLine("[FeideWindow] Credentials saved temporarily for testing");

                // Step 2: Test the credentials with AuthenticationService
                var authService = new AuthenticationService(null);
                var testResult = await authService.AuthenticateAsync();
                
                if (testResult.Success)
                {
                    Debug.WriteLine("[FeideWindow] ✓ Credentials test successful!");
                    
                    // Track successful Feide setup - removed events tracking
                    Debug.WriteLine("[FeideWindow] Feide setup completed successfully");
                    
                    // Step 3a: Mark setup as complete since credentials work
                    await MarkSetupAsCompleteAsync();
                    Debug.WriteLine("[FeideWindow] ✓ Setup marked as complete");

                    // Show restart message
                    IsRestarting = true;
                    Debug.WriteLine("[FeideWindow] Showing restart message to user");

                    // Notify success (this will trigger app restart)
                    SetupCompleted?.Invoke(this, new FeideSetupCompletedEventArgs
                    {
                        Success = true,
                        UserEmail = FeideUsername
                    });

                    Debug.WriteLine("[FeideWindow] Setup completed successfully - app will restart!");
                }
                else
                {
                    Debug.WriteLine("[FeideWindow] ❌ Credentials test failed!");
                    
                    // Track failed Feide setup - removed events tracking
                    Debug.WriteLine("[FeideWindow] Feide setup failed");
                    
                    // Log detailed error for developers
                    try
                    {
                        await _analyticsService.LogErrorAsync(
                            "feide_setup_authentication_failure",
                            testResult.ErrorMessage ?? "Feide authentication failed"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Analytics] Failed to log Feide setup failure: {ex.Message}");
                    }
                    
                    // Step 3b: Delete the invalid credentials
                    await DeleteCredentialsAsync();
                    Debug.WriteLine("[FeideWindow] Invalid credentials deleted");
                    
                    // Show error message to user
                    string errorMsg;
                    if (!string.IsNullOrEmpty(testResult.ErrorMessage))
                    {
                        errorMsg = testResult.ErrorMessage;
                    }
                    else
                    {
                        // Default message if no specific error
                        errorMsg = "Feil brukernavn eller passord. Vennligst sjekk dine Feide-innloggingsdata og prøv igjen.";
                    }
                    
                    ErrorMessage = errorMsg;
                    Debug.WriteLine($"[FeideWindow] Error shown to user: {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FeideWindow] Error during credential testing: {ex}");
                
                // Clean up any partially saved credentials
                try
                {
                    await DeleteCredentialsAsync();
                }
                catch { }
                
                ErrorMessage = $"Feil ved testing av innloggingsdata: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveCredentialsTemporarilyAsync()
        {
            try
            {
                Debug.WriteLine("[FeideWindow] Saving credentials temporarily for testing...");
                
                // Save credentials to secure storage (but don't mark setup as complete yet)
                await SecureCredentialStorage.SaveCredentialAsync("LoginEmail", FeideUsername);
                Debug.WriteLine($"[FeideWindow] ✓ Saved LoginEmail: {FeideUsername}");
                
                // Convert SecureString to plaintext temporarily for saving
                var passwordPlain = SecureStringToString(_feidePasswordSecure);
                await SecureCredentialStorage.SaveCredentialAsync("LoginPassword", passwordPlain);
                // Immediately clear the plaintext password
                passwordPlain = null;
                Debug.WriteLine("[FeideWindow] ✓ Saved LoginPassword (hidden)");
                
                await SecureCredentialStorage.SaveCredentialAsync("SchoolName", SchoolName);
                Debug.WriteLine($"[FeideWindow] ✓ Saved SchoolName: {SchoolName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FeideWindow] FAILED to save credentials temporarily: {ex}");
                throw;
            }
        }

        private async Task MarkSetupAsCompleteAsync()
        {
            try
            {
                // Update settings.json to mark setup as complete
                var settings = new AppSettings
                {
                    InitialSetupCompleted = true,
                    LastUpdated = DateTime.Now
                };

                await SafeSettingsLoader.SaveSettingsSafelyAsync(settings);
                Debug.WriteLine("[FeideWindow] ✓ Updated settings.json - setup marked complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FeideWindow] FAILED to mark setup as complete: {ex}");
                throw;
            }
        }

        private async Task DeleteCredentialsAsync()
        {
            try
            {
                Debug.WriteLine("[FeideWindow] Deleting invalid credentials...");
                
                // Delete credentials from secure storage
                await SecureCredentialStorage.DeleteCredentialAsync("LoginEmail");
                await SecureCredentialStorage.DeleteCredentialAsync("LoginPassword");
                await SecureCredentialStorage.DeleteCredentialAsync("SchoolName");
                
                // Also delete any cached cookies
                await SecureCredentialStorage.DeleteCookiesAsync();
                
                Debug.WriteLine("[FeideWindow] ✓ Invalid credentials deleted");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FeideWindow] Error deleting credentials: {ex}");
                // Don't throw here - this is cleanup
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

        #region SecureString Helpers
        
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
                ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secure);
                return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(ptr) ?? string.Empty;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(ptr);
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _feidePasswordSecure?.Dispose();
        }
    }
    
    public class FeideSetupCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public required string UserEmail { get; set; }
    }
}