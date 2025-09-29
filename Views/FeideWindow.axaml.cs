using Avalonia.Controls;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class FeideWindow : Window
    {
        public FeideWindow()
        {
            InitializeComponent();

            var viewModel = new FeideWindowViewModel();

            // Handle setup completion
            viewModel.SetupCompleted += (s, success) =>
            {
                if (success)
                {
                    // Close this window and open main window
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    this.Close();
                }
            };

            DataContext = viewModel;
        }
    }
}