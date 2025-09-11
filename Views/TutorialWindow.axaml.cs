using AkademiTrack.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace AkademiTrack.Views
{
    public partial class TutorialWindow : Window
    {
        public TutorialWindow()
        {
            InitializeComponent();
            var viewModel = new TutorialWindowViewModel();
            DataContext = viewModel;

            viewModel.ContinueRequested += (s, e) => Close();
            viewModel.ExitRequested += (s, e) =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            };
        }
    }
}