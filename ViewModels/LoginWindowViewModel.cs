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
        private string _activationKey = string.Empty;
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

        public string ActivationKey
        {
            get => _activationKey;
            set
            {
                _activationKey = value;
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

        public bool CanLogin => !IsLoading && !string.IsNullOrWhiteSpace(ActivationKey);

        public string LoginButtonText => IsLoading ? "Aktiverer..." : "Aktiver";

        public ICommand LoginCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task LoginAsync()
        {
            if (!CanLogin) return;

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                // Step by step validation with detailed feedback
                var result = await ValidateActivationKeyAsync(ActivationKey);

                if (result.IsValid)
                {
                    // Mark the key as used and save activation status locally
                    await MarkActivationKeyAsUsedAsync(result.FoundRecord.ActivationKey);
                    await SaveActivationStatusAsync(result.FoundRecord.UserEmail);

                    // Notify success and close window
                    LoginCompleted?.Invoke(this, true);
                }
                // ErrorMessage is already set in ValidateActivationKeyAsync
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Feil: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Activation error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<ValidationResult> ValidateActivationKeyAsync(string activationKey)
        {
            try
            {
                string cleanKey = activationKey?.Trim();

                System.Diagnostics.Debug.WriteLine($"=== ACTIVATION VALIDATION START ===");
                System.Diagnostics.Debug.WriteLine($"Input Key: '{cleanKey}'");

                var url = $"{_supabaseUrl}/rest/v1/activation_keys?select=*";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
                request.Headers.Add("Accept", "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Database Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Database Error: {responseContent}");
                    ErrorMessage = "Kunne ikke koble til database";
                    return new ValidationResult { IsValid = false };
                }

                var allRecords = JsonSerializer.Deserialize<ActivationKeyRecord[]>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                System.Diagnostics.Debug.WriteLine($"Total records in database: {allRecords?.Length ?? 0}");

                if (allRecords == null || allRecords.Length == 0)
                {
                    ErrorMessage = "Ingen aktiveringsnøkler funnet i systemet";
                    return new ValidationResult { IsValid = false };
                }

                // Log all records for debugging
                System.Diagnostics.Debug.WriteLine("=== ALL DATABASE RECORDS ===");
                foreach (var record in allRecords)
                {
                    System.Diagnostics.Debug.WriteLine($"ID: {record.Id}, Email: '{record.UserEmail}', Key: '{record.ActivationKey}', IsActivated: {record.IsActivated}");
                }

                // Find exact key match
                var exactMatch = allRecords.FirstOrDefault(r =>
                    string.Equals(r.ActivationKey?.Trim(), cleanKey, StringComparison.OrdinalIgnoreCase));

                if (exactMatch == null)
                {
                    System.Diagnostics.Debug.WriteLine($"No activation key match found for: '{cleanKey}'");
                    ErrorMessage = "Aktiveringsnøkkelen finnes ikke i systemet";
                    return new ValidationResult { IsValid = false };
                }

                // Check if already activated
                if (exactMatch.IsActivated)
                {
                    System.Diagnostics.Debug.WriteLine($"Key already activated at: {exactMatch.ActivatedAt}");
                    ErrorMessage = "Denne aktiveringsnøkkelen er allerede brukt";
                    return new ValidationResult { IsValid = false };
                }

                System.Diagnostics.Debug.WriteLine($"SUCCESS: Valid unused activation key found!");
                System.Diagnostics.Debug.WriteLine($"Associated email: {exactMatch.UserEmail}");
                System.Diagnostics.Debug.WriteLine($"=== ACTIVATION VALIDATION END ===");

                return new ValidationResult { IsValid = true, FoundRecord = exactMatch };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VALIDATION ERROR: {ex}");
                ErrorMessage = $"Valideringsfeil: {ex.Message}";
                return new ValidationResult { IsValid = false };
            }
        }

        private async Task MarkActivationKeyAsUsedAsync(string activationKey)
        {
            try
            {
                // Update the activation key to mark it as used - using the exact key match
                var updateData = new
                {
                    is_activated = true,
                    activated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Use exact match for the key from the database
                var url = $"{_supabaseUrl}/rest/v1/activation_keys?activation_key=eq.{Uri.EscapeDataString(activationKey.Trim())}";

                var request = new HttpRequestMessage(HttpMethod.Patch, url)
                {
                    Content = content
                };

                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
                request.Headers.Add("Prefer", "return=minimal");

                System.Diagnostics.Debug.WriteLine($"Marking activation key as used: {url}");
                System.Diagnostics.Debug.WriteLine($"Update payload: {json}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Mark as used Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Mark as used Response Content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully marked activation key as used");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to mark activation key as used: {response.StatusCode}");
                    // Don't throw here - the validation was successful, we just couldn't update the status
                    // This prevents double usage but doesn't block the user if there's a temporary issue
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking activation key as used: {ex.Message}");
                // Don't throw - validation was successful, just updating failed
            }
        }

        private async Task SaveActivationStatusAsync(string associatedEmail)
        {
            try
            {
                var activationData = new
                {
                    IsActivated = true,
                    ActivatedAt = DateTime.UtcNow,
                    Email = associatedEmail, // Use the email from the database record
                    ActivationKey = ActivationKey // Store for reference
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

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public ActivationKeyRecord FoundRecord { get; set; }
    }

    public class ActivationKeyRecord
    {
        public int Id { get; set; }

        [JsonPropertyName("user_email")]
        public string UserEmail { get; set; }

        [JsonPropertyName("activation_key")]
        public string ActivationKey { get; set; }

        [JsonPropertyName("is_activated")]
        public bool IsActivated { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("activated_at")]
        public DateTime? ActivatedAt { get; set; }
    }

    public class SupabaseErrorResponse
    {
        public string Error { get; set; }
        public string ErrorDescription { get; set; }
        public string Message { get; set; }
        public string Code { get; set; }
        public string Details { get; set; }
        public string Hint { get; set; }
    }
}