using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PuppeteerSharp;
using AkademiTrack.Services;

namespace AkademiTrack.ViewModels
{
    public class DependencyDownloadViewModel : INotifyPropertyChanged
    {
        private double _progressPercentage = 0;
        private string _statusMessage = "Initialiserer...";
        private string _progressDetails = "";
        private bool _showProgressDetails = false;
        private bool _isIndeterminate = true;
        private bool _hasError = false;
        private bool _isCompleted = false;
        private string _errorMessage = "";
        private CancellationTokenSource? _cancellationTokenSource;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? DownloadCompleted;
        public event EventHandler<string>? DownloadFailed;

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ProgressDetails
        {
            get => _progressDetails;
            set => SetProperty(ref _progressDetails, value);
        }

        public bool ShowProgressDetails
        {
            get => _showProgressDetails;
            set => SetProperty(ref _showProgressDetails, value);
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public ICommand RetryCommand { get; }

        public DependencyDownloadViewModel()
        {
            RetryCommand = new SimpleAsyncCommand(StartDownloadAsync);
        }

        public async Task StartDownloadAsync()
        {
            try
            {
                // Reset state
                HasError = false;
                IsCompleted = false;
                ProgressPercentage = 0;
                IsIndeterminate = true;
                ShowProgressDetails = false;
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                // Check for test mode
                var args = Environment.GetCommandLineArgs();
                bool isTestMode = args.Contains("--test-dependency-window");

                StatusMessage = "Sjekker Chrome/Chromium-status...";
                
                if (!isTestMode)
                {
                    // Use ChromeManager to handle all Chrome detection/installation
                    StatusMessage = "Forbereder Chrome/Chromium...";
                    ProgressDetails = "Sjekker installerte nettlesere...";
                    ShowProgressDetails = true;
                    
                    var chromePath = await ChromeManager.GetChromeExecutablePathAsync();
                    
                    if (!string.IsNullOrEmpty(chromePath))
                    {
                        StatusMessage = "Chrome/Chromium klar!";
                        ProgressPercentage = 100;
                        IsIndeterminate = false;
                        ProgressDetails = $"Bruker: {Path.GetFileName(chromePath)}";
                        
                        await Task.Delay(800, _cancellationTokenSource.Token);
                        await CompleteSuccessfully();
                        return;
                    }
                    else
                    {
                        // This should rarely happen with ChromeManager's fallbacks
                        StatusMessage = "Kunne ikke installere Chrome/Chromium";
                        HasError = true;
                        ErrorMessage = "AkademiTrack kunne ikke finne eller installere en kompatibel nettleser.\n\n" +
                                     "Vennligst installer Google Chrome manuelt og start AkademiTrack på nytt.";
                        DownloadFailed?.Invoke(this, ErrorMessage);
                        return;
                    }
                }
                else
                {
                    StatusMessage = "TEST MODE: Simulerer Chrome-sjekk...";
                    ShowProgressDetails = true;
                    ProgressDetails = "TEST: Starter simulert sjekk...";
                    
                    await SimulateProgressAsync(_cancellationTokenSource.Token);
                    
                    StatusMessage = "TEST: Chrome klar (simulert)";
                    ProgressDetails = "TEST: Chrome klar (simulert)";
                    ProgressPercentage = 95;
                    IsIndeterminate = false;
                    
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                    ProgressDetails = "TEST: Chrome klar (simulert)";
                    
                    await CompleteSuccessfully();
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operasjon avbrutt";
                HasError = true;
                ErrorMessage = "Operasjonen ble avbrutt av brukeren";
            }
            catch (HttpRequestException ex)
            {
                StatusMessage = "Nettverksfeil";
                HasError = true;
                ErrorMessage = $"Kunne ikke laste ned Chrome: {ex.Message}";
                DownloadFailed?.Invoke(this, ErrorMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = "Installasjon feilet";
                HasError = true;
                ErrorMessage = $"En uventet feil oppstod: {ex.Message}";
                DownloadFailed?.Invoke(this, ErrorMessage);
            }
        }

        private async Task SimulateProgressAsync(CancellationToken cancellationToken)
        {
            try
            {
                IsIndeterminate = false;
                var random = new Random();
                
                // Simulate realistic Chrome detection/installation
                var totalSteps = 100.0;
                var currentStep = 0.0;
                var startTime = DateTime.Now;
                var lastUpdateTime = startTime;
                
                var phases = new[]
                {
                    ("Sjekker system Chrome...", 20),
                    ("Laster ned Chrome installer...", 40),
                    ("Installerer Chrome...", 30),
                    ("Verifiserer installasjon...", 10)
                };
                
                foreach (var (phaseName, phaseSteps) in phases)
                {
                    StatusMessage = $"TEST: {phaseName}";
                    
                    for (int i = 0; i < phaseSteps && !cancellationToken.IsCancellationRequested; i++)
                    {
                        currentStep++;
                        var progressPercent = (currentStep / totalSteps) * 100;
                        ProgressPercentage = Math.Min(progressPercent, 95);
                        
                        ProgressDetails = $"TEST: {phaseName} ({progressPercent:F0}%)";
                        
                        // Variable delay to simulate real work
                        var delay = 50 + random.Next(0, 100);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DependencyDownload] Progress simulation error: {ex.Message}");
            }
        }

        private async Task CompleteSuccessfully()
        {
            StatusMessage = "Nedlasting fullført!";
            ProgressPercentage = 100;
            IsIndeterminate = false;
            ShowProgressDetails = false;
            
            // Brief delay to show completion, then trigger event immediately
            await Task.Delay(800);
            
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }
    }

    // Simple async command implementation for this specific use case
    public class SimpleAsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private bool _isExecuting;

        public SimpleAsyncCommand(Func<Task> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting;
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            try
            {
                _isExecuting = true;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}