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
                    // Check if we have bundled Chromium first
                    var bundledChromiumPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "chromium-cache");
                    bool hasBundledChromium = Directory.Exists(bundledChromiumPath);
                    
                    if (hasBundledChromium)
                    {
                        StatusMessage = "Bruker bundled Chromium...";
                        ProgressPercentage = 100;
                        IsIndeterminate = false;
                        
                        // Quick validation delay
                        await Task.Delay(800, _cancellationTokenSource.Token);
                        await CompleteSuccessfully();
                        return;
                    }
                    
                    // Use consistent cache directory for fallback download
                    var cacheDir = GetChromiumCacheDirectory();
                    var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
                    {
                        Path = cacheDir
                    });
                    
                    // Quick check if Chromium is already properly installed
                    var installedBrowsers = browserFetcher.GetInstalledBrowsers();
                    
                    if (installedBrowsers.Any())
                    {
                        // Verify the installation is complete and working
                        var browser = installedBrowsers.First();
                        var executablePath = browser.GetExecutablePath();
                        
                        if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                        {
                            var fileInfo = new System.IO.FileInfo(executablePath);
                            if (fileInfo.Length > 0) // Basic check that file isn't empty/corrupted
                            {
                                StatusMessage = "Chromium allerede installert";
                                ProgressPercentage = 100;
                                IsIndeterminate = false;
                                
                                // Quick validation delay
                                await Task.Delay(800, _cancellationTokenSource.Token);
                                await CompleteSuccessfully();
                                return;
                            }
                        }
                    }

                    StatusMessage = "Forbereder Chromium-nedlasting...";
                    ShowProgressDetails = true;
                    ProgressDetails = "Henter nedlastingsinformasjon...";

                    // Use our custom download method with real progress tracking
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
            IsIndeterminate = false;
            var startTime = DateTime.Now;
            
            StatusMessage = "Forbereder Chromium-nedlasting...";
            ProgressDetails = "Starter nedlasting...";
            
            // Get the cache directory where Chromium will be downloaded
            var cacheDir = browserFetcher.CacheDir;
            var initialDirSize = GetDirectorySize(cacheDir);
            
            // Start the download task
            var downloadTask = browserFetcher.DownloadAsync();
            
            // Monitor progress by checking directory size
            var lastSize = initialDirSize;
            var lastUpdateTime = DateTime.Now;
            var estimatedTotalSize = 150L * 1024 * 1024; // Start with 150MB estimate
            var hasStartedDownloading = false;
            
            StatusMessage = "Laster ned Chromium browser...";
            
            while (!downloadTask.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var currentSize = GetDirectorySize(cacheDir);
                var downloadedBytes = currentSize - initialDirSize;
                
                // Check if download has actually started
                if (downloadedBytes > 1024 * 1024) // More than 1MB downloaded
                {
                    hasStartedDownloading = true;
                    
                    // Adjust estimate if we're downloading more than expected
                    if (downloadedBytes > estimatedTotalSize * 0.8)
                    {
                        estimatedTotalSize = (long)(downloadedBytes * 1.25); // Increase estimate
                    }
                }
                
                // Update progress every 500ms
                if ((now - lastUpdateTime).TotalMilliseconds >= 500)
                {
                    var currentMB = downloadedBytes / (1024.0 * 1024.0);
                    var estimatedTotalMB = estimatedTotalSize / (1024.0 * 1024.0);
                    
                    double progressPercent = 0;
                    if (hasStartedDownloading && estimatedTotalSize > 0)
                    {
                        progressPercent = Math.Min((double)downloadedBytes / estimatedTotalSize * 100, 95);
                    }
                    else
                    {
                        // Use time-based estimation for initial phase
                        var elapsedSeconds = (now - startTime).TotalSeconds;
                        progressPercent = Math.Min(elapsedSeconds / 30.0 * 10, 10); // 0-10% in first 30 seconds
                    }
                    
                    ProgressPercentage = progressPercent;
                    
                    // Calculate download speed
                    var timeDiff = (now - lastUpdateTime).TotalSeconds;
                    var sizeDiff = currentSize - lastSize;
                    var speedMBps = timeDiff > 0 ? (sizeDiff / (1024.0 * 1024.0)) / timeDiff : 0;
                    
                    if (hasStartedDownloading)
                    {
                        ProgressDetails = $"{currentMB:F1} MB / ~{estimatedTotalMB:F0} MB ({progressPercent:F0}%) - {speedMBps:F1} MB/s";
                    }
                    else
                    {
                        ProgressDetails = $"Forbereder nedlasting... ({progressPercent:F0}%)";
                    }
                    
                    // Update status based on progress
                    if (progressPercent < 25)
                        StatusMessage = "Laster ned Chromium browser...";
                    else if (progressPercent < 50)
                        StatusMessage = "Nedlasting pågår...";
                    else if (progressPercent < 75)
                        StatusMessage = "Mer enn halvveis...";
                    else if (progressPercent < 90)
                        StatusMessage = "Nesten ferdig...";
                    else
                        StatusMessage = "Fullfører nedlasting...";
                    
                    lastUpdateTime = now;
                    lastSize = currentSize;
                }
                
                // Wait before next check
                await Task.Delay(500, cancellationToken);
            }
            
            // Handle cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Nedlasting avbrutt av bruker");
            }
            
            // Wait for download to complete
            var installedBrowser = await downloadTask;
            
            if (installedBrowser != null)
            {
                // Get final size
                var finalSize = GetDirectorySize(cacheDir) - initialDirSize;
                var finalMB = finalSize / (1024.0 * 1024.0);
                
                StatusMessage = "Verifiserer Chromium...";
                ProgressPercentage = 98;
                ProgressDetails = $"{finalMB:F1} MB nedlastet - verifiserer...";
                
                await Task.Delay(500, cancellationToken);
                
                // Skip verification to prevent Chrome window from opening
                try
                {
                    var executablePath = installedBrowser.GetExecutablePath();
                    if (File.Exists(executablePath))
                    {
                        // Just check file size instead of running Chrome
                        var fileInfo = new FileInfo(executablePath);
                        var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                        
                        if (fileSizeMB > 50) // Chrome executable should be at least 50MB
                        {
                            ProgressDetails = $"{finalMB:F1} MB - Chromium installert ({fileSizeMB:F0} MB)";
                        }
                        else
                        {
                            ProgressDetails = $"{finalMB:F1} MB - Chromium installert";
                        }
                    }
                    else
                    {
                        ProgressDetails = $"{finalMB:F1} MB - Chromium installert";
                    }
                }
                catch
                {
                    ProgressDetails = $"{finalMB:F1} MB - Chromium installert";
                }
                
                await CompleteSuccessfully();
            }
            else
            {
                throw new InvalidOperationException("Chromium ble ikke lastet ned korrekt");
            }
        }
        
        private static long GetDirectorySize(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return 0;
                
                var dirInfo = new DirectoryInfo(directoryPath);
                return dirInfo.GetFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }
        
        private static string GetChromiumCacheDirectory()
        {
            // EXTERNAL CHROMIUM APPROACH: Check for external signed Chromium first
            var externalChromiumPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AkademiTrack-Chromium");
            
            if (Directory.Exists(externalChromiumPath))
            {
                Debug.WriteLine($"[CHROMIUM] Using external signed Chromium at: {externalChromiumPath}");
                
                // Ensure executable permissions on macOS/Linux
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    EnsureExecutablePermissions(externalChromiumPath);
                }
                
                return externalChromiumPath;
            }
            
            // Fallback: Check for bundled Chromium (legacy)
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var bundledChromiumPath = Path.Combine(appDirectory, "Assets", "chromium-cache");
            
            if (Directory.Exists(bundledChromiumPath))
            {
                Debug.WriteLine($"[CHROMIUM] Using bundled Chromium at: {bundledChromiumPath}");
                
                // Ensure executable permissions on macOS/Linux
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    EnsureExecutablePermissions(bundledChromiumPath);
                }
                
                return bundledChromiumPath;
            }
            
            // Final fallback: AppData directory for download
            Debug.WriteLine("[CHROMIUM] No external or bundled Chromium found, falling back to AppData directory");
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cacheDir = Path.Combine(appDataDir, "AkademiTrack", "chromium-cache");
            
            // Ensure directory exists
            Directory.CreateDirectory(cacheDir);
            
            return cacheDir;
        }
        
        private static void EnsureExecutablePermissions(string chromiumPath)
        {
            try
            {
                // Find Chrome executable in the bundled path
                var chromeExecutables = Directory.GetFiles(chromiumPath, "*Chrome*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileName(f).Contains("Chrome") && !Path.GetExtension(f).Equals(".app", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var executable in chromeExecutables)
                {
                    if (File.Exists(executable))
                    {
                        Debug.WriteLine($"[CHROMIUM] Setting executable permissions for: {executable}");
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"+x \"{executable}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CHROMIUM] Failed to set executable permissions: {ex.Message}");
            }
        }


        private async Task SimulateProgressAsync(CancellationToken cancellationToken)
        {
            try
            {
                IsIndeterminate = false;
                var random = new Random();
                
                // Simulate realistic download with variable speeds
                var totalMB = 151.0;
                var downloadedMB = 0.0;
                var startTime = DateTime.Now;
                var lastUpdateTime = startTime;
                var lastDownloadedMB = 0.0;
                
                // Simulate network conditions with variable download speeds
                var baseSpeedMBps = 2.0; // Base 2 MB/s
                
                while (downloadedMB < totalMB && !cancellationToken.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    var timeDelta = (now - lastUpdateTime).TotalSeconds;
                    
                    if (timeDelta >= 0.5) // Update every 500ms
                    {
                        // Simulate variable network speed (0.5x to 3x base speed)
                        var speedMultiplier = 0.5 + (random.NextDouble() * 2.5);
                        var currentSpeedMBps = baseSpeedMBps * speedMultiplier;
                        
                        // Calculate how much to download in this interval
                        var mbToDownload = currentSpeedMBps * timeDelta;
                        downloadedMB = Math.Min(downloadedMB + mbToDownload, totalMB);
                        
                        var progressPercent = (downloadedMB / totalMB) * 100;
                        ProgressPercentage = Math.Min(progressPercent, 95); // Cap at 95% until "extraction"
                        
                        // Calculate actual speed for this interval
                        var actualSpeedMBps = (downloadedMB - lastDownloadedMB) / timeDelta;
                        
                        ProgressDetails = $"{downloadedMB:F1} MB / {totalMB:F1} MB ({progressPercent:F0}%) - {actualSpeedMBps:F1} MB/s";
                        
                        // Update status based on progress
                        if (progressPercent < 25)
                            StatusMessage = "TEST: Laster ned Chromium browser...";
                        else if (progressPercent < 50)
                            StatusMessage = "TEST: Nedlasting pågår...";
                        else if (progressPercent < 75)
                            StatusMessage = "TEST: Mer enn halvveis...";
                        else if (progressPercent < 90)
                            StatusMessage = "TEST: Nesten ferdig...";
                        else
                            StatusMessage = "TEST: Fullfører nedlasting...";
                        
                        lastUpdateTime = now;
                        lastDownloadedMB = downloadedMB;
                    }
                    
                    // Small delay to prevent busy waiting
                    await Task.Delay(100, cancellationToken);
                }
                
                // Simulate extraction phase
                if (!cancellationToken.IsCancellationRequested)
                {
                    StatusMessage = "TEST: Pakker ut Chromium...";
                    ProgressPercentage = 95;
                    ProgressDetails = $"{totalMB:F1} MB nedlastet - pakker ut...";
                    await Task.Delay(1000, cancellationToken);
                    
                    ProgressPercentage = 98;
                    ProgressDetails = "TEST: Sjekker installasjon...";
                    await Task.Delay(500, cancellationToken);
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