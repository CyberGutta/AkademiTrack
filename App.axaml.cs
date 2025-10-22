using AkademiTrack.ViewModels;
using AkademiTrack.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AkademiTrack
{
    public partial class App : Application
    {
        private bool _isShuttingDown = false;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                string userEmail = GetUserEmail();

                System.Diagnostics.Debug.WriteLine($"=== APP STARTUP - PRIVACY CHECK ===");
                System.Diagnostics.Debug.WriteLine($"User email: {userEmail}");

                Task.Run(async () =>
                {
                    try
                    {
                        bool needsPrivacyAcceptance = await PrivacyPolicyWindowViewModel.CheckIfNeedsPrivacyAcceptanceLocalAsync(userEmail);

                        System.Diagnostics.Debug.WriteLine($"Needs privacy acceptance: {needsPrivacyAcceptance}");

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_isShuttingDown) return;

                            if (needsPrivacyAcceptance)
                            {
                                System.Diagnostics.Debug.WriteLine("Showing privacy policy window...");
                                ShowPrivacyPolicyWindow(desktop, userEmail);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("Privacy up-to-date, continuing normal flow...");
                                ContinueNormalFlow(desktop);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during startup validation: {ex.Message}");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!_isShuttingDown)
                            {
                                ShowPrivacyPolicyWindow(desktop, userEmail);
                            }
                        });
                    }
                });
            }
            base.OnFrameworkInitializationCompleted();
        }

        private void ShowPrivacyPolicyWindow(IClassicDesktopStyleApplicationLifetime desktop, string userEmail)
        {
            System.Diagnostics.Debug.WriteLine($"ShowPrivacyPolicyWindow called for: {userEmail}");

            var privacyWindow = new PrivacyPolicyWindow();
            var privacyViewModel = new PrivacyPolicyWindowViewModel();

            privacyViewModel.Accepted += async (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Privacy accepted, continuing normal flow...");

                Window nextWindow;
                if (!CheckFeideCredentials())
                {
                    nextWindow = new FeideWindow();
                }
                else
                {
                    nextWindow = new MainWindow();
                    AkademiTrack.Services.TrayIconManager.Initialize(nextWindow);
                }

                var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime!;
                desktop.MainWindow = nextWindow;

                nextWindow.Show();

                privacyWindow.Close();
            };

            privacyViewModel.Exited += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Privacy declined, shutting down...");
                _isShuttingDown = true;
                desktop.Shutdown();
            };

            privacyWindow.DataContext = privacyViewModel;
            desktop.MainWindow = privacyWindow;
            privacyWindow.Show();
        }

        private async void ContinueNormalFlow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_isShuttingDown) return;

            System.Diagnostics.Debug.WriteLine("ContinueNormalFlow called");

            bool hasFeideCredentials = CheckFeideCredentials();
            System.Diagnostics.Debug.WriteLine($"Has Feide credentials: {hasFeideCredentials}");

            bool startMinimized = await ShouldStartMinimized();
            System.Diagnostics.Debug.WriteLine($"Should start minimized: {startMinimized}");

            if (!hasFeideCredentials)
            {
                var feideWindow = new FeideWindow();
                desktop.MainWindow = feideWindow;
                feideWindow.Show();
            }
            else
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                AkademiTrack.Services.TrayIconManager.Initialize(mainWindow);

                if (startMinimized)
                {
                    System.Diagnostics.Debug.WriteLine("Starting minimized to tray...");

                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                    {
                        mainWindow.ShowInTaskbar = false;
                        AkademiTrack.Services.TrayIconManager.ShowTrayIcon();
                        System.Diagnostics.Debug.WriteLine("macOS: Window not shown, started in tray");
                    }
                    else
                    {
                        mainWindow.Opacity = 0;
                        mainWindow.ShowInTaskbar = false;

                        mainWindow.Show();

                        await Task.Delay(100);

                        mainWindow.Hide();

                        mainWindow.Opacity = 1;
                        mainWindow.ShowInTaskbar = true;

                        AkademiTrack.Services.TrayIconManager.ShowTrayIcon();

                        System.Diagnostics.Debug.WriteLine("Windows/Linux: App started in tray - no visual flash");
                    }
                }
                else
                {
                    mainWindow.Show();
                }
            }
        }

        private async Task<bool> ShouldStartMinimized()
        {
            try
            {
                var settings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
                return settings.StartMinimized && settings.StartWithSystem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking start minimized: {ex.Message}");
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

        private string GetUserEmail()
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

                    return settings?.UserEmail ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading user email: {ex.Message}");
                return string.Empty;
            }
        }
    }

    public class AppSettings
    {
        public bool InitialSetupCompleted { get; set; }

        [JsonPropertyName("userEmail")]
        public string? UserEmail { get; set; }
    }
}