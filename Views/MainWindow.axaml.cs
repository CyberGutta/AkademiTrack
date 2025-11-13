using AkademiTrack.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //DataContext = new MainWindowViewModel();

            this.Closing += MainWindow_Closing;
            this.WindowState = WindowState.Normal;

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            try
            {
                var settings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();

                if (settings.StartMinimized && settings.StartWithSystem)
                {
                    e.Cancel = true;

                    var result = await AkademiTrack.Views.ConfirmationDialog.ShowAsync(
                        this,
                        "Minimer til systemstatusfeltet?",
                        "Vil du minimere til systemstatusfeltet eller avslutte helt?\n\n" +
                        "Tips: Du kan alltid avslutte ved å høyreklikke på ikonet i systemstatusfeltet.",
                        false
                    );

                    if (result)
                    {
                        AkademiTrack.Services.TrayIconManager.MinimizeToTray();
                    }
                    else
                    {
                        AkademiTrack.Services.TrayIconManager.Dispose();

                        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown(0);
                        }
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

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == WindowStateProperty)
            {
                var newState = (WindowState)(change.NewValue ?? WindowState.Normal);

                if (newState == WindowState.Minimized)
                {

                    Task.Run(async () =>
                    {
                        try
                        {
                            var settings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();

                            if (settings.StartMinimized && settings.StartWithSystem)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    AkademiTrack.Services.TrayIconManager.MinimizeToTray();
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error handling minimize: {ex.Message}");
                        }
                    });
                }
            }
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