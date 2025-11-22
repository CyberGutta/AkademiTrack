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
using System.Diagnostics;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using AkademiTrack.Services;

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
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
                ContinueNormalFlow(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void ContinueNormalFlow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_isShuttingDown) return;

            Debug.WriteLine("[App] ContinueNormalFlow called");

            bool hasFeideCredentials = CheckFeideCredentials();
            Debug.WriteLine($"[App] Has Feide credentials: {hasFeideCredentials}");

            bool startMinimized = await ShouldStartMinimized();
            Debug.WriteLine($"[App] Should start minimized: {startMinimized}");

            if (!hasFeideCredentials)
            {
                Debug.WriteLine("[App] No Feide credentials - showing setup window");
                ShowFeideSetupWindow(desktop);
            }
            else
            {
                Debug.WriteLine("[App] Credentials found - showing main window");
                ShowMainWindow(desktop, startMinimized);
            }
        }

        private void ShowFeideSetupWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var feideWindow = new FeideWindow
            {
                DataContext = new FeideWindowViewModel()
            };

            var viewModel = feideWindow.DataContext as FeideWindowViewModel;
            if (viewModel != null)
            {
                // Handle successful setup completion
                viewModel.SetupCompleted += async (sender, e) =>
                {
                    if (e.Success)
                    {
                        Debug.WriteLine($"[App] Feide setup completed successfully for: {e.UserEmail}");
                        
                        // IMPORTANT: Create and set MainWindow BEFORE closing FeideWindow
                        // Otherwise the app will shut down due to ShutdownMode.OnLastWindowClose
                        Debug.WriteLine("[App] Creating main window...");
                        var mainWindow = new MainWindow
                        {
                            DataContext = new MainWindowViewModel()
                        };
                        
                        // Set as MainWindow first
                        desktop.MainWindow = mainWindow;
                        Debug.WriteLine("[App] Main window set as MainWindow");
                        
                        // Initialize tray icon
                        AkademiTrack.Services.TrayIconManager.Initialize(mainWindow);
                        Debug.WriteLine("[App] TrayIconManager initialized");
                        
                        // Show the main window
                        mainWindow.Show();
                        Debug.WriteLine("[App] Main window shown");
                        
                        // Small delay for smooth transition
                        await Task.Delay(200);
                        
                        // NOW it's safe to close Feide window
                        Debug.WriteLine("[App] Closing Feide window...");
                        feideWindow.Close();
                        
                        Debug.WriteLine("[App] Transition from Feide to Main window complete!");
                    }
                };

                // Handle window close request from ViewModel
                viewModel.CloseRequested += (sender, e) =>
                {
                    Debug.WriteLine("[App] CloseRequested event received from FeideWindowViewModel");
                    // Window will close itself, event is just for logging
                };
            }

            desktop.MainWindow = feideWindow;
            feideWindow.Show();
            Debug.WriteLine("[App] Feide window shown");
        }

        private async void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool startMinimized)
        {
            var settingsViewModel = new SettingsViewModel(); 
            var mainWindowViewModel = new MainWindowViewModel
            {
                SettingsViewModel = settingsViewModel
            };

            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel
            };

            desktop.MainWindow = mainWindow;
            Debug.WriteLine("[App] Main window set as MainWindow");

            AkademiTrack.Services.TrayIconManager.Initialize(mainWindow);
            Debug.WriteLine("[App] TrayIconManager initialized");

            if (startMinimized)
            {
                Debug.WriteLine("[App] Starting minimized to tray...");

                mainWindow.Show();
                await Task.Delay(50);
                mainWindow.Hide();
                mainWindow.ShowInTaskbar = false;
                AkademiTrack.Services.TrayIconManager.ShowTrayIcon();
            }
            else
            {
                mainWindow.Show();
                Debug.WriteLine("[App] Main window shown normally");
            }

            // ✅ Trigger update check after window is shown
            _ = settingsViewModel.CheckForUpdatesAsync();
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
                Debug.WriteLine($"[App] Error checking start minimized: {ex.Message}");
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

                    bool hasCredentials = settings?.InitialSetupCompleted == true;
                    Debug.WriteLine($"[App] InitialSetupCompleted from settings.json: {hasCredentials}");
                    return hasCredentials;
                }
                
                Debug.WriteLine("[App] settings.json not found - no credentials");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error checking Feide credentials: {ex.Message}");
                return false;
            }
        }

        // ========== NATIVE MENU HANDLERS ==========

        private void ShowAbout(object? sender, EventArgs e)
        {
            var aboutText = @"AkademiTrack v1.1.0

Automatisk oppmøteregistrering for STU-økter på Akademiet

Utviklet av:
- Andreas Nilsen (@CyberNilsen)
- Mathias Hansen (@CyberHansen)

Lisens: MIT
© 2025 CyberGutta

github.com/CyberGutta/AkademiTrack";

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var messageBox = MsBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandard("Om AkademiTrack", aboutText, ButtonEnum.Ok);
                await messageBox.ShowAsync();
            });
        }

        private void ShowSettings(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    // Ensure main window is visible
                    desktop.MainWindow?.Show();
                    desktop.MainWindow?.Activate();
                    
                    // Trigger OpenSettingsCommand from MainWindowViewModel
                    if (desktop.MainWindow?.DataContext is MainWindowViewModel mainViewModel)
                    {
                        mainViewModel.OpenSettingsCommand?.Execute(null);
                    }
                }
            });
        }

        private void CheckForUpdates(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow?.Show();
                    desktop.MainWindow?.Activate();
                    
                    if (desktop.MainWindow?.DataContext is MainWindowViewModel viewModel)
                    {
                        if (viewModel.SettingsViewModel == null)
                        {
                            viewModel.SettingsViewModel = new SettingsViewModel();
                        }
                        
                        await viewModel.SettingsViewModel.CheckForUpdatesAsync();
                        
                        var status = viewModel.SettingsViewModel.UpdateStatus;
                        if (viewModel.SettingsViewModel.UpdateAvailable)
                        {
                            NativeNotificationService.Show(
                                "Oppdatering tilgjengelig",
                                $"Versjon {viewModel.SettingsViewModel.AvailableVersion} er klar for nedlasting!",
                                "SUCCESS"
                            );
                        }
                        else
                        {
                            NativeNotificationService.Show(
                                "Ingen oppdateringer",
                                "Du har allerede den nyeste versjonen.",
                                "INFO"
                            );
                        }
                    }
                }
            });
        }

        private void QuitApp(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _isShuttingDown = true;
                desktop.Shutdown();
            }
        }

        private void OpenWiki(object? sender, EventArgs e)
        {
            OpenUrl("https://github.com/CyberGutta/AkademiTrack/wiki");
        }

        private void OpenUserGuide(object? sender, EventArgs e)
        {
            OpenUrl("https://github.com/CyberGutta/AkademiTrack/wiki/User-Guide");
        }

        private void ReportIssue(object? sender, EventArgs e)
        {
            OpenUrl("https://forms.gle/HsHgvxYrZMgd9fyR7");
        }

        private void OpenGitHub(object? sender, EventArgs e)
        {
            OpenUrl("https://github.com/CyberGutta/AkademiTrack");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to open URL: {ex.Message}");
            }
        }
    }

    public class AppSettings
    {
        public bool InitialSetupCompleted { get; set; }
        public bool StartMinimized { get; set; }
        public bool StartWithSystem { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}