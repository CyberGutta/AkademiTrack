using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AkademiTrack.ViewModels;
using AkademiTrack.Views;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;

namespace AkademiTrack
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // First check if user is activated
                bool isActivated = CheckActivationStatus();
                if (isActivated)
                {
                    var activationData = GetLocalActivationData();
                    string userEmail = activationData?.Email ?? string.Empty;

                    System.Diagnostics.Debug.WriteLine($"=== APP STARTUP - PRIVACY CHECK ===");
                    System.Diagnostics.Debug.WriteLine($"User email: {userEmail}");

                    // DON'T set MainWindow yet - wait for privacy check
                    Task.Run(async () =>
                    {
                        try
                        {
                            // USE THE CORRECT METHOD THAT CHECKS VERSION AND AUTO-RESETS
                            bool needsPrivacyAcceptance = await PrivacyPolicyWindowViewModel.NeedsPrivacyPolicyAcceptance(userEmail);

                            System.Diagnostics.Debug.WriteLine($"Needs privacy acceptance: {needsPrivacyAcceptance}");

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (needsPrivacyAcceptance)
                                {
                                    System.Diagnostics.Debug.WriteLine("Showing privacy policy window...");
                                    // Show privacy policy window
                                    ShowPrivacyPolicyWindow(desktop, userEmail);
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Privacy up-to-date, continuing normal flow...");
                                    // Continue with normal flow
                                    ContinueNormalFlow(desktop);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error checking privacy policy: {ex.Message}");
                            // On error, show privacy window to be safe
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ShowPrivacyPolicyWindow(desktop, userEmail);
                            });
                        }
                    });

                    // Start async verification of activation key in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (activationData != null && !string.IsNullOrEmpty(activationData.ActivationKey))
                            {
                                bool keyStillExists = await VerifyActivationKeyExists(activationData.ActivationKey);
                                if (!keyStillExists)
                                {
                                    DeleteActivationFile();
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        var currentWindow = desktop.MainWindow;
                                        var loginWindow = new LoginWindow();
                                        desktop.MainWindow = loginWindow;
                                        loginWindow.Show();
                                        currentWindow?.Close();
                                    });
                                    return;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Remote verification error: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // User must log in first
                    desktop.MainWindow = new LoginWindow();
                    desktop.MainWindow.Show();
                }
            }
            base.OnFrameworkInitializationCompleted();
        }

        private void ShowPrivacyPolicyWindow(IClassicDesktopStyleApplicationLifetime desktop, string userEmail)
        {
            System.Diagnostics.Debug.WriteLine($"ShowPrivacyPolicyWindow called for: {userEmail}");

            var privacyWindow = new PrivacyPolicyWindow();
            var privacyViewModel = new PrivacyPolicyWindowViewModel(userEmail);

            privacyViewModel.Accepted += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Privacy accepted in App.axaml.cs, continuing normal flow...");
                // When accepted, continue with normal flow
                ContinueNormalFlow(desktop);
                privacyWindow.Close();
            };

            privacyViewModel.Exited += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Privacy declined, shutting down...");
                desktop.Shutdown();
            };

            privacyWindow.DataContext = privacyViewModel;
            desktop.MainWindow = privacyWindow;
            privacyWindow.Show();
        }

        private void ContinueNormalFlow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            System.Diagnostics.Debug.WriteLine("ContinueNormalFlow called");

            // Check Feide credentials
            bool hasFeideCredentials = CheckFeideCredentials();

            System.Diagnostics.Debug.WriteLine($"Has Feide credentials: {hasFeideCredentials}");

            if (!hasFeideCredentials)
            {
                // Show Feide setup window after activation and privacy
                var feideWindow = new FeideWindow();
                desktop.MainWindow = feideWindow;
                feideWindow.Show();
            }
            else
            {
                // Standard flow – go directly to main window
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            }
        }

        private bool CheckActivationStatus()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string activationPath = Path.Combine(appDataDir, "activation.json");
                if (File.Exists(activationPath))
                {
                    string json = File.ReadAllText(activationPath);
                    var activationData = JsonSerializer.Deserialize<ActivationData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return activationData?.IsActivated == true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking activation status: {ex.Message}");
                return false;
            }
        }

        private bool CheckFeideCredentials()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string settingsPath = Path.Combine(appDataDir, "settings.json");

                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return settings?.InitialSetupCompleted == true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking Feide credentials: {ex.Message}");
                return false;
            }
        }

        private ActivationData GetLocalActivationData()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string activationPath = Path.Combine(appDataDir, "activation.json");
                if (File.Exists(activationPath))
                {
                    string json = File.ReadAllText(activationPath);
                    return JsonSerializer.Deserialize<ActivationData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading activation data: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> VerifyActivationKeyExists(string activationKey)
        {
            if (string.IsNullOrEmpty(activationKey))
                return false;

            HttpClient httpClient = null;
            try
            {
                httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                string supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

                var url = $"{supabaseUrl}/rest/v1/activation_keys?activation_key=eq.{Uri.EscapeDataString(activationKey.Trim())}&select=id,is_activated";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                request.Headers.Add("Accept", "application/json");

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to check activation key - HTTP {response.StatusCode}");
                    return false;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var records = JsonSerializer.Deserialize<RemoteKeyRecord[]>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                return records?.Any(r => r.IsActivated) == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking activation key: {ex.Message}");
                return false;
            }
            finally
            {
                httpClient?.Dispose();
            }
        }

        private void DeleteActivationFile()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string activationPath = Path.Combine(appDataDir, "activation.json");

                if (File.Exists(activationPath))
                {
                    File.Delete(activationPath);
                    System.Diagnostics.Debug.WriteLine("Activation file deleted - key no longer exists remotely");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting activation file: {ex.Message}");
            }
        }
    }

    public class ActivationData
    {
        [JsonPropertyName("activationKey")]
        public string ActivationKey { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("activatedAt")]
        public DateTime? ActivatedAt { get; set; }

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("deviceName")]
        public string? DeviceName { get; set; }

        [JsonPropertyName("isActivated")]
        public bool IsActivated { get; set; }
    }

    public class RemoteKeyRecord
    {
        public int Id { get; set; }

        [JsonPropertyName("is_activated")]
        public bool IsActivated { get; set; }
    }

    public class AppSettings
    {
        public bool InitialSetupCompleted { get; set; }
    }
}