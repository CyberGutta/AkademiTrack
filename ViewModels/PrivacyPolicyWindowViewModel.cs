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
    public class PrivacyPolicyWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private bool _hasAccepted;
        private bool _isLoading;
        private string _errorMessage = string.Empty;
        private string _userEmail;
        private string _latestVersion = "1.0";

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Accepted;
        public event EventHandler Exited;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public PrivacyPolicyWindowViewModel(string userEmail)
        {
            _userEmail = userEmail;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            AcceptCommand = new InlineCommand(async () => await AcceptPrivacyPolicyAsync(), () => CanAccept);
            ExitCommand = new InlineCommand(() => Exit());

            // Load the latest version on initialization
            _ = LoadLatestVersionAsync();
        }

        public bool HasAccepted
        {
            get => _hasAccepted;
            set
            {
                _hasAccepted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
                (AcceptCommand as InlineCommand)?.NotifyCanExecuteChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
                OnPropertyChanged(nameof(AcceptButtonText));
                (AcceptCommand as InlineCommand)?.NotifyCanExecuteChanged();
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

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public bool CanAccept => HasAccepted && !IsLoading;

        public string AcceptButtonText => IsLoading ? "Lagrer..." : "Godta og fortsett";

        public ICommand AcceptCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task LoadLatestVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/privacy-policy.json");
                var versionInfo = JsonSerializer.Deserialize<PrivacyPolicyVersionInfo>(response);

                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                {
                    _latestVersion = versionInfo.Version.Trim();
                    System.Diagnostics.Debug.WriteLine($"Latest privacy policy version: {_latestVersion}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading latest privacy policy version: {ex.Message}");
                // Keep default version if loading fails
            }
        }

        /// <summary>
        /// Checks if the user needs to accept the privacy policy (either never accepted or outdated version)
        /// </summary>
        public static async Task<bool> NeedsPrivacyPolicyAcceptance(string userEmail)
        {
            try
            {
                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                string normalizedEmail = userEmail?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(normalizedEmail))
                {
                    return true; // Require acceptance if email is invalid
                }

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    // Get latest version
                    string latestVersion = "1.0";
                    try
                    {
                        var versionResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/privacy-policy.json");
                        var versionInfo = JsonSerializer.Deserialize<PrivacyPolicyVersionInfo>(versionResponse);
                        if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                        {
                            latestVersion = versionInfo.Version.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading latest version: {ex.Message}");
                    }

                    System.Diagnostics.Debug.WriteLine($"Latest version from JSON: {latestVersion}");

                    // Check user's current acceptance status
                    var checkUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}&select=privacy_policy_accepted,privacy_policy_version";

                    var checkRequest = new HttpRequestMessage(HttpMethod.Get, checkUrl);
                    checkRequest.Headers.Add("apikey", supabaseKey);
                    checkRequest.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                    var checkResponse = await httpClient.SendAsync(checkRequest);
                    var checkContent = await checkResponse.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($"User profile check: {checkContent}");

                    if (!checkResponse.IsSuccessStatusCode || string.IsNullOrEmpty(checkContent) || checkContent == "[]")
                    {
                        return true; // Require acceptance if user not found
                    }

                    var profiles = JsonSerializer.Deserialize<UserProfile[]>(checkContent);
                    if (profiles == null || profiles.Length == 0)
                    {
                        return true;
                    }

                    var profile = profiles[0];

                    // User needs to accept if:
                    // 1. They haven't accepted at all (privacy_policy_accepted is false)
                    // 2. Their version is different from the latest version
                    bool needsAcceptance = !profile.PrivacyPolicyAccepted ||
                                          profile.PrivacyPolicyVersion != latestVersion;

                    System.Diagnostics.Debug.WriteLine($"User accepted: {profile.PrivacyPolicyAccepted}, User version: {profile.PrivacyPolicyVersion}, Latest: {latestVersion}, Needs acceptance: {needsAcceptance}");

                    // If user has old version, reset their acceptance
                    if (profile.PrivacyPolicyAccepted && profile.PrivacyPolicyVersion != latestVersion)
                    {
                        System.Diagnostics.Debug.WriteLine($"User has outdated version ({profile.PrivacyPolicyVersion}), resetting acceptance...");
                        await ResetPrivacyPolicyAcceptance(normalizedEmail, httpClient, supabaseUrl, supabaseKey);
                    }

                    return needsAcceptance;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking privacy policy status: {ex.Message}");
                return true; // Require acceptance on error to be safe
            }
        }

        private static async Task ResetPrivacyPolicyAcceptance(string normalizedEmail, HttpClient httpClient, string supabaseUrl, string supabaseKey)
        {
            try
            {
                var resetData = new
                {
                    privacy_policy_accepted = false,
                    privacy_policy_accepted_at = (string)null
                    // Don't update version - keep old version to track what they had
                };

                var jsonContent = JsonSerializer.Serialize(resetData);
                var updateUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}";

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl)
                {
                    Content = content
                };
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                request.Headers.Add("Prefer", "return=representation");

                var response = await httpClient.SendAsync(request);
                System.Diagnostics.Debug.WriteLine($"Reset acceptance response: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting privacy policy: {ex.Message}");
            }
        }

        private async Task AcceptPrivacyPolicyAsync()
        {
            if (!HasAccepted)
            {
                ErrorMessage = "Du må godta personvernserklæringen for å fortsette.";
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                bool success = await SavePrivacyPolicyAcceptance(_userEmail);

                if (success)
                {
                    Accepted?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ErrorMessage = "Kunne ikke lagre godkjenningen. Vennligst prøv igjen.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"En feil oppstod: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Privacy policy acceptance error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<bool> SavePrivacyPolicyAcceptance(string email)
        {
            try
            {
                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                string normalizedEmail = email?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(normalizedEmail))
                {
                    System.Diagnostics.Debug.WriteLine("Error: Email is null or empty");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"=== PRIVACY POLICY UPDATE START ===");
                System.Diagnostics.Debug.WriteLine($"Email to update: {normalizedEmail}");
                System.Diagnostics.Debug.WriteLine($"Version to set: {_latestVersion}");

                // First verify the record exists
                var checkUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}&select=id,user_email,privacy_policy_accepted";

                var checkRequest = new HttpRequestMessage(HttpMethod.Get, checkUrl);
                checkRequest.Headers.Add("apikey", supabaseKey);
                checkRequest.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                System.Diagnostics.Debug.WriteLine($"Checking if record exists: {checkUrl}");
                var checkResponse = await _httpClient.SendAsync(checkRequest);
                var checkContent = await checkResponse.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Check response: {checkResponse.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Check content: {checkContent}");

                if (!checkResponse.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to check record existence");
                    return false;
                }

                if (string.IsNullOrEmpty(checkContent) || checkContent == "[]")
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: No record found with email {normalizedEmail}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"Record exists, proceeding with update...");

                // Now perform the update with the latest version
                var updateData = new
                {
                    privacy_policy_accepted = true,
                    privacy_policy_accepted_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    privacy_policy_version = _latestVersion
                };

                var jsonContent = JsonSerializer.Serialize(updateData);
                System.Diagnostics.Debug.WriteLine($"Update payload: {jsonContent}");

                var updateUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}";
                System.Diagnostics.Debug.WriteLine($"Update URL: {updateUrl}");

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl)
                {
                    Content = content
                };
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                request.Headers.Add("Prefer", "return=representation");

                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Update Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Update Response Content: {responseContent}");
                System.Diagnostics.Debug.WriteLine($"Response Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Update failed with status {response.StatusCode}");
                    return false;
                }

                // Check if any rows were actually updated
                if (string.IsNullOrEmpty(responseContent) || responseContent == "[]")
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Update returned success but NO ROWS WERE UPDATED!");
                    System.Diagnostics.Debug.WriteLine($"This is likely an RLS policy issue blocking the update.");
                    System.Diagnostics.Debug.WriteLine($"Check your 'Allow updates for privacy policy acceptance' RLS policy.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"SUCCESS: Privacy policy updated to version {_latestVersion}!");
                System.Diagnostics.Debug.WriteLine($"Updated record: {responseContent}");
                System.Diagnostics.Debug.WriteLine($"=== PRIVACY POLICY UPDATE END ===");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving privacy policy acceptance: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private void Exit()
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public class PrivacyPolicyVersionInfo
        {
            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonPropertyName("lastUpdated")]
            public string LastUpdated { get; set; }

            [JsonPropertyName("effectiveDate")]
            public string EffectiveDate { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }

            [JsonPropertyName("markdownUrl")]
            public string MarkdownUrl { get; set; }
        }

        public class UserProfile
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("user_id")]
            public string UserId { get; set; }

            [JsonPropertyName("privacy_policy_accepted")]
            public bool PrivacyPolicyAccepted { get; set; }

            [JsonPropertyName("privacy_policy_version")]
            public string PrivacyPolicyVersion { get; set; }

            [JsonPropertyName("privacy_policy_accepted_at")]
            public string PrivacyPolicyAcceptedAt { get; set; }
        }
    }
}