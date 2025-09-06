using Avalonia;
using Avalonia.Controls;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            var viewModel = new SettingsViewModel();
            DataContext = viewModel;

            // Handle close request from ViewModel
            viewModel.CloseRequested += (sender, args) => Close();
        }

        public SettingsWindow(Window owner) : this()
        {
            // Set the owner for proper modal behavior
            if (owner != null)
            {
                this.Owner = owner;
            }
        }
    }
}