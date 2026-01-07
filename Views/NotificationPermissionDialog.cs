using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class NotificationPermissionDialog : Window
    {
        public bool UserGrantedPermission { get; private set; }
        
        private static bool _notificationsEnabled = false;
        private static bool _isDialogOpen = false;
        private static readonly object _dialogLock = new object();
        
        public static bool IsDialogCurrentlyOpen 
        { 
            get 
            { 
                lock (_dialogLock) 
                { 
                    return _isDialogOpen; 
                } 
            } 
        }

        public NotificationPermissionDialog()
        {
            lock (_dialogLock)
            {
                if (_isDialogOpen)
                {
                    Debug.WriteLine("[NotificationDialog] Dialog already open - preventing duplicate");
                    this.IsVisible = false;
                    this.Opened += (s, e) => Close();
                    return;
                }
                _isDialogOpen = true;
            }
            
            InitializeComponent();
            
            this.Closed += (s, e) =>
            {
                lock (_dialogLock)
                {
                    _isDialogOpen = false;
                    Debug.WriteLine("[NotificationDialog] Dialog closed - lock released");
                }
            };
            
            // Restore state if notifications were already enabled
            if (_notificationsEnabled)
            {
                RestoreEnabledState();
            }
        }

        private async void OnEnableButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                await EnableNotificationsAsync();
                e.Handled = true;
            }
        }

        private void OnSettingsButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Debug.WriteLine("Opening macOS notification settings...");
                AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                e.Handled = true;
            }
        }

        private async void OnDoneButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                Close();
                e.Handled = true;
            }
        }

        private async void OnSkipButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                UserGrantedPermission = false;
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                Close();
                e.Handled = true;
            }
        }

        private async void OnCloseButtonPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                UserGrantedPermission = false;
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                Close();
                e.Handled = true;
            }
        }

        private async Task EnableNotificationsAsync()
        {
            if (_notificationsEnabled) return;
            
            _notificationsEnabled = true;
            UserGrantedPermission = true;
            
            // Disable and update enable button
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
            
            // Expand window height
            this.Height = 580;
            
            // Show step indicator
            var stepIndicator = this.FindControl<Border>("StepIndicator");
            if (stepIndicator != null)
            {
                stepIndicator.IsVisible = true;
            }
            
            // Request permission (sends test notification)
            await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
            
            await Task.Delay(1500);
            
            // Update UI - hide enable button, show settings and done buttons
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
            // Expand window
            this.Height = 580;
            
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
    }
}