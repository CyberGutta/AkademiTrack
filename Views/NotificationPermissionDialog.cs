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
        
        private Border? _enableButton;
        private Border? _settingsButton;
        private Border? _skipButton;
        private Border? _doneButton;
        private TextBlock? _enableButtonText;
        private TextBlock? _instructionText;
        private Border? _stepIndicator;

        public NotificationPermissionDialog()
        {
            lock (_dialogLock)
            {
                if (_isDialogOpen)
                {
                    System.Diagnostics.Debug.WriteLine("[NotificationDialog] Dialog already open - preventing duplicate");
                    this.IsVisible = false;
                    this.Opened += (s, e) => Close();
                    return;
                }
                _isDialogOpen = true;
            }
            
            Title = "Varsel Tillatelser";
            Width = 480;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.BorderOnly;
            
            Topmost = false;
            ExtendClientAreaToDecorationsHint = false;
            
            this.Closed += (s, e) =>
            {
                lock (_dialogLock)
                {
                    _isDialogOpen = false;
                    System.Diagnostics.Debug.WriteLine("[NotificationDialog] Dialog closed - lock released");
                }
            };
            
            BuildContent();
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
                FontSize = text == "✕" ? 18 : (text == "Aktiver Varsler" || text == "Ferdig" ? 15 : 13),
                FontWeight = text == "Aktiver Varsler" || text == "Ferdig" ? FontWeight.SemiBold : FontWeight.Normal,
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
                Padding = new Thickness(25)
            };

            var mainGrid = new Grid();
            
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
                Spacing = 18
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
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#1A1A1A"))
            };
            stackPanel.Children.Add(title);

            var description = new TextBlock
            {
                Text = "AkademiTrack vil holde deg informert om:\n\n" +
                       "• Automatisk registrering av STU-timer\n" +
                       "• Viktige oppdateringer og meldinger\n" +
                       "• Status for automatisering",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#666666")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 380,
                LineHeight = 20
            };
            stackPanel.Children.Add(description);

            _stepIndicator = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#E8F5E9")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                IsVisible = false,
                MaxWidth = 380
            };

            var stepStack = new StackPanel { Spacing = 8 };
            
            var stepTitle = new TextBlock
            {
                Text = "✓ Testvarsel Sendt!",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#2E7D32"))
            };
            stepStack.Children.Add(stepTitle);

            _instructionText = new TextBlock
            {
                Text = "1. Åpne Systeminnstillinger\n" +
                       "2. Gå til 'Varslinger'\n" +
                       "3. Finn 'AkademiTrack'\n" +
                       "4. Slå på 'Tillat varsler'",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#555555")),
                LineHeight = 18
            };
            stepStack.Children.Add(_instructionText);

            _stepIndicator.Child = stepStack;
            stackPanel.Children.Add(_stepIndicator);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0)
            };

            _enableButton = CreateStyledButton(
                "Aktiver Varsler",
                300,
                44,
                "#007AFF",
                "#FFFFFF",
                "#0051D5",
                async () => await EnableNotificationsAsync()
            );
            _enableButtonText = (TextBlock)_enableButton.Child!;

            _settingsButton = CreateStyledButton(
                "→ Åpne Systeminnstillinger",
                300,
                44,
                "#007AFF",
                "#FFFFFF",
                "#0051D5",
                () =>
                {
                    System.Diagnostics.Debug.WriteLine("Opening macOS notification settings...");
                    AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                },
                true
            );
            _settingsButton.IsVisible = false;

            _doneButton = CreateStyledButton(
                "Jeg har aktivert varsler",
                300,
                40,
                "#F5F5F5",
                "#666666",
                "#E0E0E0",
                async () =>
                {
                    await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                    Close();
                }
            );
            _doneButton.IsVisible = false;

            // Skip button
            _skipButton = CreateStyledButton(
                "Hopp over",
                300,
                38,
                "#FFFFFF",
                "#999999",
                "#F5F5F5",
                async () =>
                {
                    UserGrantedPermission = false;
                    await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                    Close();
                }
            );

            buttonPanel.Children.Add(_enableButton);
            buttonPanel.Children.Add(_settingsButton);
            buttonPanel.Children.Add(_doneButton);
            buttonPanel.Children.Add(_skipButton);
            stackPanel.Children.Add(buttonPanel);

            var helpText = new TextBlock
            {
                Text = "Du kan endre dette senere i Systeminnstillinger",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stackPanel.Children.Add(helpText);

            mainGrid.Children.Add(stackPanel);
            mainGrid.Children.Add(closeButton);

            mainBorder.Child = mainGrid;
            Content = mainBorder;

            if (_notificationsEnabled)
            {
                RestoreEnabledState();
            }
        }

        private async Task EnableNotificationsAsync()
        {
            if (_notificationsEnabled) return;
            
            _notificationsEnabled = true;
            UserGrantedPermission = true;
            
            SetButtonEnabled(_enableButton!, false);
            _enableButton!.Cursor = new Cursor(StandardCursorType.Wait);
            _enableButtonText!.Text = "Sender testvarsel...";
            
            this.Height = 580;
            
            if (_stepIndicator != null)
            {
                _stepIndicator.IsVisible = true;
            }
            
            await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
            
            await Task.Delay(1500);
            
            _enableButton.IsVisible = false;
            
            if (_doneButton != null)
            {
                _doneButton.IsVisible = true;
            }
            
            if (_settingsButton != null)
            {
                _settingsButton.IsVisible = true;
            }
            
            if (_skipButton != null)
            {
                _skipButton.IsVisible = false;
            }
            
            await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
        }

        private void RestoreEnabledState()
        {
            if (_enableButton != null && _doneButton != null && _settingsButton != null && _stepIndicator != null)
            {
                // Expand window
                this.Height = 580;
                
                _enableButton.IsVisible = false;
                
                _stepIndicator.IsVisible = true;
                
                _doneButton.IsVisible = true;
                _settingsButton.IsVisible = true;
                
                if (_skipButton != null)
                {
                    _skipButton.IsVisible = false;
                }
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