using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.ComponentModel;
using System.Linq;
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

        // Supabase configuration - same as in MainWindowViewModel
        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<bool> LoginCompleted;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LoginWindowViewModel()
        {
            _httpClient = new HttpClient();
            // Set a reasonable timeout to prevent hanging
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            LoginCommand = new SimpleCommand(async () => await LoginAsync(), () => CanLogin);
            ExitCommand = new SimpleCommand(ExitAsync);
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
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
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
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
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
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
                // Attempt to authenticate with Supabase
                bool isAuthenticated = await AuthenticateWithSupabaseAsync(Email, Password);

                if (isAuthenticated)
                {
                    // Save activation status locally
                    await SaveActivationStatusAsync();

                    // Notify success and close window
                    LoginCompleted?.Invoke(this, true);
                }
                else
                {
                    ErrorMessage = "Ugyldig e-post eller passord. Sjekk at du har en gyldig konto.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Innlogging feilet: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<bool> AuthenticateWithSupabaseAsync(string email, string password)
        {
            try
            {
                // Use the correct Supabase Auth API endpoint and format
                var payload = new
                {
                    email = email,
                    password = password
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Use the correct token endpoint with grant_type=password
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/auth/v1/token?grant_type=password")
                {
                    Content = content
                };

                // Add required headers - these must be exactly right
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");

                // Add additional headers that the JavaScript client includes
                request.Headers.Add("X-Client-Info", "supabase-csharp/0.0.1");

                // Make sure Content-Type is set correctly
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                System.Diagnostics.Debug.WriteLine($"Making request to: {request.RequestUri}");
                System.Diagnostics.Debug.WriteLine($"Request payload: {json}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Auth Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Auth Response Content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = JsonSerializer.Deserialize<SupabaseAuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Check if we got a valid access token
                    bool success = !string.IsNullOrEmpty(authResponse?.AccessToken);
                    System.Diagnostics.Debug.WriteLine($"Authentication success: {success}");
                    System.Diagnostics.Debug.WriteLine($"Access token received: {!string.IsNullOrEmpty(authResponse?.AccessToken)}");
                    return success;
                }
                else
                {
                    // Parse error response
                    System.Diagnostics.Debug.WriteLine($"Authentication failed with status: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");

                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<SupabaseErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        string errorMsg = errorResponse?.ErrorDescription ?? errorResponse?.Message ?? errorResponse?.Error ?? "Authentication failed";
                        System.Diagnostics.Debug.WriteLine($"Parsed error message: {errorMsg}");
                        ErrorMessage = ConvertErrorMessage(errorMsg);
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse error response: {parseEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"Raw error response: {responseContent}");
                        ErrorMessage = "Autentisering feilet - sjekk innloggingsdetaljene dine";
                    }

                    return false;
                }
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP error during authentication: {httpEx.Message}");
                ErrorMessage = "Nettverksfeil - sjekk internettforbindelsen din";
                return false;
            }
            catch (TaskCanceledException timeoutEx)
            {
                System.Diagnostics.Debug.WriteLine($"Request timeout during authentication: {timeoutEx.Message}");
                ErrorMessage = "Forespørsel tidsavbrudd - prøv igjen";
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error during authentication: {ex}");
                ErrorMessage = "Innlogging feilet - prøv igjen senere";
                return false;
            }
        }

        private string ConvertErrorMessage(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return "Innlogging feilet - sjekk e-post og passord";

            // Convert common Supabase error messages to Norwegian
            return errorMessage.ToLower() switch
            {
                var msg when msg.Contains("invalid login credentials") || msg.Contains("invalid_grant") => "Ugyldig e-post eller passord",
                var msg when msg.Contains("email not confirmed") => "E-post ikke bekreftet - sjekk e-posten din",
                var msg when msg.Contains("user not found") => "Bruker ikke funnet",
                var msg when msg.Contains("invalid email") => "Ugyldig e-postadresse",
                var msg when msg.Contains("password") && msg.Contains("wrong") => "Passord er feil",
                var msg when msg.Contains("too many requests") || msg.Contains("rate") => "For mange forsøk - vent litt før du prøver igjen",
                var msg when msg.Contains("network") => "Nettverksfeil - sjekk internettforbindelsen",
                var msg when msg.Contains("timeout") => "Tidsavbrudd - prøv igjen",
                _ => $"Innlogging feilet: {errorMessage}"
            };
        }

        private async Task SaveActivationStatusAsync()
        {
            try
            {
                var activationData = new
                {
                    IsActivated = true,
                    ActivatedAt = DateTime.UtcNow,
                    Email = Email // Store email for reference, but not password
                };

                var json = JsonSerializer.Serialize(activationData, new JsonSerializerOptions { WriteIndented = true });

                // Save to the same directory as other settings
                string appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                System.IO.Directory.CreateDirectory(appDataDir);
                string activationPath = System.IO.Path.Combine(appDataDir, "activation.json");

                await System.IO.File.WriteAllTextAsync(activationPath, json);

                System.Diagnostics.Debug.WriteLine($"Activation status saved to: {activationPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save activation status: {ex.Message}");
                // Don't throw - activation was successful, just saving failed
            }
        }

        private async Task ExitAsync()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class SupabaseAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("user")]
        public SupabaseUser User { get; set; }
    }

    public class SupabaseUser
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastSignInAt { get; set; }
        public DateTime? EmailConfirmedAt { get; set; }
        public DateTime? PhoneConfirmedAt { get; set; }
        public SupabaseUserMetadata UserMetadata { get; set; }
        public SupabaseUserMetadata AppMetadata { get; set; }
    }

    public class SupabaseUserMetadata
    {
        // This can contain custom user data
        // For basic usage, you might not need specific properties
        // or you can add properties based on your needs
    }

    public class SupabaseErrorResponse
    {
        public string Error { get; set; }
        public string ErrorDescription { get; set; }
        public string Message { get; set; }
    }
}