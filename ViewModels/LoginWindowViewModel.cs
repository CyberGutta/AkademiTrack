using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class LoginWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading = false;

        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<LoginCompletedEventArgs>? LoginCompleted;

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LoginWindowViewModel()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            LoginCommand = new InlineCommand(async () => await LoginAsync(), () => CanLogin);
            ExitCommand = new InlineCommand(() => ExitAsync());
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                (LoginCommand as InlineCommand)?.NotifyCanExecuteChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                (LoginCommand as InlineCommand)?.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(CanLogin));
                OnPropertyChanged(nameof(LoginButtonText));
                (LoginCommand as InlineCommand)?.NotifyCanExecuteChanged();
            }
        }

        public bool CanLogin => !IsLoading && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

        public string LoginButtonText => IsLoading ? "Logger inn..." : "Logg inn";

        public ICommand LoginCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task LoginAsync()
        {
            if (!CanLogin) return;

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var result = await ValidateUserAsync(Email, Password);

                if (result.IsValid && result.FoundRecord != null)
                {
                    await SaveLoginStatusAsync(result.FoundRecord.Email);

                    bool needsPrivacyAcceptance = await PrivacyPolicyWindowViewModel.NeedsPrivacyPolicyAcceptance(result.FoundRecord.Email);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LoginCompleted?.Invoke(this, new LoginCompletedEventArgs
                        {
                            Success = true,
                            UserEmail = result.FoundRecord.Email,
                            NeedsPrivacyAcceptance = needsPrivacyAcceptance
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Feil: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<LoginValidationResult> ValidateUserAsync(string email, string password)
        {
            try
            {
                var url = $"{_supabaseUrl}/auth/v1/token?grant_type=password";
                var payload = new
                {
                    email = email,
                    password = password
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Auth Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Auth Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    ErrorMessage = "Feil brukernavn eller passord.";
                    return new LoginValidationResult { IsValid = false };
                }

                var result = JsonSerializer.Deserialize<LoginResponse>(responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return new LoginValidationResult
                {
                    IsValid = true,
                    FoundRecord = new UserRecord
                    {
                        Email = result?.User?.Email ?? ""
                    }
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Feil ved pålogging: {ex.Message}";
                return new LoginValidationResult { IsValid = false };
            }
        }

        public class LoginResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("user")]
            public SupabaseUser User { get; set; }
        }

        public class SupabaseUser
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("email")]
            public string Email { get; set; }
        }

        private async Task SaveLoginStatusAsync(string email)
        {
            try
            {
                var loginData = new
                {
                    IsLoggedIn = true,
                    Email = email,
                    LoggedInAt = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(loginData, new JsonSerializerOptions { WriteIndented = true });
                string appDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkademiTrack");
                System.IO.Directory.CreateDirectory(appDataDir);
                string loginPath = System.IO.Path.Combine(appDataDir, "login.json");

                await System.IO.File.WriteAllTextAsync(loginPath, json);
                System.Diagnostics.Debug.WriteLine($"Login status saved to: {loginPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save login status: {ex.Message}");
            }
        }

        private void ExitAsync()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class LoginCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public required string UserEmail { get; set; }
        public bool NeedsPrivacyAcceptance { get; set; }
    }



    public class LoginSession
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("loggedInAt")]
        public DateTime LoggedInAt { get; set; }
    }

    public class InlineCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public InlineCommand(Action execute, Func<bool> canExecute = null!)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class LoginValidationResult
    {
        public bool IsValid { get; set; }
        public UserRecord? FoundRecord { get; set; }
    }

    public class UserRecord
    {
        [JsonPropertyName("email")]
        public required string Email { get; set; }

    }
}
