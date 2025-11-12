using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Threading.Tasks;

namespace AkademiTrack.ViewModels
{
    public class NotificationPermissionDialog : Window
    {
        public bool UserGrantedPermission { get; private set; }
        
        // Static state to persist across window instances
        private static bool _notificationsEnabled = false;
        private Border? _enableButton;
        private Border? _settingsButton;
        private TextBlock? _enableButtonText;

        public NotificationPermissionDialog()
        {
            Title = "Varsel Tillatelser";
            Width = 500;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.BorderOnly;
            
            // Keep window within app, not system-modal
            Topmost = false;
            ExtendClientAreaToDecorationsHint = false;
            
            BuildContent();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            System.Diagnostics.Debug.WriteLine("[NotificationDialog] Dialog opened, starting permission check loop");
            _ = PermissionCheckLoopAsync();
        }

        private async Task PermissionCheckLoopAsync()
        {
            // Wait a bit before first check
            await Task.Delay(500);
            
            while (IsVisible)
            {
                try
                {
                    bool isEnabled = await CheckIfNotificationsReallyEnabledAsync();
                    System.Diagnostics.Debug.WriteLine($"[NotificationDialog] Permission check result: {isEnabled}");
                    
                    if (isEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine("[NotificationDialog] Notifications ARE enabled, closing now!");
                        await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                        Close();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationDialog] Check error: {ex.Message}");
                }
                
                await Task.Delay(1500); // Check every 1.5 seconds
            }
        }

        private async Task<bool> CheckIfNotificationsReallyEnabledAsync()
        {
            try
            {
                // ONLY check for flags = 16 which means notifications are ACTUALLY enabled
                // Just being in the notification center doesn't mean they're enabled!
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -B 2 'AkademiTrack' | grep -A 2 'AkademiTrack' | grep 'flags' | grep '16'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
                    
                    System.Diagnostics.Debug.WriteLine($"[NotificationDialog] Permission check output: '{output}'");
                    
                    // Only return true if we find "flags = 16" or "flags = (16)"
                    bool isEnabled = !string.IsNullOrEmpty(output) && 
                                   (output.Contains("flags = 16") || output.Contains("flags = (") && output.Contains("16"));
                    
                    System.Diagnostics.Debug.WriteLine($"[NotificationDialog] Is enabled: {isEnabled}");
                    return isEnabled;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationDialog] Error checking: {ex.Message}");
            }
            
            return false;
        }

