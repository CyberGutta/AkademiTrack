using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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

        private void BuildContent()
        {
            var mainBorder = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(30)
            };

            var mainGrid = new Grid();
            
            // Close button in top-right corner
            var closeButton = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -10, -10, 0),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            
            // Hover effect for close button
            closeButton.PointerEntered += (s, e) =>
            {
                closeButton.Foreground = new SolidColorBrush(Color.Parse("#FF3B30"));
                closeButton.Background = new SolidColorBrush(Color.Parse("#FFEBEE"));
            };
            closeButton.PointerExited += (s, e) =>
            {
                closeButton.Foreground = new SolidColorBrush(Color.Parse("#999999"));
                closeButton.Background = Brushes.Transparent;
            };
            
            closeButton.Click += async (s, e) =>
            {
                UserGrantedPermission = false;
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                Close();
            };

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
            var enableButton = new Button
            {
                Content = "Aktiver Varsler",
                Width = 280,
                Height = 44,
                Background = new SolidColorBrush(Color.Parse("#007AFF")),
                Foreground = Brushes.White,
                Padding = new Thickness(20, 12),
                CornerRadius = new CornerRadius(8),
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Hover effect for enable button
            enableButton.PointerEntered += (s, e) =>
            {
                if (enableButton.IsEnabled)
                    enableButton.Background = new SolidColorBrush(Color.Parse("#0051D5"));
            };
            enableButton.PointerExited += (s, e) =>
            {
                if (enableButton.IsEnabled)
                    enableButton.Background = new SolidColorBrush(Color.Parse("#007AFF"));
            };
            
            enableButton.Click += async (s, e) =>
            {
                UserGrantedPermission = true;
                enableButton.IsEnabled = false;
                enableButton.Content = "Sender testvarsel...";
                enableButton.Background = new SolidColorBrush(Color.Parse("#007AFF")) { Opacity = 0.6 };
                enableButton.Cursor = new Cursor(StandardCursorType.Wait);
                
                await AkademiTrack.Services.NotificationPermissionChecker.RequestPermissionAsync();
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                
                // Small delay to show feedback
                await Task.Delay(1000);
                
                Close();
            };

            // Secondary button - Open settings
            var settingsButton = new Button
            {
                Content = "Åpne Systeminnstillinger",
                Width = 280,
                Height = 44,
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                Foreground = new SolidColorBrush(Color.Parse("#333333")),
                Padding = new Thickness(20, 12),
                CornerRadius = new CornerRadius(8),
                FontSize = 14,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Hover effect for settings button
            settingsButton.PointerEntered += (s, e) =>
            {
                settingsButton.Background = new SolidColorBrush(Color.Parse("#E0E0E0"));
            };
            settingsButton.PointerExited += (s, e) =>
            {
                settingsButton.Background = new SolidColorBrush(Color.Parse("#F0F0F0"));
            };
            
            settingsButton.Click += async (s, e) =>
            {
                AkademiTrack.Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                await AkademiTrack.Services.NotificationPermissionChecker.MarkDialogDismissedAsync();
                UserGrantedPermission = false;
                Close();
            };

            buttonPanel.Children.Add(enableButton);
            buttonPanel.Children.Add(settingsButton);
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