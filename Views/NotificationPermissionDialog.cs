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

        public NotificationPermissionDialog()
        {
            Title = "Varsel Tillatelser";
            Width = 500;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.BorderOnly;

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
                IsEnabled = isEnabled,
                Opacity = isEnabled ? 1.0 : 0.5
            };

            if (isEnabled)
            {
                var originalBg = bgColor;
                var originalFg = fgColor;

                border.PointerEntered += (s, e) =>
                {
                    if (border.IsEnabled)
                    {
                        border.Background = new SolidColorBrush(Color.Parse(hoverColor));
                        if (text == "✕")
                        {
                            textBlock.Foreground = new SolidColorBrush(Color.Parse("#FF3B30"));
                        }
                        System.Diagnostics.Debug.WriteLine($"Hover entered: {text}");
                    }
                };

                border.PointerExited += (s, e) =>
                {
                    if (border.IsEnabled)
                    {
                        border.Background = new SolidColorBrush(Color.Parse(originalBg));
                        textBlock.Foreground = new SolidColorBrush(Color.Parse(originalFg));
                        System.Diagnostics.Debug.WriteLine($"Hover exited: {text}");
                    }
                };

                border.PointerPressed += (s, e) =>
                {
                    if (border.IsEnabled && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                    {
                        System.Diagnostics.Debug.WriteLine($"Button clicked: {text}");
                        clickAction?.Invoke();
                        e.Handled = true;
                    }
                };
            }

            return border;
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

            // Settings button reference
            Border settingsButtonBorder = null!;
            Border enableButton = null!;

            // Primary action button - Activate notifications
            enableButton = CreateStyledButton(
                "Aktiver Varsler",
                280,
                44,
                "#007AFF",
                "#FFFFFF",
                "#0051D5",
                async () =>
                {
                    UserGrantedPermission = true;
                    enableButton.IsEnabled = false;
                    enableButton.Opacity = 0.6;
                    enableButton.Cursor = new Cursor(StandardCursorType.Wait);
                    
                    var textBlock = (TextBlock)enableButton.Child!;
                    textBlock.Text = "Sender testvarsel...";
                    
                    await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
                    
                    // Enable the settings button
                    if (settingsButtonBorder != null)
                    {
                        settingsButtonBorder.IsEnabled = true;
                        settingsButtonBorder.Opacity = 1.0;
                        settingsButtonBorder.Cursor = new Cursor(StandardCursorType.Hand);
                        var settingsText = (TextBlock)settingsButtonBorder.Child!;
                        settingsText.Foreground = new SolidColorBrush(Color.Parse("#333333"));
                    }
                    
                    textBlock.Text = "Varsler Aktivert! ✓";
                    enableButton.Background = new SolidColorBrush(Color.Parse("#34C759"));
                    
                    await Task.Delay(2000);
                    
                    await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                    Close();
                }
            );

            // Secondary button - Open settings (initially disabled)
            settingsButtonBorder = CreateStyledButton(
                "Åpne Systeminnstillinger",
                280,
                44,
                "#F0F0F0",
                "#999999",
                "#E0E0E0",
                async () =>
                {
                    AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                    await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                    UserGrantedPermission = false;
                    Close();
                },
                false // Initially disabled
            );

            buttonPanel.Children.Add(enableButton);
            buttonPanel.Children.Add(settingsButtonBorder);
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
        }
    }
}