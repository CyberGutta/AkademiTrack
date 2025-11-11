using Avalonia;
using Avalonia.Controls;
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
            Height = 320;
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
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var laterButton = new Button
            {
                Content = "Senere",
                Width = 120,
                Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
                Foreground = new SolidColorBrush(Color.Parse("#333333")),
                Padding = new Thickness(20, 10),
                CornerRadius = new CornerRadius(8)
            };
            laterButton.Click += (s, e) =>
            {
                UserGrantedPermission = false;
                Close();
            };

            var enableButton = new Button
            {
                Content = "Aktiver Varsler",
                Width = 160,
                Background = new SolidColorBrush(Color.Parse("#007AFF")),
                Foreground = Brushes.White,
                Padding = new Thickness(20, 10),
                CornerRadius = new CornerRadius(8),
                FontWeight = FontWeight.SemiBold
            };
            enableButton.Click += async (s, e) =>
            {
                UserGrantedPermission = true;
                await Services.NotificationPermissionChecker.RequestPermissionAsync();
                Services.NotificationPermissionChecker.OpenMacNotificationSettings();
                Close();
            };

            buttonPanel.Children.Add(laterButton);
            buttonPanel.Children.Add(enableButton);
            stackPanel.Children.Add(buttonPanel);

            // Help text
            var helpText = new TextBlock
            {
                Text = "Du kan endre dette senere i Systeminnstillinger",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            stackPanel.Children.Add(helpText);

            mainBorder.Child = stackPanel;
            Content = mainBorder;
        }
    }
}