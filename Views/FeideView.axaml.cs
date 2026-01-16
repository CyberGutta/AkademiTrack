using Avalonia.Controls;
using Avalonia.Threading;
using System;

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
        }

        private void FeideView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Find the loading text block if it exists
            _loadingTextBlock = this.FindControl<TextBlock>("LoadingTextBlock");
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

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTree.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            StopLoadingAnimation();
        }
    }
}