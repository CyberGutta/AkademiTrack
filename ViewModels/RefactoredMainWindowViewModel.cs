using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        private readonly AnalyticsService _analyticsService;
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
        public ICommand ShowNotificationPermissionDialogCommand { get; }
        #endregion

        #region Constructor
        public RefactoredMainWindowViewModel(bool skipInitialization = false)
        {
            // Get services from service locator
            _loggingService = ServiceLocator.Instance.GetService<ILoggingService>();
            _notificationService = ServiceLocator.Instance.GetService<INotificationService>();
            _settingsService = ServiceLocator.Instance.GetService<ISettingsService>();
            _automationService = ServiceLocator.Instance.GetService<IAutomationService>();
            _analyticsService = ServiceLocator.Instance.GetService<AnalyticsService>();
            
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
            ShowNotificationPermissionDialogCommand = new AsyncRelayCommand(ShowNotificationPermissionDialogAsync);

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

                // Track app initialization
                await _analyticsService.TrackEventAsync("app_initialization_started", new { 
                    retry_count = _initializationRetryCount 
                });

                // Initialize authentication service
                _authService = new AuthenticationService();

                // Perform authentication
                var authResult = await _authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                {
                    _loggingService.LogSuccess("✓ Autentisering fullført!");
                    
                    // Track successful initialization
                    await _analyticsService.TrackEventAsync("app_initialization_success", new { 
                        school_id = authResult.Parameters.SkoleId,
                        fylke_id = authResult.Parameters.FylkeId 
                    });
                    
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

                    // Check notification permissions on macOS
                    await CheckNotificationPermissionsAsync();

                    // Reset retry count on success
                    _initializationRetryCount = 0;
                    
                    await Task.Delay(500);
                    IsLoading = false;
                }
                else
                {
                    _initializationRetryCount++;
                    
                    // Track initialization failure
                    await _analyticsService.TrackEventAsync("app_initialization_failed", new { 
                        retry_count = _initializationRetryCount,
                        error_message = authResult.ErrorMessage 
                    });
                    
                    // Use specific error message if available
                    string errorMessage = !string.IsNullOrEmpty(authResult.ErrorMessage) 
                        ? authResult.ErrorMessage 
                        : "Kunne ikke autentisere med iskole.net. Sjekk innloggingsdata i innstillinger.";
                    
                    if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
                    {
                        // Only log to error_logs table on final failure (not every retry)
                        await _analyticsService.LogErrorAsync(
                            "app_initialization_critical_failure",
                            $"Failed after {MAX_RETRY_ATTEMPTS} attempts: {errorMessage}"
                        );
                        
                        _loggingService.LogError($"❌ Autentisering mislyktes etter {MAX_RETRY_ATTEMPTS} forsøk - stopper automatiske forsøk");
                        _loggingService.LogError($"Feilmelding: {errorMessage}");
                        await _notificationService.ShowNotificationAsync(
                            "Autentisering mislyktes",
                            $"Etter {MAX_RETRY_ATTEMPTS} forsøk: {errorMessage}",
                            NotificationLevel.Error
                        );
                        
                        
                        StatusMessage = "Autentisering mislyktes - sjekk innstillinger eller nettverk";
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
                // Log critical exception for developers
                await _analyticsService.LogErrorAsync(
                    "app_initialization_exception",
                    ex.Message,
                    ex
                );
                
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

            // Track automation start
            await _analyticsService.TrackEventAsync("automation_started", new { 
                school_id = _userParameters?.SkoleId,
                manual_start = true 
            });

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
                // Log automation failure for developers
                await _analyticsService.LogErrorAsync(
                    "automation_start_failure", 
                    result.Message ?? "Unknown automation start error"
                );
                
                await _notificationService.ShowNotificationAsync(
                    "Automatisering feilet",
                    result.Message ?? "Nettverk eller autentiseringsfeil ved start av automatisering",
                    NotificationLevel.Error
                );
                
                // Track automation start failure
                await _analyticsService.TrackEventAsync("automation_start_failed", new { 
                    error = result.Message,
                    school_id = _userParameters?.SkoleId 
                });
            }
            
            // Update UI
            OnPropertyChanged(nameof(IsAutomationRunning));
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
        }

        private async Task StopAutomationAsync()
        {
            // Track automation stop
            await _analyticsService.TrackEventAsync("automation_stopped", new { 
                manual_stop = true 
            });

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

        private async Task OpenSettingsAsync()
        {
            _loggingService.LogInfo("Åpner innstillinger...");
            
            // Track settings navigation
            await _analyticsService.TrackEventAsync("navigation_settings");

            // Connect SettingsViewModel to logging service
            SettingsViewModel.ConnectToMainViewModel(this);

            ShowDashboard = false;
            ShowTutorial = false;
            ShowFeide = false;
            ShowSettings = true;

            // Subscribe to close event
            SettingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            SettingsViewModel.CloseRequested += OnSettingsCloseRequested;
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

        private async Task OpenTutorialAsync()
        {
            _loggingService.LogInfo("Åpner veiledning...");
            
            // Track tutorial navigation
            await _analyticsService.TrackEventAsync("navigation_tutorial");
            
            ShowDashboard = false;
            ShowSettings = false;
            ShowFeide = false;
            ShowTutorial = true;
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

        private async Task ShowNotificationPermissionDialogAsync()
        {
            _loggingService.LogInfo("🔔 Manually showing notification permission dialog...");
            
            // Reset the dismissed flag so the dialog can be shown again
            await NotificationPermissionChecker.ResetDialogDismissedAsync();
            
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    // Check if dialog is already open to prevent duplicates
                    if (Views.NotificationPermissionDialog.IsDialogCurrentlyOpen)
                    {
                        _loggingService.LogInfo("Notification permission dialog already open - skipping");
                        return;
                    }

                    var dialog = new Views.NotificationPermissionDialog();
                    
                    // Get the main window to set as owner
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow != null)
                    {
                        await dialog.ShowDialog(desktop.MainWindow);
                        
                        if (dialog.UserGrantedPermission)
                        {
                            _loggingService.LogSuccess("✓ User granted notification permissions");
                            await _notificationService.ShowNotificationAsync(
                                "Varsler aktivert",
                                "Du vil nå motta viktige varsler fra AkademiTrack",
                                NotificationLevel.Success
                            );
                        }
                        else
                        {
                            _loggingService.LogInfo("User declined notification permissions");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Error showing notification permission dialog: {ex.Message}");
                }
            });
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
        private async Task CheckNotificationPermissionsAsync()
        {
            try
            {
                // Only check once per day to avoid annoying users
                if (!await ShouldCheckNotificationPermissions())
                {
                    _loggingService.LogInfo("🔔 Notification permission check skipped (already checked today)");
                    return;
                }

                var permissionStatus = await NotificationPermissionChecker.CheckMacNotificationPermissionAsync();
                
                _loggingService.LogInfo($"🔔 Notification permission status: {permissionStatus}");
                
                // Mark that we've checked today
                await MarkNotificationPermissionCheckedToday();
                
                if (permissionStatus == NotificationPermissionChecker.PermissionStatus.NotDetermined)
                {
                    _loggingService.LogInfo("🔔 Notification permissions not determined - showing permission dialog");
                    
                    // Show the notification permission dialog
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            // Check if dialog is already open to prevent duplicates
                            if (Views.NotificationPermissionDialog.IsDialogCurrentlyOpen)
                            {
                                _loggingService.LogInfo("Notification permission dialog already open - skipping");
                                return;
                            }

                            var dialog = new Views.NotificationPermissionDialog();
                            
                            // Get the main window to set as owner
                            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                                desktop.MainWindow != null)
                            {
                                await dialog.ShowDialog(desktop.MainWindow);
                                
                                if (dialog.UserGrantedPermission)
                                {
                                    _loggingService.LogSuccess("✓ User granted notification permissions");
                                    await _notificationService.ShowNotificationAsync(
                                        "Varsler aktivert",
                                        "Du vil nå motta viktige varsler fra AkademiTrack",
                                        NotificationLevel.Success
                                    );
                                }
                                else
                                {
                                    _loggingService.LogInfo("User declined notification permissions");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError($"Error showing notification permission dialog: {ex.Message}");
                        }
                    });
                }
                else if (permissionStatus == NotificationPermissionChecker.PermissionStatus.Authorized)
                {
                    _loggingService.LogInfo("✓ Notification permissions already granted");
                }
                else if (permissionStatus == NotificationPermissionChecker.PermissionStatus.Denied)
                {
                    _loggingService.LogInfo("ℹ️ Notification permissions denied by user - you can re-enable them in settings");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking notification permissions: {ex.Message}");
            }
        }

        private async Task<bool> ShouldCheckNotificationPermissions()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string settingsPath = Path.Combine(appDataDir, "settings.json");

                if (File.Exists(settingsPath))
                {
                    string json = await File.ReadAllTextAsync(settingsPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                    
                    if (settings != null && settings.TryGetValue("LastNotificationPermissionCheck", out var lastCheckObj))
                    {
                        if (DateTime.TryParse(lastCheckObj.ToString(), out var lastCheck))
                        {
                            // Only check once per day
                            return DateTime.Now.Date > lastCheck.Date;
                        }
                    }
                }
                
                return true; // First time, should check
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking if should check notification permissions: {ex.Message}");
                return true; // Default to checking if there's an error
            }
        }

        private async Task MarkNotificationPermissionCheckedToday()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                
                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                string settingsPath = Path.Combine(appDataDir, "settings.json");
                
                var settings = new System.Collections.Generic.Dictionary<string, object>();
                
                if (File.Exists(settingsPath))
                {
                    string json = await File.ReadAllTextAsync(settingsPath);
                    try
                    {
                        settings = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json) 
                            ?? new System.Collections.Generic.Dictionary<string, object>();
                    }
                    catch
                    {
                        settings = new System.Collections.Generic.Dictionary<string, object>();
                    }
                }

                settings["LastNotificationPermissionCheck"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                
                string updatedJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(settingsPath, updatedJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error marking notification permission checked: {ex.Message}");
            }
        }

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