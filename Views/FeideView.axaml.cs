using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using System;
using AkademiTrack.ViewModels;

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