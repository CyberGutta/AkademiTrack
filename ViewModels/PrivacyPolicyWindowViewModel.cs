using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Commands;

namespace AkademiTrack.ViewModels
{
    public class PrivacyPolicyWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private bool _hasAcceptedPrivacy;
        private bool _hasAcceptedTerms;
        private bool _isLoading;
        private string _errorMessage = string.Empty;

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

        public PrivacyPolicyWindowViewModel()
        {
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

        private string LocalConsentFilePath =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                 "AkademiTrack", "user_acceptance.json");

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

        public static async Task<bool> CheckIfNeedsPrivacyAcceptanceLocalAsync(string userEmail)
        {
            try
            {
                string latestPrivacyVersion = "1.1";
                string latestTermsVersion = "1.1";

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    try
                    {
                        var privacyResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/privacy-policy.json");
                        var privacyInfo = JsonSerializer.Deserialize<VersionInfo>(privacyResponse);
                        if (privacyInfo != null && !string.IsNullOrEmpty(privacyInfo.Version))
                            latestPrivacyVersion = privacyInfo.Version.Trim();

                        var termsResponse = await httpClient.GetStringAsync("https://cybergutta.github.io/AkademietTrack/terms-of-use.json");
                        var termsInfo = JsonSerializer.Deserialize<VersionInfo>(termsResponse);
                        if (termsInfo != null && !string.IsNullOrEmpty(termsInfo.Version))
                            latestTermsVersion = termsInfo.Version.Trim();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error fetching latest versions: {ex.Message}");
                    }
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "AkademiTrack");
                string filePath = Path.Combine(folder, "user_acceptance.json");

                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("No local acceptance file found — user must accept.");
                    return true;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<UserAcceptanceData>(json);
                if (data == null)
                {
                    System.Diagnostics.Debug.WriteLine("Corrupted local acceptance file — user must accept again.");
                    return true;
                }

                bool needsPrivacy = data.PrivacyPolicyVersion != latestPrivacyVersion;
                bool needsTerms = data.TermsOfUseVersion != latestTermsVersion;

                System.Diagnostics.Debug.WriteLine($"Local privacy version: {data.PrivacyPolicyVersion}, latest: {latestPrivacyVersion}");
                System.Diagnostics.Debug.WriteLine($"Local terms version: {data.TermsOfUseVersion}, latest: {latestTermsVersion}");
                System.Diagnostics.Debug.WriteLine($"NeedsPrivacy={needsPrivacy}, NeedsTerms={needsTerms}");

                return needsPrivacy || needsTerms;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking local acceptance: {ex.Message}");
                return true;
            }
        }

        public class UserAcceptanceData
        {
            public string PrivacyPolicyVersion { get; set; } = string.Empty;
            public string TermsOfUseVersion { get; set; } = string.Empty;
            public DateTime LastAccepted { get; set; }
        }

        private async Task LoadVersionsAndCheckUpgradesAsync()
        {
            try
            {
                await LoadLocalUserConsentAsync();
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

        private async Task LoadLocalUserConsentAsync()
        {
            try
            {
                if (!File.Exists(LocalConsentFilePath))
                {
                    _userCurrentPrivacyVersion = null;
                    _userCurrentTermsVersion = null;
                    return;
                }

                string json = await File.ReadAllTextAsync(LocalConsentFilePath);
                var consent = JsonSerializer.Deserialize<UserConsent>(json);

                _userCurrentPrivacyVersion = consent?.PrivacyPolicyVersion;
                _userCurrentTermsVersion = consent?.TermsOfUseVersion;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading local consent: {ex.Message}");
            }
        }

        private async Task<bool> SaveLocalUserConsentAsync()
        {
            try
            {
                var consent = new UserConsent
                {
                    PrivacyPolicyAccepted = true,
                    PrivacyPolicyVersion = _latestPrivacyVersion,
                    TermsOfUseAccepted = true,
                    TermsOfUseVersion = _latestTermsVersion,
                    AcceptedAt = DateTime.UtcNow
                };

                var folder = Path.GetDirectoryName(LocalConsentFilePath)!;
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                string json = JsonSerializer.Serialize(consent, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(LocalConsentFilePath, json);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving local consent: {ex.Message}");
                return false;
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
                bool success = await SaveLocalUserConsentAsync();

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

        public class UserConsent
        {
            public bool PrivacyPolicyAccepted { get; set; }
            public string? PrivacyPolicyVersion { get; set; }
            public bool TermsOfUseAccepted { get; set; }
            public string? TermsOfUseVersion { get; set; }
            public DateTime AcceptedAt { get; set; }
        }
    }
}