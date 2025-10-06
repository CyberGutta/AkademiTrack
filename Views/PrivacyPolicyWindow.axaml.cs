using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using System.Diagnostics;

namespace AkademiTrack.Views  // Changed from AkademiTrack
{
    public partial class PrivacyPolicyWindow : Window
    {
        public PrivacyPolicyWindow()
        {
            InitializeComponent();
        }

        private void OnPrivacyPolicyLinkTapped(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://cybergutta.github.io/AkademietTrack/privacy-policy.html",
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening URL: {ex.Message}");
            }
        }
    }
}