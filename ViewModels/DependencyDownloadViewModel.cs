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
        private bool _migrationNeeded = false;
        private bool _needsSudo = false;

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

        public bool NeedsSudo
        {
            get => _needsSudo;
            set => SetProperty(ref _needsSudo, value);
        }

        public ICommand RetryCommand { get; }

        public DependencyDownloadViewModel()
        {
            RetryCommand = new SimpleAsyncCommand(StartDownloadAsync);
        }

        public void SetMigrationNeeded(bool migrationNeeded)
        {
            _migrationNeeded = migrationNeeded;
            Debug.WriteLine($"[DependencyDownload] Migration needed: {migrationNeeded}");
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

                StatusMessage = _migrationNeeded ? "Forbereder app-oppdatering..." : "Sjekker WebKit-status...";
                
                // Check for Linux and install secret-tool if needed
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    await EnsureSecretToolInstalledAsync();
                }
                
                if (!isTestMode)
                {
                    // Use WebKitManager to handle all WebKit detection/installation
                    StatusMessage = _migrationNeeded ? "Installerer WebKit for oppdatert app..." : "Forbereder WebKit...";
                    ProgressDetails = "Sjekker installerte nettlesere...";
                    ShowProgressDetails = true;
                    
                    // Create progress reporter to update UI
                    var progress = new Progress<string>(message =>
                    {
                        ProgressDetails = message;
                        
                        // Extract percentage if available
                        if (message.Contains("%"))
                        {
                            var parts = message.Split(':');
                            if (parts.Length > 1 && parts[1].Trim().EndsWith("%"))
                            {
                                var percentStr = parts[1].Trim().Replace("%", "");
                                if (int.TryParse(percentStr, out var percent))
                                {
                                    ProgressPercentage = percent;
                                    IsIndeterminate = false;
                                }
                            }
                        }
                        else if (message.Contains("successfully"))
                        {
                            ProgressPercentage = 100;
                            IsIndeterminate = false;
                        }
                    });
                    
                    var chromeDriverInstalled = await ChromeDriverManager.EnsureChromeDriverInstalledAsync(progress);
                    
                    if (chromeDriverInstalled)
                    {
                        StatusMessage = _migrationNeeded ? "ChromeDriver klar! Fullfører app-oppdatering..." : "ChromeDriver klar!";
                        ProgressPercentage = 100;
                        IsIndeterminate = false;
                        ProgressDetails = "Bruker: ChromeDriver";
                        
                        await Task.Delay(800, _cancellationTokenSource.Token);
                        await CompleteSuccessfully();
                        return;
                    }
                    else
                    {
                        StatusMessage = "Kunne ikke installere WebKit";
                        HasError = true;
                        ErrorMessage = "AkademiTrack kunne ikke installere WebKit-nettleser.\n\n" +
                                     "Vennligst sjekk internettforbindelsen og start AkademiTrack på nytt.";
                        DownloadFailed?.Invoke(this, ErrorMessage);
                        return;
                    }
                }
                else
                {
                    StatusMessage = "TEST MODE: Simulerer WebKit-sjekk...";
                    ShowProgressDetails = true;
                    ProgressDetails = "TEST: Starter simulert sjekk...";
                    
                    await SimulateProgressAsync(_cancellationTokenSource.Token);
                    
                    StatusMessage = "TEST: WebKit klar (simulert)";
                    ProgressDetails = "TEST: WebKit klar (simulert)";
                    ProgressPercentage = 95;
                    IsIndeterminate = false;
                    
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                    ProgressDetails = "TEST: WebKit klar (simulert)";
                    
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
                ErrorMessage = $"Kunne ikke laste ned WebKit: {ex.Message}";
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
                
                // Simulate realistic WebKit detection/installation
                var totalSteps = 100.0;
                var currentStep = 0.0;
                var startTime = DateTime.Now;
                var lastUpdateTime = startTime;
                
                var phases = new[]
                {
                    ("Sjekker system WebKit...", 20),
                    ("Laster ned WebKit installer...", 40),
                    ("Installerer WebKit...", 30),
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

        private async Task EnsureSecretToolInstalledAsync()
        {
            try
            {
                // Check if secret-tool is already installed
                if (File.Exists("/usr/bin/secret-tool"))
                {
                    Debug.WriteLine("[DependencyDownload] secret-tool already installed");
                    return;
                }

                Debug.WriteLine("[DependencyDownload] secret-tool not found, attempting installation...");
                StatusMessage = "Installerer secret-tool for sikker lagring...";
                ProgressDetails = "Sjekker om sudo er tilgjengelig...";
                ShowProgressDetails = true;

                // Check if we're running as root or have sudo access
                var whoamiProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "whoami",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                whoamiProcess.Start();
                var currentUser = (await whoamiProcess.StandardOutput.ReadToEndAsync()).Trim();
                await whoamiProcess.WaitForExitAsync();

                bool isRoot = currentUser == "root";

                if (!isRoot)
                {
                    // Check if user has sudo access
                    var sudoCheckProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "sudo",
                            Arguments = "-n true",
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    sudoCheckProcess.Start();
                    await sudoCheckProcess.WaitForExitAsync();

                    if (sudoCheckProcess.ExitCode != 0)
                    {
                        // No sudo access - need to prompt user
                        Debug.WriteLine("[DependencyDownload] No sudo access - prompting user to run with sudo");
                        StatusMessage = "Krever sudo-tilgang for å installere secret-tool";
                        ProgressDetails = "AkademiTrack trenger sudo-tilgang for å installere libsecret-tools.\n\n" +
                                        "Vennligst kjør AkademiTrack med sudo:\n" +
                                        "sudo ./AkademiTrack\n\n" +
                                        "Appen vil lukke om 10 sekunder...";
                        ShowProgressDetails = true;
                        NeedsSudo = true;

                        // Wait 10 seconds then exit
                        await Task.Delay(10000);
                        
                        // Try to restart with sudo
                        try
                        {
                            var restartProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "pkexec",
                                    Arguments = $"{Environment.ProcessPath}",
                                    UseShellExecute = false
                                }
                            };
                            restartProcess.Start();
                        }
                        catch
                        {
                            Debug.WriteLine("[DependencyDownload] Could not restart with pkexec");
                        }

                        Environment.Exit(1);
                        return;
                    }
                }

                // We have sudo access, install secret-tool
                ProgressDetails = "Installerer libsecret-tools...";
                
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sudo",
                        Arguments = "apt-get install -y libsecret-tools",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                installProcess.Start();
                var output = await installProcess.StandardOutput.ReadToEndAsync();
                var error = await installProcess.StandardError.ReadToEndAsync();
                await installProcess.WaitForExitAsync();

                if (installProcess.ExitCode == 0)
                {
                    Debug.WriteLine("[DependencyDownload] ✓ secret-tool installed successfully");
                    StatusMessage = "secret-tool installert!";
                    ProgressDetails = "Sikker lagring er nå tilgjengelig";
                }
                else
                {
                    Debug.WriteLine($"[DependencyDownload] ⚠️ secret-tool installation failed: {error}");
                    StatusMessage = "Kunne ikke installere secret-tool";
                    ProgressDetails = "Appen vil bruke fallback-lagring i stedet";
                }

                await Task.Delay(1500);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DependencyDownload] Error installing secret-tool: {ex.Message}");
                StatusMessage = "Kunne ikke installere secret-tool";
                ProgressDetails = "Appen vil bruke fallback-lagring";
                await Task.Delay(1500);
            }
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