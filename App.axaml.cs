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
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Services.DependencyInjection;

namespace AkademiTrack
{
    public partial class App : Application
    {
        private bool _isShuttingDown = false;
        private static bool _hasShownHideNotification = false; 
        public static bool IsShuttingDown => ((App)Current!)._isShuttingDown;
        public static bool HasShownHideNotification 
        { 
            get => _hasShownHideNotification;
            set => _hasShownHideNotification = value;
        }

        public override void Initialize()
        {
            try
            {
                // Add global handler for unobserved task exceptions
                TaskScheduler.UnobservedTaskException += (sender, e) =>
                {
                    Debug.WriteLine($"[App] UnobservedTaskException: {e.Exception.GetBaseException().Message}");
                    Debug.WriteLine($"[App] Stack trace: {e.Exception.GetBaseException().StackTrace}");
                    
                    // Mark as observed to prevent app crash
                    e.SetObserved();
                    
                    // Log to our logging system if available
                    try
                    {
                        var loggingService = ServiceContainer.GetOptionalService<ILoggingService>();
                        loggingService?.LogError($"UnobservedTaskException: {e.Exception.GetBaseException().Message}");
                    }
                    catch
                    {
                        // Ignore if logging service not available
                    }
                };
                
                AvaloniaXamlLoader.Load(this);
                Debug.WriteLine("[App] XAML loaded successfully");

                ServiceContainer.Initialize();
                Debug.WriteLine("[App] Services initialized successfully");
                
                // Initialize global exception handler
                try
                {
                    var loggingService = ServiceContainer.GetService<ILoggingService>();
                    var notificationService = ServiceContainer.GetService<INotificationService>();
                    var analyticsService = ServiceContainer.GetService<AnalyticsService>();
                    
                    Services.Utilities.GlobalExceptionHandler.Initialize(loggingService, notificationService, analyticsService);
                    Debug.WriteLine("✓ Global exception handler initialized");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Failed to initialize global exception handler: {ex.Message}");
                    // Continue without global exception handler - not critical
                }
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Pre-initialize browser if needed
                        var browserFetcher = new PuppeteerSharp.BrowserFetcher();
                        await browserFetcher.DownloadAsync();
                        Debug.WriteLine("[App] Puppeteer pre-initialization completed");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Puppeteer pre-initialization failed: {ex.Message}");
                        // Not critical - continue without pre-initialization
                    }
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        Debug.WriteLine($"[App] Puppeteer task failed: {t.Exception.GetBaseException().Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
                
                Debug.WriteLine("[App] Initialize() completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] CRITICAL ERROR in Initialize(): {ex.Message}");
                Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
                
                // Try to show error to user if possible
                try
                {
                    System.Console.WriteLine($"AkademiTrack initialization failed: {ex.Message}");
                }
                catch
                {
                    // If even console output fails, we're in serious trouble
                }
                
                // Re-throw to prevent app from starting in broken state
                throw;
            }
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

            ShowMainWindow(desktop, startMinimized, !hasFeideCredentials);
        }

