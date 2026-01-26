using Avalonia.Controls;
using Avalonia.Interactivity;
using AkademiTrack.ViewModels;
using System;
using System.ComponentModel;

namespace AkademiTrack.Views
{
    public partial class DependencyDownloadWindow : Window
    {
        private DependencyDownloadViewModel? _viewModel;
        
        public DependencyDownloadWindow()
        {
            InitializeComponent();
            _viewModel = new DependencyDownloadViewModel();
            DataContext = _viewModel;
            
            // Handle window closing
            Closing += OnWindowClosing;
        }
        
        public DependencyDownloadWindow(DependencyDownloadViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            
            // Handle window closing
            Closing += OnWindowClosing;
        }
        
        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            // If download is in progress and not completed, ask user for confirmation
            if (_viewModel != null && !_viewModel.IsCompleted && !_viewModel.HasError)
            {
                // Cancel the download
                _viewModel.Cancel();
                

            }
        }
    }
}