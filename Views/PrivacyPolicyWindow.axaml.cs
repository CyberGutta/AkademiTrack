using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using System.Diagnostics;

namespace AkademiTrack.Views
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
                System.Diagnostics.Debug.WriteLine($"Error opening privacy policy URL: {ex.Message}");
            }
        }

        private void OnTermsOfUseLinkTapped(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://cybergutta.github.io/AkademietTrack/terms-of-use.html",
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening terms of use URL: {ex.Message}");
            }
        }
    }
}