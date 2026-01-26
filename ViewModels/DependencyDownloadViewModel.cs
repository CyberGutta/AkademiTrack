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

                StatusMessage = "Sjekker Chromium-status...";
                
                if (!isTestMode)
                {
                    // Check if Chromium is already downloaded
                    var browserFetcher = new BrowserFetcher();
                    var installedBrowsers = browserFetcher.GetInstalledBrowsers();
                    
                    if (installedBrowsers.Any())
                    {
                        StatusMessage = "Chromium allerede installert";
                        ProgressPercentage = 100;
                        IsIndeterminate = false;
                        
                        // Quick validation
                        await Task.Delay(500, _cancellationTokenSource.Token);
                        await CompleteSuccessfully();
                        return;
                    }

                    StatusMessage = "Laster ned Chromium browser...";
                    ShowProgressDetails = true;
                    ProgressDetails = "Starter nedlasting (~151 MB)...";

                    // Start monitoring download progress in a more realistic way
                    await DownloadWithRealProgressAsync(browserFetcher, _cancellationTokenSource.Token);
                }
                else
                {
                    StatusMessage = "TEST MODE: Simulerer Chromium-nedlasting...";
                    ShowProgressDetails = true;
                    ProgressDetails = "TEST: Starter simulert nedlasting (~120 MB)...";
                    
                    await SimulateProgressAsync(_cancellationTokenSource.Token);
                    
                    StatusMessage = "TEST: Verifiserer Chromium...";
                    ProgressDetails = "TEST: Sjekker integritet...";
                    ProgressPercentage = 95;
                    IsIndeterminate = false;
                    
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                    ProgressDetails = "TEST: Chromium klar (simulert)";
                    
                    await CompleteSuccessfully();
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Nedlasting avbrutt";
                HasError = true;
                ErrorMessage = "Nedlastingen ble avbrutt av brukeren";
            }
            catch (HttpRequestException ex)
            {
                StatusMessage = "Nettverksfeil";
                HasError = true;
                ErrorMessage = $"Kunne ikke laste ned Chromium: {ex.Message}";
                DownloadFailed?.Invoke(this, ErrorMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = "Nedlasting feilet";
                HasError = true;
                ErrorMessage = $"En uventet feil oppstod: {ex.Message}";
                DownloadFailed?.Invoke(this, ErrorMessage);
            }
        }

        private async Task DownloadWithRealProgressAsync(BrowserFetcher browserFetcher, CancellationToken cancellationToken)
        {
            // Start the actual download
            var downloadTask = browserFetcher.DownloadAsync();
            
            var startTime = DateTime.Now;
            IsIndeterminate = false;
            
            // Try to find the download directory where Chromium will be downloaded
            string? downloadDir = null;
            long totalExpectedSize = 151 * 1024 * 1024; // Default to 151 MB, will update if we can detect actual size
            
            try
            {
                // Check common PuppeteerSharp download locations
                var possiblePaths = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net9.0", "chromium"),
                    Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "net9.0", "chromium"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chromium"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local-chromium"),
                    Path.Combine(Path.GetTempPath(), "chromium-download")
                };
                
                // Monitor for directory creation
                foreach (var path in possiblePaths)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        downloadDir = path;
                        break;
                    }
                }
            }
            catch
            {
                // Ignore errors in finding download directory
            }
            
            // Progress monitoring loop with real size tracking
            long lastSize = 0;
            var stagnantCount = 0;
            var hasStartedDownloading = false;
            
            while (!downloadTask.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                var elapsed = DateTime.Now - startTime;
                var elapsedSeconds = elapsed.TotalSeconds;
                
                long currentSize = 0;
                double currentProgress = 0;
                
                // Try to get real progress by checking directory/file size
                try
                {
                    if (downloadDir != null)
                    {
                        if (Directory.Exists(downloadDir))
                        {
                            var dirInfo = new DirectoryInfo(downloadDir);
                            currentSize = dirInfo.GetFiles("*", SearchOption.AllDirectories)
                                .Sum(file => file.Length);
                            hasStartedDownloading = true;
                        }
                        else
                        {
                            // Check if parent directory has any chromium-related files
                            var parentDir = Path.GetDirectoryName(downloadDir);
                            if (Directory.Exists(parentDir))
                            {
                                var chromiumFiles = Directory.GetFiles(parentDir, "*chromium*", SearchOption.AllDirectories);
                                if (chromiumFiles.Length > 0)
                                {
                                    currentSize = chromiumFiles.Sum(file => new FileInfo(file).Length);
                                    hasStartedDownloading = true;
                                }
                            }
                        }
                    }
                    
                    // If we have real size data, use it
                    if (currentSize > 0)
                    {
                        // Update expected size if we detect it's larger than our estimate
                        if (currentSize > totalExpectedSize * 0.9) // If we're near our estimate but still downloading
                        {
                            totalExpectedSize = (long)(currentSize * 1.1); // Increase estimate by 10%
                        }
                        
                        currentProgress = Math.Min((double)currentSize / totalExpectedSize * 100, 95);
                        
                        if (currentSize > lastSize)
                        {
                            lastSize = currentSize;
                            stagnantCount = 0;
                        }
                        else
                        {
                            stagnantCount++;
                        }
                    }
                    else if (!hasStartedDownloading && elapsedSeconds < 30)
                    {
                        // Initial phase - preparing download
                        currentProgress = Math.Min(elapsedSeconds / 30.0 * 5, 5); // 0-5% in first 30 seconds
                        StatusMessage = "Forbereder nedlasting...";
                    }
                    else
                    {
                        // Fallback to time-based estimation if we can't track real size
                        if (elapsedSeconds < 60)
                            currentProgress = 5 + ((elapsedSeconds - 30) / 30.0 * 15); // 5-20% in next 30 seconds
                        else if (elapsedSeconds < 180)
                            currentProgress = 20 + ((elapsedSeconds - 60) / 120.0 * 50); // 20-70% in next 2 minutes
                        else
                            currentProgress = 70 + ((elapsedSeconds - 180) / 60.0 * 20); // 70-90% in final minute
                        
                        currentProgress = Math.Min(currentProgress, 90);
                    }
                }
                catch
                {
                    // Fallback to time-based if monitoring fails
                    currentProgress = Math.Min(elapsedSeconds / 180.0 * 85, 85);
                }
                
                ProgressPercentage = currentProgress;
                
                // Show real download progress
                if (currentSize > 0)
                {
                    var currentMB = currentSize / (1024.0 * 1024.0);
                    var totalMB = totalExpectedSize / (1024.0 * 1024.0);
                    ProgressDetails = $"{currentMB:F1} MB / {totalMB:F0} MB ({currentProgress:F0}%)";
                }
                else
                {
                    // Show estimated progress when we can't track real size yet
                    var estimatedMB = (totalExpectedSize / (1024.0 * 1024.0) * currentProgress) / 100.0;
                    ProgressDetails = $"~{estimatedMB:F1} MB / 151 MB (forbereder...)";
                }
                
                // Update status message based on progress
                if (currentProgress < 10)
                    StatusMessage = "Starter nedlasting...";
                else if (currentProgress < 30)
                    StatusMessage = "Laster ned Chromium browser...";
                else if (currentProgress < 60)
                    StatusMessage = "Nedlasting pågår...";
                else if (currentProgress < 85)
                    StatusMessage = "Nesten ferdig...";
                else
                    StatusMessage = "Fullfører nedlasting...";
                
                // Check every 1 second for more responsive updates
                await Task.Delay(1000, cancellationToken);
            }
            
            // Handle cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Download was cancelled by user");
            }
            
            // Wait for download to actually complete
            var installedBrowser = await downloadTask;
            
            if (installedBrowser != null && File.Exists(installedBrowser.GetExecutablePath()))
            {
                // Get final size for verification
                try
                {
                    if (downloadDir != null && Directory.Exists(downloadDir))
                    {
                        var dirInfo = new DirectoryInfo(downloadDir);
                        var finalSize = dirInfo.GetFiles("*", SearchOption.AllDirectories)
                            .Sum(file => file.Length);
                        var finalMB = finalSize / (1024.0 * 1024.0);
                        ProgressDetails = $"{finalMB:F1} MB nedlastet";
                    }
                }
                catch
                {
                    ProgressDetails = "Nedlasting fullført";
                }
                
                StatusMessage = "Verifiserer Chromium...";
                ProgressPercentage = 95;
                IsIndeterminate = false;
                
                // Quick verification by trying to get version
                await Task.Delay(1000, cancellationToken);
                
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = installedBrowser.GetExecutablePath(),
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync(cancellationToken);
                        if (process.ExitCode == 0)
                        {
                            var version = await process.StandardOutput.ReadToEndAsync();
                            ProgressDetails = $"Chromium klar: {version.Trim()}";
                        }
                    }
                }
                catch
                {
                    // Version check failed, but file exists so probably OK
                    ProgressDetails = "Chromium installert og klar";
                }

                await CompleteSuccessfully();
            }
            else
            {
                throw new InvalidOperationException("Chromium ble ikke lastet ned korrekt");
            }
        }

        private async Task SimulateProgressAsync(CancellationToken cancellationToken)
        {
            try
            {
                IsIndeterminate = false;
                var random = new Random();
                
                // Simulate download progress over ~2 minutes for 151 MB
                for (int i = 0; i <= 90; i += random.Next(1, 4))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    ProgressPercentage = Math.Min(i, 90);
                    
                    var estimatedMB = (151.0 * i) / 100.0;
                    ProgressDetails = $"{estimatedMB:F1} MB / 151.0 MB ({i}%)";
                    
                    if (i < 25)
                        StatusMessage = "Laster ned Chromium browser...";
                    else if (i < 50)
                        StatusMessage = "Nedlasting pågår...";
                    else if (i < 75)
                        StatusMessage = "Nesten ferdig...";
                    else
                        StatusMessage = "Fullfører nedlasting...";
                    
                    // Variable delay to simulate network fluctuations
                    var delay = random.Next(800, 2000);
                    await Task.Delay(delay, cancellationToken);
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