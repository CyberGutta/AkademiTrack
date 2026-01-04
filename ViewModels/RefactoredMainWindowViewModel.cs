using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AkademiTrack.Common;
using AkademiTrack.Services;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace AkademiTrack.ViewModels
{
    public class RefactoredMainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        #region Services
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        private readonly ISettingsService _settingsService;
        private readonly IAutomationService _automationService;
        #endregion

        #region Private Fields
        private readonly HttpClient _httpClient;
        private AuthenticationService? _authService;
        private UserParameters? _userParameters;
        private UpdateCheckerService? _updateChecker;
        private bool _isLoading = true;
        private bool _isAuthenticated = false;
        private string _statusMessage = "Ready";
        
        // Navigation
        private bool _showDashboard = true;
        private bool _showSettings = false;
        private bool _showTutorial = false;
        private bool _showFeide = false;
        
        // Current notification
        private NotificationEntry? _currentNotification;
        #endregion

        #region Events
        public new event PropertyChangedEventHandler? PropertyChanged;
        #endregion

        #region Properties
        
        // Loading and Authentication
        public bool IsLoading
        {
            get => _isLoading;
            private set 
            { 
                if (SetProperty(ref _isLoading, value))
                {
                    // Update command states when loading changes
                    ((AsyncRelayCommand)RetryAuthenticationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            private set 
            { 
                if (SetProperty(ref _isAuthenticated, value))
                {
                    // Update command states when authentication changes
                    ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)RetryAuthenticationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        // Navigation
        public bool ShowDashboard
        {
            get => _showDashboard;
            set => SetProperty(ref _showDashboard, value);
        }

        public bool ShowSettings
        {
            get => _showSettings;
            set => SetProperty(ref _showSettings, value);
        }

        public bool ShowTutorial
        {
            get => _showTutorial;
            set => SetProperty(ref _showTutorial, value);
        }

        public bool ShowFeide
        {
            get => _showFeide;
            set => SetProperty(ref _showFeide, value);
        }

        // Automation
        public bool IsAutomationRunning => _automationService?.IsRunning ?? false;

        // Logging
        public ObservableCollection<LogEntry> LogEntries => _loggingService.LogEntries;
        public bool ShowDetailedLogs
        {
            get => _loggingService.ShowDetailedLogs;
            set => _loggingService.ShowDetailedLogs = value;
        }

        // Notifications
        public NotificationEntry? CurrentNotification
        {
            get => _currentNotification;
            private set => SetProperty(ref _currentNotification, value);
        }

        public bool HasCurrentNotification => _currentNotification != null;

        // ViewModels
        public SettingsViewModel SettingsViewModel { get; set; }
        public DashboardViewModel Dashboard { get; private set; }
        public FeideWindowViewModel FeideViewModel { get; set; }

        // Application Info
        public string Greeting => "AkademiTrack - STU Tidsregistrering";

        #endregion

        #region Commands
        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand BackToDashboardCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenFeideCommand { get; }
        public ICommand OpenTutorialCommand { get; }
        public ICommand RefreshDataCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand DismissNotificationCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand RetryAuthenticationCommand { get; }
        #endregion

        #region Constructor
        public RefactoredMainWindowViewModel(bool skipInitialization = false)
        {
            // Get services from service locator
            _loggingService = ServiceLocator.Instance.GetService<ILoggingService>();
            _notificationService = ServiceLocator.Instance.GetService<INotificationService>();
            _settingsService = ServiceLocator.Instance.GetService<ISettingsService>();
            _automationService = ServiceLocator.Instance.GetService<IAutomationService>();
            
            // Initialize other services
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            // Initialize ViewModels
            SettingsViewModel = new SettingsViewModel();
            _updateChecker = new UpdateCheckerService(SettingsViewModel);
            Dashboard = new DashboardViewModel();
            FeideViewModel = new FeideWindowViewModel();

            // Initialize Commands
            StartAutomationCommand = new AsyncRelayCommand(StartAutomationAsync, () => IsAuthenticated && !IsAutomationRunning);
            StopAutomationCommand = new AsyncRelayCommand(StopAutomationAsync, () => IsAutomationRunning);
            BackToDashboardCommand = new AsyncRelayCommand(BackToDashboardAsync);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
            OpenFeideCommand = new AsyncRelayCommand(OpenFeideAsync);
            OpenTutorialCommand = new AsyncRelayCommand(OpenTutorialAsync);
            RefreshDataCommand = new AsyncRelayCommand(RefreshDataAsync);
            ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new AsyncRelayCommand(ToggleDetailedLogsAsync);
            DismissNotificationCommand = new AsyncRelayCommand(DismissCurrentNotificationAsync);
            ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
            RetryAuthenticationCommand = new AsyncRelayCommand(RetryAuthenticationAsync, () => !IsAuthenticated && !IsLoading);

            // Subscribe to service events
            SubscribeToServiceEvents();

            // Only start initialization if not skipping (i.e., not showing Feide setup)
            if (!skipInitialization)
            {
                _ = InitializeAsync();
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts the authentication and initialization process manually.
        /// Used when the ViewModel was created with skipInitialization=true.
        /// </summary>
        public async Task StartInitializationAsync()
        {
            await InitializeAsync();
        }
        #endregion

        #region Initialization
        private int _initializationRetryCount = 0;
        private const int MAX_RETRY_ATTEMPTS = 3;
        
        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                _loggingService.LogInfo($"🚀 Starter AkademiTrack... (Forsøk {_initializationRetryCount + 1}/{MAX_RETRY_ATTEMPTS})");

                // Initialize authentication service
                _authService = new AuthenticationService();

                // Perform authentication
                var authResult = await _authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                {
                    _loggingService.LogSuccess("✓ Autentisering fullført!");
                    
                    IsAuthenticated = true;
                    _userParameters = authResult.Parameters;
                    
                    // Update command states after authentication
                    ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                    
                    // Initialize dashboard with credentials
                    var servicesUserParams = new Services.UserParameters
                    {
                        FylkeId = authResult.Parameters.FylkeId,
                        PlanPeri = authResult.Parameters.PlanPeri,
                        SkoleId = authResult.Parameters.SkoleId
                    };
                    
                    Dashboard.SetCredentials(servicesUserParams, authResult.Cookies);
                    
                    _loggingService.LogInfo("📊 Laster dashboard data...");
                    await Dashboard.RefreshDataAsync();
                    
                    _loggingService.LogSuccess("✓ Applikasjon er klar!");
                    StatusMessage = "Klar til å starte";

                    _updateChecker?.StartPeriodicChecks();
                    _loggingService.LogInfo("Update checker ready");

                    // Reset retry count on success
                    _initializationRetryCount = 0;
                    
                    await Task.Delay(500);
                    IsLoading = false;
                }
                else
                {
                    _initializationRetryCount++;
                    
                    // Use specific error message if available
                    string errorMessage = !string.IsNullOrEmpty(authResult.ErrorMessage) 
                        ? authResult.ErrorMessage 
                        : "Kunne ikke autentisere med iskole.net. Sjekk innloggingsdata i innstillinger.";
                    
                    if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
                    {
                        _loggingService.LogError($"❌ Autentisering mislyktes etter {MAX_RETRY_ATTEMPTS} forsøk - stopper automatiske forsøk");
                        _loggingService.LogError($"Feilmelding: {errorMessage}");
                        await _notificationService.ShowNotificationAsync(
                            "Autentisering mislyktes",
                            $"Etter {MAX_RETRY_ATTEMPTS} forsøk: {errorMessage}",
                            NotificationLevel.Error
                        );
                        
                        StatusMessage = "Autentisering mislyktes - sjekk innstillinger";
                        IsLoading = false;
                        return; // Stop retrying
                    }
                    
                    _loggingService.LogError($"❌ Autentisering mislyktes (forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS}) - prøver igjen om {3 * _initializationRetryCount} sekunder");
                    _loggingService.LogError($"Feilmelding: {errorMessage}");
                    await _notificationService.ShowNotificationAsync(
                        "Autentisering mislyktes",
                        $"Forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS} mislyktes. Prøver igjen...",
                        NotificationLevel.Warning
                    );
                    
                    // Exponential backoff: 3s, 6s, 9s
                    await Task.Delay(3000 * _initializationRetryCount);
                    await InitializeAsync(); // Retry with increased delay
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Kritisk feil under oppstart: {ex.Message}");
                await _notificationService.ShowNotificationAsync(
                    "Oppstartsfeil",
                    "En kritisk feil oppstod under oppstart. Prøver igjen...",
                    NotificationLevel.Error
                );
                
                await Task.Delay(3000);
                await InitializeAsync(); // Retry
            }
        }
        #endregion

        #region Service Event Subscriptions
        private void SubscribeToServiceEvents()
        {
            // Subscribe to logging events for status updates
            _loggingService.LogEntryAdded += OnLogEntryAdded;
            
            // Subscribe to notification events
            _notificationService.NotificationAdded += OnNotificationAdded;
            _notificationService.NotificationDismissed += OnNotificationDismissed;
            
            // Subscribe to settings changes
            _settingsService.SettingsChanged += OnSettingsChanged;
            
            // Subscribe to automation events
            _automationService.StatusChanged += OnAutomationStatusChanged;
            _automationService.ProgressUpdated += OnAutomationProgressUpdated;
        }

        private void OnLogEntryAdded(object? sender, LogEntryEventArgs e)
        {
            // Update status message for important log entries
            if (ShouldShowInStatus(e.LogEntry.Message ?? "", e.LogEntry.Level ?? ""))
            {
                StatusMessage = e.LogEntry.FormattedMessage;
            }
        }

        private void OnNotificationAdded(object? sender, NotificationEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentNotification = e.Notification;
                OnPropertyChanged(nameof(HasCurrentNotification));
            });
        }

        private void OnNotificationDismissed(object? sender, NotificationEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CurrentNotification?.Id == e.Notification.Id)
                {
                    CurrentNotification = null;
                    OnPropertyChanged(nameof(HasCurrentNotification));
                }
            });
        }

        private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        {
            // Handle settings changes that affect the main window
            if (e.PropertyName == nameof(_settingsService.ShowDetailedLogs))
            {
                OnPropertyChanged(nameof(ShowDetailedLogs));
            }
        }

        private void OnAutomationStatusChanged(object? sender, AutomationStatusChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = e.Status;
                OnPropertyChanged(nameof(IsAutomationRunning));
                ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
            });
        }

        private void OnAutomationProgressUpdated(object? sender, AutomationProgressEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = e.Message;
            });
        }
        #endregion

        #region Command Implementations
        private async Task StartAutomationAsync()
        {
            if (!IsAuthenticated || _userParameters == null || !_userParameters.IsComplete)
            {
                _loggingService.LogError("Ikke autentisert - kan ikke starte automatisering");
                await _notificationService.ShowNotificationAsync(
                    "Autentiseringsfeil",
                    "Du må være innlogget for å starte automatisering",
                    NotificationLevel.Error
                );
                return;
            }

            // Set credentials in automation service
            if (_automationService is AutomationService automationService)
            {
                var cookies = await SecureCredentialStorage.LoadCookiesAsync();
                if (cookies != null)
                {
                    automationService.SetCredentials(_userParameters, cookies);
                }
            }

            var result = await _automationService.StartAsync();
            
            if (!result.Success)
            {
                await _notificationService.ShowNotificationAsync(
                    "Automatisering feilet",
                    result.Message ?? "Ukjent feil",
                    NotificationLevel.Error
                );
            }
            
            // Update UI
            OnPropertyChanged(nameof(IsAutomationRunning));
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
        }

        private async Task StopAutomationAsync()
        {
            var result = await _automationService.StopAsync();
            
            if (!result.Success)
            {
                await _notificationService.ShowNotificationAsync(
                    "Stopp feilet",
                    result.Message ?? "Kunne ikke stoppe automatisering",
                    NotificationLevel.Warning
                );
            }
            
            // Update UI
            OnPropertyChanged(nameof(IsAutomationRunning));
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
        }

        private Task BackToDashboardAsync()
        {
            _loggingService.LogInfo("Tilbake til dashboard...");
            
            ShowTutorial = false;
            ShowSettings = false;
            ShowFeide = false;
            ShowDashboard = true;
            
            return Task.CompletedTask;
        }

        private Task OpenSettingsAsync()
        {
            _loggingService.LogInfo("Åpner innstillinger...");

            // Connect SettingsViewModel to logging service
            SettingsViewModel.ConnectToMainViewModel(this);

            ShowDashboard = false;
            ShowTutorial = false;
            ShowFeide = false;
            ShowSettings = true;

            // Subscribe to close event
            SettingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            SettingsViewModel.CloseRequested += OnSettingsCloseRequested;

            return Task.CompletedTask;
        }

        private Task OpenFeideAsync()
        {
            _loggingService.LogInfo("Åpner Feide-pålogging...");

            ShowDashboard = false;
            ShowSettings = false;
            ShowTutorial = false;
            ShowFeide = true;

            // Subscribe to events
            FeideViewModel.SetupCompleted -= OnFeideSetupCompleted;
            FeideViewModel.SetupCompleted += OnFeideSetupCompleted;

            return Task.CompletedTask;
        }

        private Task OpenTutorialAsync()
        {
            _loggingService.LogInfo("Åpner veiledning...");
            
            ShowDashboard = false;
            ShowSettings = false;
            ShowFeide = false;
            ShowTutorial = true;

            return Task.CompletedTask;
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                _loggingService.LogInfo("🔄 Oppdaterer data...");
                StatusMessage = "Oppdaterer...";

                await Dashboard.RefreshDataAsync();
                
                _loggingService.LogSuccess("✓ Data oppdatert!");
                StatusMessage = "Data oppdatert!";
                
                await _notificationService.ShowNotificationAsync(
                    "Data Oppdatert",
                    "Dashboard-data er oppdatert med fersk informasjon.",
                    NotificationLevel.Success
                );

                // Reset status after 3 seconds
                await Task.Delay(3000);
                if (StatusMessage == "Data oppdatert!")
                {
                    StatusMessage = IsAutomationRunning ? "Automatisering kjører..." : "Klar til å starte";
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Feil ved oppdatering: {ex.Message}");
                StatusMessage = "Oppdatering mislyktes";
                
                await _notificationService.ShowNotificationAsync(
                    "Oppdateringsfeil",
                    "Kunne ikke oppdatere data. Prøv igjen senere.",
                    NotificationLevel.Warning
                );
            }
        }

        private Task ClearLogsAsync()
        {
            _loggingService.ClearLogs();
            return Task.CompletedTask;
        }

        private Task ToggleDetailedLogsAsync()
        {
            ShowDetailedLogs = !ShowDetailedLogs;
            _loggingService.LogInfo($"Detaljert logging {(ShowDetailedLogs ? "aktivert" : "deaktivert")}");
            return Task.CompletedTask;
        }

        private async Task DismissCurrentNotificationAsync()
        {
            if (CurrentNotification != null)
            {
                await _notificationService.DismissNotificationAsync(CurrentNotification.Id);
            }
        }

        private Task ToggleThemeAsync()
        {
            Services.ThemeManager.Instance.ToggleTheme();
            _loggingService.LogInfo($"Theme changed to {(Services.ThemeManager.Instance.IsDarkMode ? "dark" : "light")} mode");
            return Task.CompletedTask;
        }

        private async Task RetryAuthenticationAsync()
        {
            _loggingService.LogInfo("🔄 Manuell retry av autentisering startet...");
            
            // Reset retry count for manual retry
            _initializationRetryCount = 0;
            
            // Reset authentication state
            IsAuthenticated = false;
            _userParameters = null;
            
            // Update command states
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)RetryAuthenticationCommand).RaiseCanExecuteChanged();
            
            // Start initialization again
            await InitializeAsync();
        }
        #endregion

        #region Event Handlers
        private void OnSettingsCloseRequested(object? sender, EventArgs e)
        {
            _ = BackToDashboardAsync();
        }

        private void OnFeideSetupCompleted(object? sender, FeideSetupCompletedEventArgs e)
        {
            if (e.Success)
            {
                _loggingService.LogSuccess($"✓ Feide-oppsett fullført for {e.UserEmail}");
                
                // Reset retry count
                _initializationRetryCount = 0;
                // Note: Navigation and StartInitializationAsync() is handled by App.axaml.cs
            }
            else
            {
                _loggingService.LogError("❌ Feide-oppsett mislyktes");
            }
        }
        #endregion

        #region Helper Methods
        private bool ShouldShowInStatus(string message, string level)
        {
            if (level == "ERROR" || level == "SUCCESS")
            {
                return true;
            }

            var importantMessages = new[]
            {
                "Applikasjon er klar",
                "Starter automatisering...",
                "Automatisering stoppet",
                "Automatisering fullført",
                "Innlogging fullført",
                "Autentisering og parametere fullført",
                "Registreringsvindu er ÅPENT",
                "Forsøker å registrere oppmøte",
                "Alle STU-økter er håndtert",
                "Syklus #"
            };

            return importantMessages.Any(important => message.StartsWith(important));
        }



        private new bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected new virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            _updateChecker?.Dispose();
            _httpClient?.Dispose();
            _authService?.Dispose();
            FeideViewModel?.Dispose();
        }

        public async Task RefreshAutoStartStatusAsync()
        {
            try
            {
                _loggingService.LogInfo("[AUTO-START] Manual refresh triggered - checking status immediately");
                
                // Check school hours and potentially auto-start automation
                bool isSchoolHours = await _automationService.CheckSchoolHoursAsync();
                
                if (isSchoolHours && !IsAutomationRunning)
                {
                    await _settingsService.LoadSettingsAsync();
                    if (_settingsService.AutoStartAutomation)
                    {
                        _loggingService.LogInfo("Auto-starting automation due to school hours...");
                        await StartAutomationAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error refreshing auto-start status: {ex.Message}");
            }
        }
        
        #endregion
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null!)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !_isExecuting && _canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}