        private Border CreateStyledButton(
            string text, 
            double width, 
            double height,
            string bgColor,
            string fgColor,
            string hoverColor,
            Action clickAction,
            bool isEnabled = true)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = text == "✕" ? 18 : (text == "Aktiver Varsler" ? 15 : 14),
                FontWeight = text == "Aktiver Varsler" ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.Parse(fgColor)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.Parse(bgColor)),
                CornerRadius = new CornerRadius(text == "✕" ? 0 : 8),
                Child = textBlock,
                Cursor = isEnabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
                Tag = new ButtonState { IsEnabled = isEnabled, OriginalBg = bgColor, OriginalFg = fgColor },
                Opacity = isEnabled ? 1.0 : 0.5
            };

            border.PointerEntered += (s, e) =>
            {
                var state = border.Tag as ButtonState;
                if (state?.IsEnabled == true)
                {
                    border.Background = new SolidColorBrush(Color.Parse(hoverColor));
                    if (text == "✕")
                    {
                        textBlock.Foreground = new SolidColorBrush(Color.Parse("#FF3B30"));
                    }
                }
            };

            border.PointerExited += (s, e) =>
            {
                var state = border.Tag as ButtonState;
                if (state?.IsEnabled == true)
                {
                    border.Background = new SolidColorBrush(Color.Parse(state.OriginalBg));
                    textBlock.Foreground = new SolidColorBrush(Color.Parse(state.OriginalFg));
                }
            };

            border.PointerPressed += (s, e) =>
            {
                var state = border.Tag as ButtonState;
                if (state?.IsEnabled == true && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                {
                    System.Diagnostics.Debug.WriteLine($"Button clicked: {text}");
                    clickAction?.Invoke();
                    e.Handled = true;
                }
            };

            return border;
        }

        private void SetButtonEnabled(Border button, bool enabled)
        {
            if (button.Tag is ButtonState state)
            {
                state.IsEnabled = enabled;
                button.Opacity = enabled ? 1.0 : 0.5;
                button.Cursor = enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            }
        }

        private void BuildContent()
        {
            var mainBorder = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(30)
            };

            var mainGrid = new Grid();
            
            // Close button in top-right corner
            var closeButton = CreateStyledButton(
                "✕",
                32,
                32,
                "#FFFFFF",
                "#999999",
                "#FFEBEE",
                async () =>
                {
                    UserGrantedPermission = false;
                    await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                    Close();
                }
            );
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.VerticalAlignment = VerticalAlignment.Top;
            closeButton.Margin = new Thickness(0, -10, -10, 0);

            var stackPanel = new StackPanel
            {
                Spacing = 20
            };

            var iconBorder = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(30),
                Background = new SolidColorBrush(Color.Parse("#007AFF")) { Opacity = 0.1 },
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = "🔔",
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = iconText;
            stackPanel.Children.Add(iconBorder);

            var title = new TextBlock
            {
                Text = "Aktiver Varsler",
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#1A1A1A"))
            };
            stackPanel.Children.Add(title);

            var description = new TextBlock
            {
                Text = "AkademiTrack trenger tillatelse til å sende varsler for å holde deg informert om:\n\n" +
                       "• Automatisk registrering av STU-timer\n" +
                       "• Viktige oppdateringer og meldinger\n" +
                       "• Status for automatisering",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#666666")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 400,
                LineHeight = 22
            };
            stackPanel.Children.Add(description);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // Primary action button - Activate notifications
            _enableButton = CreateStyledButton(
                "Aktiver Varsler",
                280,
                44,
                "#007AFF",
                "#FFFFFF",
                "#0051D5",
                async () => await EnableNotificationsAsync()
            );
            _enableButtonText = (TextBlock)_enableButton.Child!;

            // Secondary button - Open settings (initially disabled)
            _settingsButton = CreateStyledButton(
                "Åpne Systeminnstillinger",
                280,
                44,
                "#F0F0F0",
                "#999999",
                "#E0E0E0",
                () =>
                {
                    System.Diagnostics.Debug.WriteLine("Opening macOS notification settings...");
                    AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                    // Don't close or reset - just open settings
                },
                false // Initially disabled
            );

            buttonPanel.Children.Add(_enableButton);
            buttonPanel.Children.Add(_settingsButton);
            stackPanel.Children.Add(buttonPanel);

            // Help text
            var helpText = new TextBlock
            {
                Text = "Klikk 'Aktiver Varsler' for å sende et testvarsel.\nDu vil bli bedt om å tillate varsler i macOS.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                LineHeight = 16
            };
            stackPanel.Children.Add(helpText);

            mainGrid.Children.Add(stackPanel);
            mainGrid.Children.Add(closeButton);

            mainBorder.Child = mainGrid;
            Content = mainBorder;

            // Restore state if already enabled (in case window was deactivated)
            if (_notificationsEnabled)
            {
                RestoreEnabledState();
            }
        }

        private async Task EnableNotificationsAsync()
        {
            if (_notificationsEnabled) return; // Prevent double-click
            
            _notificationsEnabled = true;
            UserGrantedPermission = true;
            
            SetButtonEnabled(_enableButton!, false);
            _enableButton!.Cursor = new Cursor(StandardCursorType.Wait);
            _enableButtonText!.Text = "Sender testvarsel...";
            
            await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
            
            // Enable the settings button
            SetButtonEnabled(_settingsButton!, true);
            var settingsText = (TextBlock)_settingsButton!.Child!;
            settingsText.Foreground = new SolidColorBrush(Color.Parse("#333333"));
            
            _enableButtonText.Text = "Varsler Aktivert! ✓";
            _enableButton.Background = new SolidColorBrush(Color.Parse("#34C759"));
            
            // Mark as dismissed so it doesn't show again on next launch
            await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
        }

        private void RestoreEnabledState()
        {
            if (_enableButton != null && _enableButtonText != null && _settingsButton != null)
            {
                SetButtonEnabled(_enableButton, false);
                _enableButtonText.Text = "Varsler Aktivert! ✓";
                _enableButton.Background = new SolidColorBrush(Color.Parse("#34C759"));
                
                SetButtonEnabled(_settingsButton, true);
                var settingsText = (TextBlock)_settingsButton.Child!;
                settingsText.Foreground = new SolidColorBrush(Color.Parse("#333333"));
            }
        }

        private class ButtonState
        {
            public bool IsEnabled { get; set; }
            public string OriginalBg { get; set; } = "";
            public string OriginalFg { get; set; } = "";
        }
    }
}