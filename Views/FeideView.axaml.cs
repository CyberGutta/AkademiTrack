using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Input.Platform;
using System;
using AkademiTrack.ViewModels;
using System.Threading.Tasks;

namespace AkademiTrack.Views
{
    public partial class FeideView : UserControl
    {
        private DispatcherTimer? _loadingTimer;
        private int _dotCount = 0;
        private TextBlock? _loadingTextBlock;

        public FeideView()
        {
            InitializeComponent();
            this.Loaded += FeideView_Loaded;
            this.DataContextChanged += FeideView_DataContextChanged;
        }

        private async void CopyErrorToClipboard(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (DataContext is FeideWindowViewModel viewModel && !string.IsNullOrEmpty(viewModel.ErrorMessage))
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                    {
                        // Get full error report from ChromeDriverManager if available
                        var fullReport = AkademiTrack.Services.ChromeDriverManager.GetFullErrorReport();
                        
                        // Check if we have a real ChromeDriver error (not just empty report)
                        var hasRealError = !string.IsNullOrEmpty(fullReport) && 
                                          fullReport.Contains("===") && 
                                          !fullReport.Contains("Final Error Code: UNKNOWN");
                        
                        // If we have a detailed report, use it; otherwise just copy the displayed error message
                        var textToCopy = hasRealError ? fullReport : viewModel.ErrorMessage;
                        
                        await clipboard.SetTextAsync(textToCopy);
                        
                        // Show feedback
                        if (sender is Button button)
                        {
                            var originalContent = button.Content;
                            button.Content = "✓ Kopiert!";
                            button.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22C55E"));
                            button.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DCFCE7"));
                            
                            await Task.Delay(2000);
                            
                            button.Content = originalContent;
                            button.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D70015"));
                            button.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFE5E5"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FeideView] Failed to copy to clipboard: {ex.Message}");
            }
        }

        private void FeideView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Find the loading text block if it exists
            _loadingTextBlock = this.FindControl<TextBlock>("LoadingTextBlock");
        }

        private void FeideView_DataContextChanged(object? sender, EventArgs e)
        {
            // Subscribe to IsLoading changes
            if (DataContext is FeideWindowViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeideWindowViewModel.IsLoading))
            {
                if (DataContext is FeideWindowViewModel vm)
                {
                    var button = this.FindControl<Button>("LoadingButton");
                    if (button != null)
                    {
                        if (vm.IsLoading)
                        {
                            button.Classes.Add("loading");
                            StartLoadingAnimation();
                        }
                        else
                        {
                            button.Classes.Remove("loading");
                            StopLoadingAnimation();
                        }
                    }
                }
            }
        }

        public void StartLoadingAnimation()
        {
            if (_loadingTimer != null)
                return;

            _dotCount = 0;
            _loadingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _loadingTimer.Tick += LoadingTimer_Tick;
            _loadingTimer.Start();
            
            UpdateLoadingText();
        }

        public void StopLoadingAnimation()
        {
            if (_loadingTimer != null)
            {
                _loadingTimer.Stop();
                _loadingTimer = null;
            }
        }

        private void LoadingTimer_Tick(object? sender, EventArgs e)
        {
            _dotCount = (_dotCount + 1) % 4; // Cycle through 0, 1, 2, 3
            UpdateLoadingText();
        }

        private void UpdateLoadingText()
        {
            if (_loadingTextBlock == null)
                return;

            var dots = _dotCount switch
            {
                1 => ".",
                2 => "..",
                3 => "...",
                _ => ""
            };

            _loadingTextBlock.Text = $"Tester innlogging{dots}";
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            StopLoadingAnimation();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is FeideWindowViewModel viewModel)
            {
                // Only trigger if all fields are filled and not currently loading
                if (viewModel.CanSave)
                {
                    // Execute the save command (same as clicking the test button)
                    if (viewModel.SaveCommand.CanExecute(null))
                    {
                        viewModel.SaveCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}