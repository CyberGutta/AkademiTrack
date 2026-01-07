using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class NotificationPermissionOverlay : UserControl
    {
        public event EventHandler? Closed;
        public bool UserGrantedPermission { get; private set; }
        
        private static bool _notificationsEnabled = false;

        public NotificationPermissionOverlay()
        {
            Console.WriteLine("[NotificationOverlay] Initializing overlay...");
            InitializeComponent();
            
            if (_notificationsEnabled)
            {
                RestoreEnabledState();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnEnableButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Enable button pressed");
                await EnableNotificationsAsync();
                e.Handled = true;
            }
        }

        private void OnSettingsButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Opening macOS notification settings...");
                AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                e.Handled = true;
            }
        }

        private async void OnDoneButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Done button pressed");
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                CloseOverlay();
                e.Handled = true;
            }
        }

        private async void OnSkipButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Skip button pressed");
                UserGrantedPermission = false;
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                CloseOverlay();
                e.Handled = true;
            }
        }

        private async void OnCloseButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Close button pressed");
                UserGrantedPermission = false;
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                CloseOverlay();
                e.Handled = true;
            }
        }

        private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Console.WriteLine("[NotificationOverlay] Background clicked - closing overlay");
                _ = AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                CloseOverlay();
                e.Handled = true;
            }
        }

        private async Task EnableNotificationsAsync()
        {
            if (_notificationsEnabled) return;
            
            _notificationsEnabled = true;
            UserGrantedPermission = true;
            
            var enableButton = this.FindControl<Border>("EnableButton");
            var enableButtonText = this.FindControl<TextBlock>("EnableButtonText");
            
            if (enableButton != null)
            {
                enableButton.IsEnabled = false;
                enableButton.Cursor = new Cursor(StandardCursorType.Wait);
            }
            
            if (enableButtonText != null)
            {
                enableButtonText.Text = "Sender testvarsel...";
            }
            
            var stepIndicator = this.FindControl<Border>("StepIndicator");
            if (stepIndicator != null)
            {
                stepIndicator.IsVisible = true;
            }
            
            await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
            
            await Task.Delay(1500);
            
            if (enableButton != null)
            {
                enableButton.IsVisible = false;
            }
            
            var settingsButton = this.FindControl<Border>("SettingsButton");
            if (settingsButton != null)
            {
                settingsButton.IsVisible = true;
            }
            
            var doneButton = this.FindControl<Border>("DoneButton");
            if (doneButton != null)
            {
                doneButton.IsVisible = true;
            }
            
            var skipButton = this.FindControl<Border>("SkipButton");
            if (skipButton != null)
            {
                skipButton.IsVisible = false;
            }
            
            await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
        }

        private void RestoreEnabledState()
        {
            var enableButton = this.FindControl<Border>("EnableButton");
            if (enableButton != null)
            {
                enableButton.IsVisible = false;
            }
            
            var stepIndicator = this.FindControl<Border>("StepIndicator");
            if (stepIndicator != null)
            {
                stepIndicator.IsVisible = true;
            }
            
            var doneButton = this.FindControl<Border>("DoneButton");
            if (doneButton != null)
            {
                doneButton.IsVisible = true;
            }
            
            var settingsButton = this.FindControl<Border>("SettingsButton");
            if (settingsButton != null)
            {
                settingsButton.IsVisible = true;
            }
            
            var skipButton = this.FindControl<Border>("SkipButton");
            if (skipButton != null)
            {
                skipButton.IsVisible = false;
            }
        }

        private void CloseOverlay()
        {
            Console.WriteLine("[NotificationOverlay] Closing overlay...");
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}