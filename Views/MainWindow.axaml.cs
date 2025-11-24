using AkademiTrack.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class MainWindow : Window
    {
        private bool _hasShownMinimizeNotification = false;
        private bool _isReallyClosing = false;
        private AkademiTrack.ViewModels.AppSettings? _cachedSettings;
        private bool _isBeingRestored = false;

        public MainWindow()
        {
            InitializeComponent();

            this.Closing += MainWindow_Closing;
            this.WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            // Track when window is being legitimately restored
            this.PropertyChanged += MainWindow_PropertyChanged;

            _ = LoadSettingsAsync();
        }

        private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            // Track window state changes to detect legitimate restore operations
            if (e.Property == WindowStateProperty)
            {
                var oldState = (WindowState)(e.OldValue ?? WindowState.Normal);
                var newState = (WindowState)(e.NewValue ?? WindowState.Normal);

                // If transitioning FROM minimized TO normal, this is a legitimate restore
                if (oldState == WindowState.Minimized && newState == WindowState.Normal)
                {
                    Debug.WriteLine("[FOCUS] Window being restored from minimized - allowing activation");
                    _isBeingRestored = true;
                    
                    // Clear the flag after a short delay
                    Dispatcher.UIThread.Post(() => 
                    {
                        _isBeingRestored = false;
                    }, DispatcherPriority.Background);
                }
                
                // Handle minimize to tray
                if (newState == WindowState.Minimized && _cachedSettings?.StartMinimized == true)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        AkademiTrack.Services.TrayIconManager.MinimizeToTray();
                    }, DispatcherPriority.Background);
                }
            }
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                _cachedSettings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        public async Task RefreshSettingsAsync()
        {
            // Refresh settings WITHOUT activating the window
            var previousState = this.WindowState;
            var wasVisible = this.IsVisible;
            
            await LoadSettingsAsync();
            
            // Ensure window state doesn't change
            if (!wasVisible || previousState == WindowState.Minimized)
            {
                this.WindowState = previousState;
            }
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            try
            {
                if (_isReallyClosing)
                {
                    AkademiTrack.Services.TrayIconManager.Dispose();
                    return;
                }

                if (_cachedSettings == null)
                {
                    _cachedSettings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    _isReallyClosing = true;
                    AkademiTrack.Services.TrayIconManager.Dispose();
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                    return;
                }

                if (_cachedSettings.StartMinimized)
                {
                    e.Cancel = true;
                    AkademiTrack.Services.TrayIconManager.MinimizeToTray();

                    if (!_hasShownMinimizeNotification)
                    {
                        AkademiTrack.Services.NativeNotificationService.Show(
                            "AkademiTrack kjører fortsatt",
                            "Programmet kjører i bakgrunnen. Høyreklikk på tray-ikonet og velg 'Avslutt' for å lukke helt.",
                            "INFO"
                        );
                        _hasShownMinimizeNotification = true;
                    }
                }
                else
                {
                    AkademiTrack.Services.TrayIconManager.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MainWindow_Closing: {ex.Message}");
                AkademiTrack.Services.TrayIconManager.Dispose();
            }
        }

        public void ReallyClose()
        {
            _isReallyClosing = true;
            Close();
        }

        private void OnWindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CenterWindowManually();
            this.Loaded -= OnWindowLoaded;
        }

        private void CenterWindowManually()
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var screenWidth = screen.WorkingArea.Width;
                var screenHeight = screen.WorkingArea.Height;
                var screenX = screen.WorkingArea.X;
                var screenY = screen.WorkingArea.Y;

                var windowWidth = this.Width;
                var windowHeight = this.Height;

                var centerX = screenX + (screenWidth - windowWidth) / 2;
                var centerY = screenY + (screenHeight - windowHeight) / 2;

                Position = new PixelPoint((int)centerX, (int)centerY);
            }
        }
    }
}