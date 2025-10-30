using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using System;
using System.Diagnostics;

namespace AkademiTrack.Views
{
    public partial class FeideWindow : Window
    {
        public FeideWindow()
        {
            InitializeComponent();
            
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Wire up the CloseRequested event when ViewModel is set
            if (DataContext is FeideWindowViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
                Debug.WriteLine("[FeideWindow] CloseRequested event wired up");
            }
        }

        private void OnCloseRequested(object? sender, EventArgs e)
        {
            Debug.WriteLine("[FeideWindow] CloseRequested event triggered - closing window");
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up event subscription
            if (DataContext is FeideWindowViewModel vm)
            {
                vm.CloseRequested -= OnCloseRequested;
                Debug.WriteLine("[FeideWindow] CloseRequested event unsubscribed");
            }
            
            base.OnClosed(e);
        }
    }
}