using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Linq;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        public ErrorDialog(string title, string message) : this()
        {
            this.Title = title;

            var messageBlock = this.FindControl<TextBlock>("MessageText");
            if (messageBlock != null)
            {
                messageBlock.Text = message;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public static async Task ShowAsync(Window owner, string title, string message)
        {
            var dialog = new ErrorDialog(title, message)
            {
                Owner = owner
            };
            await dialog.ShowDialog(owner);
        }
    }
}