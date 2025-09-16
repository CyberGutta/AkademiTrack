using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkademiTrack.ViewModels;
using AkademiTrack.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System;

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
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();

                // Check activation status first
                var isActivated = CheckActivationStatus();

                if (!isActivated)
                {
                    // Show login window if not activated
                    ShowLoginWindow(desktop);
                }
                else
                {
                    // Check if tutorial should be shown
                    var shouldShowTutorial = ShouldShowTutorial();

                    if (shouldShowTutorial)
                    {
                        ShowTutorialWindow(desktop);
                    }
                    else
                    {
                        // Skip tutorial, go directly to main window
                        ShowMainWindow(desktop);
                    }
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

                if (!File.Exists(activationPath))
                    return false;

                var json = File.ReadAllText(activationPath);
                var activationData = JsonSerializer.Deserialize<ActivationData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return activationData?.IsActivated == true;
            }
            catch
            {
                // If we can't read activation status, assume not activated
                return false;
            }
        }

        private void ShowLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginWindow = new LoginWindow();
            var loginViewModel = (LoginWindowViewModel)loginWindow.DataContext;

            // Handle login completion
            loginViewModel.LoginCompleted += (s, success) =>
            {
                if (success)
                {
                    loginWindow.Close();

                    // After successful login, check if tutorial should be shown
                    var shouldShowTutorial = ShouldShowTutorial();

                    if (shouldShowTutorial)
                    {
                        ShowTutorialWindow(desktop);
                    }
                    else
                    {
                        ShowMainWindow(desktop);
                    }
                }
            };

            desktop.MainWindow = loginWindow;
        }

        private void ShowTutorialWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var tutorialWindow = new TutorialWindow();
            tutorialWindow.Closed += (s, e) =>
            {
                // Show main window after tutorial closes
                ShowMainWindow(desktop);
            };
            desktop.MainWindow = tutorialWindow;
        }

        private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            desktop.MainWindow.Show();
        }

        private bool ShouldShowTutorial()
        {
            try
            {
                if (!File.Exists("tutorial_settings.json"))
                    return true;

                var json = File.ReadAllText("tutorial_settings.json");
                var settings = JsonSerializer.Deserialize<TutorialSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return !settings.DontShowTutorial;
            }
            catch
            {
                // If we can't read settings, show tutorial
                return true;
            }
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }

    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class ActivationData
    {
        public bool IsActivated { get; set; }
        public DateTime ActivatedAt { get; set; }
        public string Email { get; set; }
    }
}