using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using System.IO;
using System.Text.Json;

namespace AkademiTrack.Views
{
    public partial class LoginWindow : Window
    {
        private LoginWindowViewModel _viewModel;

        public LoginWindow()
        {
            InitializeComponent();
            _viewModel = new LoginWindowViewModel();
            DataContext = _viewModel;
            _viewModel.LoginCompleted += OnLoginCompleted;
            Closing += OnWindowClosing;
        }

        private async void OnLoginCompleted(object sender, bool isSuccessful)
        {
            if (isSuccessful)
            {
                try
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            // Check if tutorial should be shown
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
                                tutorialWindow.Show();
                                this.Close();
                            }
                            else
                            {
                                // Tutorial already seen, show main window directly
                                var mainWindow = new MainWindow();
                                desktop.MainWindow = mainWindow;
                                mainWindow.Show();
                                this.Close();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error transitioning after login: {ex.Message}");
                }
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

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.LoginCompleted -= OnLoginCompleted;
                _viewModel.Dispose();
            }
        }

        private async void OnCreateAccountTapped(object sender, TappedEventArgs e)
        {
            try
            {
                string url = "https://cybergutta.github.io/AkademietTrack/";
                await Task.Run(() =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Process.Start("open", url);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Process.Start("xdg-open", url);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open website: {ex.Message}");
            }
        }
    }

    public class TutorialSettings
    {
        public bool DontShowTutorial { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}