        private async void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool startMinimized, bool showFeideSetup = false)
        {
            var mainWindowViewModel = new RefactoredMainWindowViewModel(skipInitialization: showFeideSetup);

            if (showFeideSetup)
            {
                Debug.WriteLine("[App] No Feide credentials - will show Feide setup in main window");

                mainWindowViewModel.FeideViewModel.SetupCompleted += async (sender, e) =>
                {
                    if (e.Success)
                    {
                        Debug.WriteLine($"[App] Feide setup completed successfully for: {e.UserEmail}");
                        Debug.WriteLine("[App] Restarting application to load all services with new credentials...");

                        await Task.Delay(1000);

                        RestartApplication();
                    }
                };

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

            AkademiTrack.Services.MacOSDockHandler.Initialize(mainWindow);
            Debug.WriteLine("[App] MacOSDockHandler initialized");

            // Check if we should start minimized (regardless of showFeideSetup for this check)
            if (startMinimized && !showFeideSetup)
            {
                Debug.WriteLine("[App] Starting minimized to tray...");

                mainWindow.Show();
                await Task.Delay(50);
                mainWindow.Hide();
                AkademiTrack.Services.TrayIconManager.ShowTrayIcon();
            }
            else
            {
                mainWindow.Show();
                Debug.WriteLine("[App] Main window shown normally");
                
                if (!showFeideSetup)
                {
                    Debug.WriteLine("[App] Not in Feide setup mode - will show notification overlay after loading completes");
                    
                    Dispatcher.UIThread.Post(async () =>
                    {
                        Debug.WriteLine("DISPATCHER POST FIRED - waiting for loading to complete");
                        
                        // Wait for initial loading to complete
                        var maxWaitTime = TimeSpan.FromSeconds(30); // Maximum wait time
                        var startTime = DateTime.Now;
                        
                        while (mainWindowViewModel.IsLoading && (DateTime.Now - startTime) < maxWaitTime)
                        {
                            Debug.WriteLine($"[App] Still loading... waiting 1 second");
                            await Task.Delay(1000);
                        }
                        
                        if (mainWindowViewModel.IsLoading)
                        {
                            Debug.WriteLine("[App] ⚠️ Loading took too long, showing notification dialog anyway");
                        }
                        else
                        {
                            Debug.WriteLine("[App] ✅ Loading complete! Now showing notification dialog");
                        }
                        
                        // Additional delay for better UX - let user see the loaded app first
                        await Task.Delay(2000);
                        
                        try
                        {
                            Debug.WriteLine("[App] Checking if notification overlay should be shown...");
                            
                            bool shouldShow = await AkademiTrack.Services.NotificationPermissionChecker
                                .ShouldShowPermissionDialogAsync();
                            
                            Debug.WriteLine($"[App] ShouldShow check result: {shouldShow}");
                            
                            if (shouldShow)
                            {
                                Debug.WriteLine("[App] Creating notification permission overlay...");
                                
                                var overlayContainer = mainWindow.FindControl<ContentControl>("OverlayContainer");
                                
                                if (overlayContainer != null)
                                {
                                    Debug.WriteLine("[App] Found OverlayContainer - creating overlay...");
                                    
                                    var overlay = new AkademiTrack.Views.NotificationPermissionOverlay();
                                    
                                    overlay.Closed += (s, e) =>
                                    {
                                        Debug.WriteLine("[App] Overlay closed - hiding container");
                                        overlayContainer.Content = null;
                                        overlayContainer.IsVisible = false;
                                    };
                                    
                                    overlayContainer.Content = overlay;
                                    overlayContainer.IsVisible = true;
                                    
                                    Debug.WriteLine("[App] Overlay shown successfully!");
                                }
                                else
                                {
                                    Debug.WriteLine("[App] ❌ ERROR: Could not find OverlayContainer in MainWindow");
                                    Debug.WriteLine("[App] Make sure you added <ContentControl Name=\"OverlayContainer\" .../> to MainWindow.axaml");
                                }
                            }
                            else
                            {
                                Debug.WriteLine("[App] Not showing notification overlay - user already prompted or not on macOS");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[App] ❌ ERROR showing notification overlay: {ex.Message}");
                            Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
                        }
                    }, DispatcherPriority.Background);
                }
                else
                {
                    Debug.WriteLine("[App] In Feide setup mode - skipping notification overlay");
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
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    var executablePath = currentProcess.MainModule?.FileName;

                    if (!string.IsNullOrEmpty(executablePath))
                    {
                        Debug.WriteLine($"[App] Executable path: {executablePath}");

                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = executablePath,
                            UseShellExecute = true,
                            WorkingDirectory = System.IO.Directory.GetCurrentDirectory()
                        };

                        Debug.WriteLine("[App] Starting new application instance...");
                        System.Diagnostics.Process.Start(startInfo);

                        Debug.WriteLine("[App] Shutting down current instance...");
                        _isShuttingDown = true;
                        desktop.Shutdown();
                    }
                    else
                    {
                        Debug.WriteLine("[App] ERROR: Could not determine executable path for restart");
                        _isShuttingDown = true;
                        desktop.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during restart: {ex.Message}");
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
© 2026 CyberGutta

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
                    desktop.MainWindow?.Show();
                    desktop.MainWindow?.Activate();
                    
                    if (OperatingSystem.IsMacOS() && desktop.MainWindow != null)
                    {
                        desktop.MainWindow.BringIntoView();
                    }

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
                    
                    if (OperatingSystem.IsMacOS() && desktop.MainWindow != null)
                    {
                        desktop.MainWindow.BringIntoView();
                    }

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

        private async void QuitApp(object? sender, EventArgs e)
        {
            Debug.WriteLine("[App] Quit requested from menu");
            _isShuttingDown = true;
            
            try
            {
                // Cleanup global exception handler
                Services.Utilities.GlobalExceptionHandler.Cleanup();
                Debug.WriteLine("[App] Global exception handler cleaned up");

                // Shutdown service container
                await ServiceContainer.ShutdownAsync();
                Debug.WriteLine("[App] Service container shut down");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Error during cleanup: {ex.Message}");
            }
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
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