using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AkademiTrack.ViewModels;
using AkademiTrack.Views;
using System;
using System.IO;
using System.Text.Json;

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
                // Check if user is already activated synchronously to avoid binding issues
                bool isActivated = CheckActivationStatus();

                if (isActivated)
                {
                    // User is already activated, show main window
                    desktop.MainWindow = new MainWindow();
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
    }

    public class ActivationData
    {
        public bool IsActivated { get; set; }
        public DateTime ActivatedAt { get; set; }
        public string Email { get; set; }
    }
}