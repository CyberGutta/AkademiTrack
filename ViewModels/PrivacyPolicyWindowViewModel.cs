using System;
using System.Collections.Generic;
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
        private bool _hasAcceptedPrivacy;
        private bool _hasAcceptedTerms;
        private bool _isLoading;
        private string _errorMessage = string.Empty;
        private string _userEmail;

        private string _latestPrivacyVersion = "1.1";
        private string? _userCurrentPrivacyVersion = null;
        private List<string> _privacyChangelogItems = new List<string>();
        private bool _isPrivacyUpgrade = false;

        private string _latestTermsVersion = "1.1";
        private string? _userCurrentTermsVersion = null;
        private List<string> _termsChangelogItems = new List<string>();
        private bool _isTermsUpgrade = false;

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? Accepted;
        public event EventHandler? Exited;

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public PrivacyPolicyWindowViewModel(string userEmail)
        {
            _userEmail = userEmail;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _isLoading = true; 

            AcceptCommand = new InlineCommand(async () => await AcceptBothAsync(), () => CanAccept);
            ExitCommand = new InlineCommand(() => Exit());

            _ = LoadVersionsAndCheckUpgradesAsync();

            System.Diagnostics.Debug.WriteLine($"=== VIEWMODEL CREATED ===");
            System.Diagnostics.Debug.WriteLine($"Initial HasAcceptedPrivacy: {HasAcceptedPrivacy}");
            System.Diagnostics.Debug.WriteLine($"Initial HasAcceptedTerms: {HasAcceptedTerms}");
            System.Diagnostics.Debug.WriteLine($"=======================");
        }

        #region Privacy Policy Properties
        public bool HasAcceptedPrivacy
        {
            get => _hasAcceptedPrivacy;
            set
            {
                _hasAcceptedPrivacy = value;
                System.Diagnostics.Debug.WriteLine($"HasAcceptedPrivacy changed to: {value}");
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
                (AcceptCommand as InlineCommand)?.NotifyCanExecuteChanged();
            }
        }

        public bool IsPrivacyUpgrade
        {
            get => _isPrivacyUpgrade;
            set
            {
                _isPrivacyUpgrade = value;
                OnPropertyChanged();
            }
        }

        public List<string> PrivacyChangelogItems
        {
            get => _privacyChangelogItems;
            set
            {
                _privacyChangelogItems = value;
                OnPropertyChanged();
            }
        }

        public string? UserCurrentPrivacyVersion
        {
            get => _userCurrentPrivacyVersion;
            set
            {
                _userCurrentPrivacyVersion = value;
                OnPropertyChanged();
            }
        }

        public string LatestPrivacyVersion
        {
            get => _latestPrivacyVersion;
            set
            {
                _latestPrivacyVersion = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Terms of Use Properties
        public bool HasAcceptedTerms
        {
            get => _hasAcceptedTerms;
            set
            {
                _hasAcceptedTerms = value;
                System.Diagnostics.Debug.WriteLine($"HasAcceptedTerms changed to: {value}");
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
                (AcceptCommand as InlineCommand)?.NotifyCanExecuteChanged();
            }
        }

        public bool IsTermsUpgrade
        {
            get => _isTermsUpgrade;
            set
            {
                _isTermsUpgrade = value;
                OnPropertyChanged();
            }
        }

        public List<string> TermsChangelogItems
        {
            get => _termsChangelogItems;
            set
            {
                _termsChangelogItems = value;
                OnPropertyChanged();
            }
        }

        public string? UserCurrentTermsVersion
        {
            get => _userCurrentTermsVersion;
            set
            {
                _userCurrentTermsVersion = value;
                OnPropertyChanged();
            }
        }

        public string LatestTermsVersion
        {
            get => _latestTermsVersion;
            set
            {
                _latestTermsVersion = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Common Properties
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
                OnPropertyChanged(nameof(AcceptButtonText));
                OnPropertyChanged(nameof(IsContentReady));
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

        public bool NeedsPrivacyConsent
        {
            get
            {
                if (string.IsNullOrEmpty(_userCurrentPrivacyVersion))
                    return true;

                return _userCurrentPrivacyVersion != _latestPrivacyVersion;
            }
        }

        public bool NeedsTermsConsent
        {
            get
            {
                if (string.IsNullOrEmpty(_userCurrentTermsVersion))
                    return true;

                return _userCurrentTermsVersion != _latestTermsVersion;
            }
        }

        public bool ShowBothSections => NeedsPrivacyConsent && NeedsTermsConsent;

        public bool IsContentReady => !IsLoading;

        public bool CanAccept
        {
            get
            {
                if (IsLoading) return false;

                bool privacyOk = !NeedsPrivacyConsent || HasAcceptedPrivacy;
                bool termsOk = !NeedsTermsConsent || HasAcceptedTerms;

                bool result = privacyOk && termsOk;

                System.Diagnostics.Debug.WriteLine($"CanAccept: IsLoading={IsLoading}, Privacy OK={privacyOk} (needs={NeedsPrivacyConsent}, has={HasAcceptedPrivacy}), Terms OK={termsOk} (needs={NeedsTermsConsent}, has={HasAcceptedTerms}), Result={result}");

                return result;
            }
        }

        public string AcceptButtonText => IsLoading ? "Lagrer..." : "Godta og fortsett";

        public ICommand AcceptCommand { get; }
        public ICommand ExitCommand { get; }
        #endregion

        private async Task LoadVersionsAndCheckUpgradesAsync()
        {
            try
            {
                await GetUserCurrentVersionsAsync();

                System.Diagnostics.Debug.WriteLine($"=== AFTER DB CHECK ===");
                System.Diagnostics.Debug.WriteLine($"User Privacy Version: '{_userCurrentPrivacyVersion}'");
                System.Diagnostics.Debug.WriteLine($"User Terms Version: '{_userCurrentTermsVersion}'");

                await LoadPrivacyVersionAsync();

                System.Diagnostics.Debug.WriteLine($"Latest Privacy Version: '{_latestPrivacyVersion}'");
                System.Diagnostics.Debug.WriteLine($"Needs Privacy Consent: {NeedsPrivacyConsent}");

                await LoadTermsVersionAsync();

                System.Diagnostics.Debug.WriteLine($"Latest Terms Version: '{_latestTermsVersion}'");
                System.Diagnostics.Debug.WriteLine($"Needs Terms Consent: {NeedsTermsConsent}");
                System.Diagnostics.Debug.WriteLine($"===================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading versions: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(NeedsPrivacyConsent));
                OnPropertyChanged(nameof(NeedsTermsConsent));
                OnPropertyChanged(nameof(ShowBothSections));
                OnPropertyChanged(nameof(CanAccept));
                (AcceptCommand as InlineCommand)?.NotifyCanExecuteChanged();

                System.Diagnostics.Debug.WriteLine($"=== LOADING COMPLETE ===");
                System.Diagnostics.Debug.WriteLine($"NeedsPrivacyConsent: {NeedsPrivacyConsent}");
                System.Diagnostics.Debug.WriteLine($"NeedsTermsConsent: {NeedsTermsConsent}");
            }
        }

        private async Task LoadPrivacyVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/privacy-policy.json");
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(response);

                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                {
                    LatestPrivacyVersion = versionInfo.Version.Trim();
                    System.Diagnostics.Debug.WriteLine($"Latest privacy policy version: {_latestPrivacyVersion}");

                    if (!string.IsNullOrEmpty(_userCurrentPrivacyVersion) && _userCurrentPrivacyVersion != _latestPrivacyVersion)
                    {
                        IsPrivacyUpgrade = true;

                        if (versionInfo.Changelog != null && versionInfo.Changelog.ContainsKey(_latestPrivacyVersion))
                        {
                            PrivacyChangelogItems = versionInfo.Changelog[_latestPrivacyVersion];
                            System.Diagnostics.Debug.WriteLine($"Loaded {_privacyChangelogItems.Count} privacy changelog items");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading privacy policy version: {ex.Message}");
            }
        }

        private async Task LoadTermsVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/terms-of-use.json");
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(response);

                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                {
                    LatestTermsVersion = versionInfo.Version.Trim();
                    System.Diagnostics.Debug.WriteLine($"Latest terms of use version: {_latestTermsVersion}");

                    if (!string.IsNullOrEmpty(_userCurrentTermsVersion) && _userCurrentTermsVersion != _latestTermsVersion)
                    {
                        IsTermsUpgrade = true;

                        if (versionInfo.Changelog != null && versionInfo.Changelog.ContainsKey(_latestTermsVersion))
                        {
                            TermsChangelogItems = versionInfo.Changelog[_latestTermsVersion];
                            System.Diagnostics.Debug.WriteLine($"Loaded {_termsChangelogItems.Count} terms changelog items");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading terms of use version: {ex.Message}");
            }
        }

        private async Task GetUserCurrentVersionsAsync()
        {
            try
            {
                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                string? normalizedEmail = _userEmail?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(normalizedEmail))
                    return;

                var checkUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}&select=privacy_policy_version,terms_of_use_version";

                var checkRequest = new HttpRequestMessage(HttpMethod.Get, checkUrl);
                checkRequest.Headers.Add("apikey", supabaseKey);
                checkRequest.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                var checkResponse = await _httpClient.SendAsync(checkRequest);
                var checkContent = await checkResponse.Content.ReadAsStringAsync();

                if (checkResponse.IsSuccessStatusCode && !string.IsNullOrEmpty(checkContent) && checkContent != "[]")
                {
                    var profiles = JsonSerializer.Deserialize<UserProfile[]>(checkContent);
                    if (profiles != null && profiles.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(profiles[0].PrivacyPolicyVersion))
                        {
                            UserCurrentPrivacyVersion = profiles[0].PrivacyPolicyVersion;
                            System.Diagnostics.Debug.WriteLine($"User's current privacy policy version: {_userCurrentPrivacyVersion}");
                            OnPropertyChanged(nameof(NeedsPrivacyConsent));
                        }

                        if (!string.IsNullOrEmpty(profiles[0].TermsOfUseVersion))
                        {
                            UserCurrentTermsVersion = profiles[0].TermsOfUseVersion;
                            System.Diagnostics.Debug.WriteLine($"User's current terms of use version: {_userCurrentTermsVersion}");
                            OnPropertyChanged(nameof(NeedsTermsConsent));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting user's current versions: {ex.Message}");
            }
        }
 
        public static async Task<bool> NeedsPrivacyPolicyAcceptance(string userEmail)
        {
            try
            {
                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                string? normalizedEmail = userEmail?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(normalizedEmail))
                {
                    return true;
                }

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    string latestPrivacyVersion = "1.0";
                    string latestTermsVersion = "1.0";

                    try
                    {
                        var privacyResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/privacy-policy.json");
                        var privacyInfo = JsonSerializer.Deserialize<VersionInfo>(privacyResponse);
                        if (privacyInfo != null && !string.IsNullOrEmpty(privacyInfo.Version))
                        {
                            latestPrivacyVersion = privacyInfo.Version.Trim();
                        }

                        var termsResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/terms-of-use.json");
                        var termsInfo = JsonSerializer.Deserialize<VersionInfo>(termsResponse);
                        if (termsInfo != null && !string.IsNullOrEmpty(termsInfo.Version))
                        {
                            latestTermsVersion = termsInfo.Version.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading latest versions: {ex.Message}");
                    }

                    var checkUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}&select=privacy_policy_accepted,privacy_policy_version,terms_of_use_accepted,terms_of_use_version";

                    var checkRequest = new HttpRequestMessage(HttpMethod.Get, checkUrl);
                    checkRequest.Headers.Add("apikey", supabaseKey);
                    checkRequest.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                    var checkResponse = await httpClient.SendAsync(checkRequest);
                    var checkContent = await checkResponse.Content.ReadAsStringAsync();

                    if (!checkResponse.IsSuccessStatusCode || string.IsNullOrEmpty(checkContent) || checkContent == "[]")
                    {
                        return true;
                    }

                    var profiles = JsonSerializer.Deserialize<UserProfile[]>(checkContent);
                    if (profiles == null || profiles.Length == 0)
                    {
                        return true;
                    }

                    var profile = profiles[0];

                   
                    bool needsPrivacy = !profile.PrivacyPolicyAccepted || profile.PrivacyPolicyVersion != latestPrivacyVersion;
                    bool needsTerms = !profile.TermsOfUseAccepted || profile.TermsOfUseVersion != latestTermsVersion;

                    bool needsAcceptance = needsPrivacy || needsTerms;

                    System.Diagnostics.Debug.WriteLine($"Privacy accepted: {profile.PrivacyPolicyAccepted}, version: {profile.PrivacyPolicyVersion}, latest: {latestPrivacyVersion}");
                    System.Diagnostics.Debug.WriteLine($"Terms accepted: {profile.TermsOfUseAccepted}, version: {profile.TermsOfUseVersion}, latest: {latestTermsVersion}");
                    System.Diagnostics.Debug.WriteLine($"Needs acceptance: {needsAcceptance}");
                    System.Diagnostics.Debug.WriteLine($"Needs privacy acceptance: {needsPrivacy}");

                    if (needsPrivacy && profile.PrivacyPolicyAccepted)
                    {
                        await ResetPrivacyAcceptance(normalizedEmail, httpClient, supabaseUrl, supabaseKey);
                    }

                    if (needsTerms && profile.TermsOfUseAccepted)
                    {
                        await ResetTermsAcceptance(normalizedEmail, httpClient, supabaseUrl, supabaseKey);
                    }

                    return needsAcceptance;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking acceptance status: {ex.Message}");
                return true;
            }
        }

        private static async Task ResetPrivacyAcceptance(string normalizedEmail, HttpClient httpClient, string supabaseUrl, string supabaseKey)
        {
            try
            {
                var resetData = new
                {
                    privacy_policy_accepted = false,
                    privacy_policy_accepted_at = (string?)null
                };

                var jsonContent = JsonSerializer.Serialize(resetData);
                var updateUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}";

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl) { Content = content };
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting privacy acceptance: {ex.Message}");
            }
        }

        private static async Task ResetTermsAcceptance(string normalizedEmail, HttpClient httpClient, string supabaseUrl, string supabaseKey)
        {
            try
            {
                var resetData = new
                {
                    terms_of_use_accepted = false,
                    terms_of_use_accepted_at = (string?)null
                };

                var jsonContent = JsonSerializer.Serialize(resetData);
                var updateUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}";

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl) { Content = content };
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting terms acceptance: {ex.Message}");
            }
        }

        private async Task AcceptBothAsync()
        {
            if (NeedsPrivacyConsent && !HasAcceptedPrivacy)
            {
                ErrorMessage = "Du må godta personvernserklæringen for å fortsette.";
                return;
            }

            if (NeedsTermsConsent && !HasAcceptedTerms)
            {
                ErrorMessage = "Du må godta brukervilkårene for å fortsette.";
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                bool success = await SaveAcceptances(_userEmail);

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
                System.Diagnostics.Debug.WriteLine($"Acceptance error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<bool> SaveAcceptances(string email)
        {
            try
            {
                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                string? normalizedEmail = email?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(normalizedEmail))
                {
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"=== ACCEPTANCE UPDATE START ===");
                System.Diagnostics.Debug.WriteLine($"Email: {normalizedEmail}");
                System.Diagnostics.Debug.WriteLine($"Needs Privacy: {NeedsPrivacyConsent}");
                System.Diagnostics.Debug.WriteLine($"Needs Terms: {NeedsTermsConsent}");

                var updateDict = new Dictionary<string, object>();

                if (NeedsPrivacyConsent)
                {
                    updateDict["privacy_policy_accepted"] = true;
                    updateDict["privacy_policy_accepted_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    updateDict["privacy_policy_version"] = _latestPrivacyVersion;
                    System.Diagnostics.Debug.WriteLine($"Updating Privacy version: {_latestPrivacyVersion}");
                }

                if (NeedsTermsConsent)
                {
                    updateDict["terms_of_use_accepted"] = true;
                    updateDict["terms_of_use_accepted_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    updateDict["terms_of_use_version"] = _latestTermsVersion;
                    System.Diagnostics.Debug.WriteLine($"Updating Terms version: {_latestTermsVersion}");
                }

                var jsonContent = JsonSerializer.Serialize(updateDict);
                System.Diagnostics.Debug.WriteLine($"Update payload: {jsonContent}");

                var updateUrl = $"{supabaseUrl}/rest/v1/user_profiles?user_email=eq.{Uri.EscapeDataString(normalizedEmail)}";

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, updateUrl) { Content = content };
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                request.Headers.Add("Prefer", "return=representation");

                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(responseContent) || responseContent == "[]")
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Update failed!");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"SUCCESS: Acceptances updated!");
                System.Diagnostics.Debug.WriteLine($"=== ACCEPTANCE UPDATE END ===");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving acceptances: {ex.Message}");
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

        public class VersionInfo
        {
            [JsonPropertyName("version")]
            public string? Version { get; set; }

            [JsonPropertyName("lastUpdated")]
            public string? LastUpdated { get; set; }

            [JsonPropertyName("effectiveDate")]
            public string? EffectiveDate { get; set; }

            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("markdownUrl")]
            public string? MarkdownUrl { get; set; }

            [JsonPropertyName("changelog")]
            public Dictionary<string, List<string>> Changelog { get; set; } = new Dictionary<string, List<string>>();
        }

        public class UserProfile
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("user_id")]
            public string? UserId { get; set; }

            [JsonPropertyName("privacy_policy_accepted")]
            public bool PrivacyPolicyAccepted { get; set; }

            [JsonPropertyName("privacy_policy_version")]
            public string? PrivacyPolicyVersion { get; set; }

            [JsonPropertyName("privacy_policy_accepted_at")]
            public string? PrivacyPolicyAcceptedAt { get; set; }

            [JsonPropertyName("terms_of_use_accepted")]
            public bool TermsOfUseAccepted { get; set; }

            [JsonPropertyName("terms_of_use_version")]
            public string? TermsOfUseVersion { get; set; }

            [JsonPropertyName("terms_of_use_accepted_at")]
            public string? TermsOfUseAcceptedAt { get; set; }
        }
    }
}