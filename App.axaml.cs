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
        private bool _isShuttingDown = false;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }



        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;


                ContinueNormalFlow(desktop);
            }

            base.OnFrameworkInitializationCompleted();
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
                        // Show window normally first to ensure proper initialization
                        mainWindow.Show();
                        
                        // Give it a moment to render
                        await Task.Delay(50);
                        
                        // Then hide it for tray mode
                        mainWindow.Hide();
                        mainWindow.ShowInTaskbar = false;

                        AkademiTrack.Services.TrayIconManager.ShowTrayIcon();

                        System.Diagnostics.Debug.WriteLine("Windows/Linux: App started in tray");
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
    }

    public class AppSettings
    {
        public bool InitialSetupCompleted { get; set; }
    }
}