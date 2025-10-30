// Views/FeideWindow.axaml.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class FeideWindow : Window
    {
        public FeideWindow()
        {
            InitializeComponent();
        }

        private void OnSaveClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FeideWindowViewModel vm)
            {
                (vm.SaveCommand as RelayCommand)?.Execute(null);
            }
        }
    }
}