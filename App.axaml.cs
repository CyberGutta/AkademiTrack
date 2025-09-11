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

                // Check if tutorial should be shown
                var shouldShowTutorial = ShouldShowTutorial();

                if (shouldShowTutorial)
                {
                    var tutorialWindow = new TutorialWindow();
                    tutorialWindow.Closed += (s, e) =>
                    {
                        // Show main window after tutorial closes
                        desktop.MainWindow = new MainWindow
                        {
                            DataContext = new MainWindowViewModel(),
                        };
                        desktop.MainWindow.Show();
                    };
                    desktop.MainWindow = tutorialWindow;
                }
                else
                {
                    // Skip tutorial, go directly to main window
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = new MainWindowViewModel(),
                    };
                }
            }

            base.OnFrameworkInitializationCompleted();
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
        public System.DateTime LastUpdated { get; set; }
    }
}