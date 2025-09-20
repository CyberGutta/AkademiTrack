using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                // Check if user is already activated synchronously first
                bool isActivated = CheckActivationStatus();
                if (isActivated)
                {
                    // Start async verification without blocking
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
                                    // Key doesn't exist anymore - delete local file and show login
                                    DeleteActivationFile();
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        // Close current window and create new login window
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
                            // Continue with normal flow on error
                        }
                    });

                    // Continue with normal activated flow
                    bool shouldShowTutorial = ShouldShowTutorial();
                    if (shouldShowTutorial)
                    {
                        // Show tutorial first
                        var tutorialWindow = new TutorialWindow();
                        var tutorialViewModel = new TutorialWindowViewModel();

                        // Handle tutorial completion - show main window after tutorial
                        tutorialViewModel.ContinueRequested += (s, e) =>
                        {
                            var mainWindow = new MainWindow();
                            mainWindow.Show();
                            tutorialWindow.Close();
                            desktop.MainWindow = mainWindow;
                        };

                        // Handle tutorial exit - close application
                        tutorialViewModel.ExitRequested += (s, e) =>
                        {
                            desktop.Shutdown();
                        };

                        tutorialWindow.DataContext = tutorialViewModel;
                        desktop.MainWindow = tutorialWindow;
                    }
                    else
                    {
                        // Tutorial already seen, show main window directly
                        desktop.MainWindow = new MainWindow();
                    }
                }
                else
                {
                    // User needs to login, show login window
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

                System.Diagnostics.Debug.WriteLine($"Checking if activation key still exists: {activationKey}");

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to check activation key - HTTP {response.StatusCode}");
                    return true; // Assume valid on network errors
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var records = JsonSerializer.Deserialize<RemoteKeyRecord[]>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                bool exists = records?.Any(r => r.IsActivated) == true;
                System.Diagnostics.Debug.WriteLine($"Key exists and is activated: {exists}");
                return exists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking activation key: {ex.Message}");
                return true; // Assume valid on errors
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

                // If file doesn't exist, show tutorial
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking tutorial settings: {ex.Message}");
                // If there's an error, show tutorial to be safe
                return true;
            }
        }
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

    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}