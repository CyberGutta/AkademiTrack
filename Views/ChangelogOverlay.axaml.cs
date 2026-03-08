using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using System;
using System.Diagnostics;

namespace AkademiTrack.Views
{
    public partial class ChangelogOverlay : UserControl
    {
        public event EventHandler? Closed;

        public ChangelogOverlay()
        {
            InitializeComponent();
            Debug.WriteLine("[ChangelogOverlay] Created");
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[ChangelogOverlay] Close button clicked!");
            Closed?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine("[ChangelogOverlay] Closed event invoked");
        }
    }
}
