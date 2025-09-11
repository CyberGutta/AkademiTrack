using AkademiTrack.ViewModels;
using Avalonia;
using Avalonia.Controls;
using System;

namespace AkademiTrack.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();

            // Set initial position to center - this will be refined after loading
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void OnWindowLoaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CenterWindowManually();
            this.Loaded -= OnWindowLoaded; // Remove event handler
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

                // Get actual window size
                var windowWidth = this.Width;
                var windowHeight = this.Height;

                // Calculate center position
                var centerX = screenX + (screenWidth - windowWidth) / 2;
                var centerY = screenY + (screenHeight - windowHeight) / 2;

                Position = new PixelPoint((int)centerX, (int)centerY);
            }
        }
    }
}