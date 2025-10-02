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
                // Først sjekk om bruker er aktivert
                bool isActivated = CheckActivationStatus();
                if (isActivated)
                {
                    // Sjekk Feide credentials
                    bool hasFeideCredentials = CheckFeideCredentials();

                    if (!hasFeideCredentials)
                    {
                        // Viser Feide setup-vindu etter aktivering
                        desktop.MainWindow = new FeideWindow();
                    }
                    else
                    {
                        // Start asynkron verifisering i bakgrunnen
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var activationData = GetLocalActivationData();
                                if (activationData != null && !string.IsNullOrEmpty(activationData.ActivationKey))
                                {
                                    bool keyStillExists = await VerifyActivationKeyExists(activationData.ActivationKey);
                                    if (!keyStillExists)
                                    {
                                        // Nøkkelen finnes ikke lenger – slett lokalt og vis login
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
                                // Fortsetter normal flyt ved feil
                            }
                        });

                        // 🚫 Tutorial deaktivert – her kan du reaktivere hvis ønskelig
                        /*
                        bool shouldShowTutorial = ShouldShowTutorial();
                        if (shouldShowTutorial)
                        {
                            var tutorialWindow = new TutorialWindow();
                            var tutorialViewModel = new TutorialWindowViewModel();

                            tutorialViewModel.ContinueRequested += (s, e) =>
                            {
                                var mainWindow = new MainWindow();
                                mainWindow.Show();
                                tutorialWindow.Close();
                                desktop.MainWindow = mainWindow;
                            };

                            tutorialViewModel.ExitRequested += (s, e) =>
                            {
                                desktop.Shutdown();
                            };

                            tutorialWindow.DataContext = tutorialViewModel;
                            desktop.MainWindow = tutorialWindow;
                        }
                        else
                        {
                            desktop.MainWindow = new MainWindow();
                        }
                        */

                        // Standard flyt – gå rett til hovedvinduet
                        desktop.MainWindow = new MainWindow();
                    }
                }
                else
                {
                    // Bruker må logge inn først
                    desktop.MainWindow = new LoginWindow();
                }
            }
            base.OnFrameworkInitializationCompleted();
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

                    // Check if initial setup is completed
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
                    // ❌ SECURITY FIX: Return false on network/server errors
                    // This forces user to have working internet connection
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
                // ❌ SECURITY FIX: Return false on exceptions (network errors, timeouts, etc.)
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

        /*
        // 🚫 Tutorial logikk – beholdt, men deaktivert
        private bool ShouldShowTutorial()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string tutorialPath = Path.Combine(appDataDir, "tutorial_settings.json");

                if (File.Exists(tutorialPath))
                {
                    string json = File.ReadAllText(tutorialPath);
                    var tutorialSettings = JsonSerializer.Deserialize<TutorialSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return tutorialSettings?.DontShowTutorial != true;
                }

                // Hvis ingen fil – vis tutorial
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking tutorial settings: {ex.Message}");
                return true; // Som fallback: vis tutorial
            }
        }
        */
    }

    public class ActivationData
    {
        public bool IsActivated { get; set; }
        public DateTime ActivatedAt { get; set; }
        public string Email { get; set; }
        public string ActivationKey { get; set; }
    }

    public class RemoteKeyRecord
    {
        public int Id { get; set; }

        [JsonPropertyName("is_activated")]
        public bool IsActivated { get; set; }
    }

    /*
    // 🚫 Tutorial-innstillinger – beholdt men kommentert ut
    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }
    */
}
