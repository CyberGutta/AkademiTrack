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
                    // User is activated, now check if tutorial should be shown
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
    }

    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}