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

            // Initialize services
            Services.ServiceLocator.InitializeServices();
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

            // Always show main window, but determine which view to show
            ShowMainWindow(desktop, startMinimized, !hasFeideCredentials);
        }

        // Replace your ShowMainWindow method in App.axaml.cs with this:

        private async void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool startMinimized, bool showFeideSetup = false)
        {
            // ✅ Create viewmodel (it already has SettingsViewModel initialized in its constructor)
            var mainWindowViewModel = new RefactoredMainWindowViewModel(skipInitialization: showFeideSetup);

            // If we need to show Feide setup, set it up
            if (showFeideSetup)
            {
                Debug.WriteLine("[App] No Feide credentials - will show Feide setup in main window");

                // Subscribe to Feide setup completion
                mainWindowViewModel.FeideViewModel.SetupCompleted += async (sender, e) =>
                {
                    if (e.Success)
                    {
                        Debug.WriteLine($"[App] Feide setup completed successfully for: {e.UserEmail}");
                        Debug.WriteLine("[App] Restarting application to load all services with new credentials...");

                        // Small delay to let user see success
                        await Task.Delay(1000);

                        // Restart the application
                        RestartApplication();
                    }
                };

                // Show Feide view initially
                mainWindowViewModel.ShowFeide = true;
                mainWindowViewModel.ShowDashboard = false;
            }

            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel
            };

            desktop.MainWindow = mainWindow;
            Debug.WriteLine("[App] Main window set as MainWindow");

            AkademiTrack.Services.TrayIconManager.Initialize(mainWindow);
            Debug.WriteLine("[App] TrayIconManager initialized");

            if (startMinimized && !showFeideSetup) // Don't start minimized if showing Feide setup
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
                Console.WriteLine("[App] Main window shown normally");
                
                // ✅ NEW: Show notification permission dialog as modal overlay
                // Only show if not in Feide setup mode
                if (!showFeideSetup)
                {
                    Console.WriteLine("[App] Not in Feide setup mode - will show notification dialog");
                    
                    // Use Dispatcher to show dialog after UI thread is ready
                    Dispatcher.UIThread.Post(async () =>
                    {
                        Console.WriteLine("[App] ========== DISPATCHER POST FIRED ==========");
                        
                        // Small delay to let the main window fully render
                        await Task.Delay(1000);
                        
                        try
                        {
                            Console.WriteLine("[App] Checking if notification dialog should be shown...");
                            
                            // Check if we should show the dialog
                            bool shouldShow = await AkademiTrack.Services.NotificationPermissionChecker
                                .ShouldShowPermissionDialogAsync();
                            
                            Console.WriteLine($"[App] ShouldShow check result: {shouldShow}");
                            Console.WriteLine($"[App] IsDialogCurrentlyOpen: {AkademiTrack.Views.NotificationPermissionDialog.IsDialogCurrentlyOpen}");
                            
                            if (shouldShow && !AkademiTrack.Views.NotificationPermissionDialog.IsDialogCurrentlyOpen)
                            {
                                Console.WriteLine("[App] Creating notification permission dialog...");
                                var dialog = new AkademiTrack.Views.NotificationPermissionDialog();
                                
                                Console.WriteLine("[App] Calling ShowDialog on main window...");
                                // ShowDialog makes it modal - blocks interaction with parent window
                                await dialog.ShowDialog(mainWindow);
                                
                                Console.WriteLine($"[App] Dialog closed. User granted permission: {dialog.UserGrantedPermission}");
                            }
                            else
                            {
                                Console.WriteLine($"[App] Not showing notification dialog. ShouldShow: {shouldShow}, IsOpen: {AkademiTrack.Views.NotificationPermissionDialog.IsDialogCurrentlyOpen}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[App] ERROR showing notification dialog: {ex.Message}");
                            Console.WriteLine($"[App] Stack trace: {ex.StackTrace}");
                        }
                    }, DispatcherPriority.Background);
                }
                else
                {
                    Console.WriteLine("[App] In Feide setup mode - skipping notification dialog");
                }
            }
        }

        private void RestartApplication()
        {
            try
            {
                Debug.WriteLine("[App] Starting application restart process...");

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    // Get the current executable path
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    var executablePath = currentProcess.MainModule?.FileName;

                    if (!string.IsNullOrEmpty(executablePath))
                    {
                        Debug.WriteLine($"[App] Executable path: {executablePath}");

                        // Start new instance
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = executablePath,
                            UseShellExecute = true,
                            WorkingDirectory = System.IO.Directory.GetCurrentDirectory()
                        };

                        Debug.WriteLine("[App] Starting new application instance...");
                        System.Diagnostics.Process.Start(startInfo);

                        // Shutdown current instance
                        Debug.WriteLine("[App] Shutting down current instance...");
                        _isShuttingDown = true;
                        desktop.Shutdown();
                    }
                    else
                    {
                        Debug.WriteLine("[App] ERROR: Could not determine executable path for restart");
                        // Fallback: just shutdown (user can manually restart)
                        _isShuttingDown = true;
                        desktop.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during restart: {ex.Message}");
                // Fallback: just shutdown
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    _isShuttingDown = true;
                    desktop.Shutdown();
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
                    var settings = JsonSerializer.Deserialize<ViewModels.AppSettings>(json, new JsonSerializerOptions
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

                    // Trigger OpenSettingsCommand from RefactoredMainWindowViewModel
                    if (desktop.MainWindow?.DataContext is RefactoredMainWindowViewModel mainViewModel)
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

                    if (desktop.MainWindow?.DataContext is RefactoredMainWindowViewModel viewModel)
                    {
                        await viewModel.SettingsViewModel.CheckForUpdatesAsync();

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
}