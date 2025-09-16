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
                        // Create and show the main window
                        var mainWindow = new MainWindow();

                        // Update the application's main window reference
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow = mainWindow;
                        }

                        // Show the main window first
                        mainWindow.Show();

                        // Then close this login window
                        this.Close();
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error transitioning to main window: {ex.Message}");
                }
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
}