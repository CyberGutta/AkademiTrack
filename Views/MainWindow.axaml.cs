using Avalonia.Controls;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }
    }
}