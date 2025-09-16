using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            DataContext = new LoginWindowViewModel();
        }

        private void OnCreateAccountTapped(object sender, TappedEventArgs e)
        {
            try
            {
                string url = "https://cybergutta.github.io/AkademietTrack/"; // Replace with your actual website URL

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
            }
            catch (Exception ex)
            {
                // Log the error - could show a message box if needed
                System.Diagnostics.Debug.WriteLine($"Failed to open website: {ex.Message}");
            }
        }
    }
}