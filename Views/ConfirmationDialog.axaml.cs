using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Linq;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class ConfirmationDialog : Window
    {
        public bool Result { get; private set; } = false;

        public ConfirmationDialog()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        public ConfirmationDialog(string title, string message, bool isDangerous = false) : this()
        {
            this.Title = title;

            var messageBlock = this.FindControl<TextBlock>("MessageText");
            if (messageBlock != null)
            {
                messageBlock.Text = message;
            }

            var iconBlock = this.FindControl<TextBlock>("IconText");
            if (iconBlock != null && isDangerous)
            {
                iconBlock.Text = "⚠️";
                iconBlock.Foreground = Avalonia.Media.Brushes.Red;
            }

            var okButton = this.FindControl<Button>("OkButton");
            if (okButton != null && isDangerous)
            {
                okButton.Content = "Ja, slett";
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        public static async Task<bool> ShowAsync(Window owner, string title, string message, bool isDangerous = false)
        {
            var dialog = new ConfirmationDialog(title, message, isDangerous)
            {
                Owner = owner
            };

            await dialog.ShowDialog(owner);
            return dialog.Result;
        }
    }
}