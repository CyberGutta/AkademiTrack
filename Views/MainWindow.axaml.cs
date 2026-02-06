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
        private bool _isReallyClosing = false;
        private AkademiTrack.ViewModels.AppSettings? _cachedSettings;
        private bool _isBeingRestored = false;

        public MainWindow()
        {
            InitializeComponent();

            this.Closing += MainWindow_Closing;
            this.Activated += MainWindow_Activated;
            this.WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            this.PropertyChanged += MainWindow_PropertyChanged;

            _ = LoadSettingsAsync();
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            // Window activated - could be from sleep, minimize, or focus change
            // Check if we need to refresh stale data, but not if we're just being restored from minimize
            if (DataContext is RefactoredMainWindowViewModel viewModel && !_isBeingRestored)
            {
                Debug.WriteLine("[FOCUS] Window activated - checking for stale data");
                _ = viewModel.CheckForStaleDataAsync();
            }
            else if (_isBeingRestored)
            {
                Debug.WriteLine("[FOCUS] Window activated during restore - skipping stale data check");
            }
        }

        private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty)
            {
                var oldState = (WindowState)(e.OldValue ?? WindowState.Normal);
                var newState = (WindowState)(e.NewValue ?? WindowState.Normal);

                if (oldState == WindowState.Minimized && newState == WindowState.Normal)
                {
                    Debug.WriteLine("[FOCUS] Window being restored from minimized - allowing activation");
                    _isBeingRestored = true;
                    
                    Dispatcher.UIThread.Post(() => 
                    {
                        _isBeingRestored = false;
                    }, DispatcherPriority.Background);
                }
                
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
            var previousState = this.WindowState;
            var wasVisible = this.IsVisible;
            
            await LoadSettingsAsync();
            
            if (!wasVisible || previousState == WindowState.Minimized)
            {
                this.WindowState = previousState;
            }
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            try
            {
                // FIRST: Check if app is shutting down (from menu quit, etc)
                if (App.IsShuttingDown)
                {
                    Debug.WriteLine("[MainWindow] App is shutting down - allowing close immediately");
                    _isReallyClosing = true;
                    // Don't wait for dispose - just close
                    _ = Task.Run(() => AkademiTrack.Services.TrayIconManager.Dispose());
                    return;
                }

                // SECOND: Check if this is marked as a real close (from ReallyClose())
                if (_isReallyClosing)
                {
                    Debug.WriteLine("[MainWindow] ReallyClosing flag set - allowing close immediately");
                    _ = Task.Run(() => AkademiTrack.Services.TrayIconManager.Dispose());
                    return;
                }

                // macOS-specific behavior: hide instead of close when clicking X
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Debug.WriteLine("[MainWindow] macOS: Hiding window instead of closing");
                    e.Cancel = true;
                    this.Hide(); 
                    return;
                }

                // Windows/Linux: Check start minimized setting
                if (_cachedSettings == null)
                {
                    _cachedSettings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
                }

                if (_cachedSettings.StartMinimized)
                {
                    e.Cancel = true;
                    AkademiTrack.Services.TrayIconManager.MinimizeToTray();
                    return;
                }
                else
                {
                    // Really close - don't wait for dispose
                    _ = Task.Run(() => AkademiTrack.Services.TrayIconManager.Dispose());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MainWindow_Closing: {ex.Message}");
                _ = Task.Run(() => AkademiTrack.Services.TrayIconManager.Dispose());
            }
        }

        public void ReallyClose()
        {
            Debug.WriteLine("[MainWindow] ReallyClose() called - setting flag and closing");
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