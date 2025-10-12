using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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

        public string ActivationKey
        {
            get => _activationKey;
            set
            {
                _activationKey = value;
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
                var result = await ValidateActivationKeyAsync(ActivationKey);

                if (result.IsValid && result.FoundRecord != null)
                {
                    await MarkActivationKeyAsUsedAsync(result.FoundRecord.ActivationKey);
                    await SaveActivationStatusAsync(result.FoundRecord.UserEmail);

                    System.Diagnostics.Debug.WriteLine($"Checking privacy policy for: {result.FoundRecord.UserEmail}");
                    bool needsPrivacyAcceptance = await PrivacyPolicyWindowViewModel.NeedsPrivacyPolicyAcceptance(result.FoundRecord.UserEmail);

                    System.Diagnostics.Debug.WriteLine($"Needs privacy acceptance: {needsPrivacyAcceptance}");

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LoginCompleted?.Invoke(this, new LoginCompletedEventArgs
                        {
                            Success = true,
                            UserEmail = result.FoundRecord.UserEmail,
                            NeedsPrivacyAcceptance = needsPrivacyAcceptance
                        });
                    });
                }
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
                string cleanKey = activationKey.Trim();

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

                System.Diagnostics.Debug.WriteLine("=== ALL DATABASE RECORDS ===");
                foreach (var record in allRecords)
                {
                    System.Diagnostics.Debug.WriteLine($"ID: {record.Id}, Email: '{record.UserEmail}', Key: '{record.ActivationKey}', IsActivated: {record.IsActivated}");
                }

                var exactMatch = allRecords.FirstOrDefault(r =>
                    string.Equals(r.ActivationKey?.Trim(), cleanKey, StringComparison.OrdinalIgnoreCase));

                if (exactMatch == null)
                {
                    System.Diagnostics.Debug.WriteLine($"No activation key match found for: '{cleanKey}'");
                    ErrorMessage = "Aktiveringsnøkkelen finnes ikke i systemet";
                    return new ValidationResult { IsValid = false };
                }

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
                var updateData = new
                {
                    is_activated = true,
                    activated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

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
                    
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking activation key as used: {ex.Message}");
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
                    Email = associatedEmail, 
                    ActivationKey = ActivationKey 
                };

                var json = JsonSerializer.Serialize(activationData, new JsonSerializerOptions { WriteIndented = true });

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
            }
        }

        private void ExitAsync()
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

    public class LoginCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public required string UserEmail { get; set; }
        public bool NeedsPrivacyAcceptance { get; set; }
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

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public ActivationKeyRecord? FoundRecord { get; set; }
    }

    public class ActivationKeyRecord
    {
        public int Id { get; set; }

        [JsonPropertyName("user_email")]
        public required string UserEmail { get; set; }

        [JsonPropertyName("activation_key")]
        public required string ActivationKey { get; set; }

        [JsonPropertyName("is_activated")]
        public bool IsActivated { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("activated_at")]
        public DateTime? ActivatedAt { get; set; }
    }
}