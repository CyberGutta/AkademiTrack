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
    public partial class RefactoredMainWindowViewModel : ViewModelBase, INotifyPropertyChanged, IDisposable
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
        private bool _disposed = false;
        private Timer? _dataRefreshTimer;
        private Timer? _nextClassUpdateTimer; 
        private Timer? _midnightResetTimer;
        private DateTime _lastDataRefresh = DateTime.MinValue;
        private DateTime _currentDay = DateTime.Now.Date;

        private bool _isRefreshingData = false;
        private DateTime _lastManualRefresh = DateTime.MinValue;
        private const int REFRESH_COOLDOWN_SECONDS = 10; // Minimum 10 seconds between refreshes

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
        private Timer? _autoStartCheckTimer;

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
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettings);
            OpenFeideCommand = new AsyncRelayCommand(OpenFeideAsync);
            OpenTutorialCommand = new AsyncRelayCommand(OpenTutorial);
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

                Debug.WriteLine("[MainWindow] App initialization started");

                // Initialize authentication service
                _authService = new AuthenticationService(_notificationService);

                // ✅ Authentication now runs on background thread - UI won't freeze
                var authResult = await _authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                {
                    _loggingService.LogSuccess("✓ Autentisering fullført!");

                    Debug.WriteLine("[MainWindow] App initialization successful");

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

                    // ✅ Dashboard refresh should also be async
                    await Dashboard.RefreshDataAsync();

                    Dashboard.UpdateNextClassFromCache();

                    _loggingService.LogSuccess("✓ Applikasjon er klar!");
                    StatusMessage = "Klar til å starte";

                    _updateChecker?.StartPeriodicChecks();
                    _loggingService.LogInfo("Update checker ready");

                    await CheckAutoStartAutomationAsync();

                    StartDashboardRefreshTimer();
                    StartMidnightResetTimer();

                    _initializationRetryCount = 0;

                    await Task.Delay(500);
                    IsLoading = false;
                }
                else
                {
                    _initializationRetryCount++;

                    Debug.WriteLine("[MainWindow] App initialization failed");

                    string errorMessage = !string.IsNullOrEmpty(authResult.ErrorMessage)
                        ? authResult.ErrorMessage
                        : "Kunne ikke autentisere med iskole.net. Sjekk innloggingsdata i innstillinger.";

                    if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
                    {
                        try
                        {
                            await _analyticsService.LogErrorAsync(
                                "app_initialization_critical_failure",
                                $"Failed after {MAX_RETRY_ATTEMPTS} attempts: {errorMessage}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Analytics] Failed to log initialization failure: {ex.Message}");
                        }

                        _loggingService.LogError($"❌ Autentisering mislyktes etter {MAX_RETRY_ATTEMPTS} forsøk - stopper automatiske forsøk");
                        _loggingService.LogError($"Feilmelding: {errorMessage}");
                        await _notificationService.ShowNotificationAsync(
                            "Autentisering mislyktes",
                            $"Etter {MAX_RETRY_ATTEMPTS} forsøk: {errorMessage}",
                            NotificationLevel.Error
                        );

                        StatusMessage = "Autentisering mislyktes - sjekk innstillinger eller nettverk";
                        IsLoading = false;
                        return;
                    }

                    _loggingService.LogError($"❌ Autentisering mislyktes (forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS}) - prøver igjen om {3 * _initializationRetryCount} sekunder");
                    _loggingService.LogError($"Feilmelding: {errorMessage}");
                    await _notificationService.ShowNotificationAsync(
                        "Autentisering mislyktes",
                        $"Forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS} mislyktes. Prøver igjen...",
                        NotificationLevel.Warning
                    );

                    await Task.Delay(3000 * _initializationRetryCount);
                    await InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "app_initialization_exception",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log initialization exception: {analyticsEx.Message}");
                }

                _loggingService.LogError($"Kritisk feil under oppstart: {ex.Message}");
                await _notificationService.ShowNotificationAsync(
                    "Oppstartsfeil",
                    "En kritisk feil oppstod under oppstart. Prøver igjen...",
                    NotificationLevel.Error
                );

                await Task.Delay(3000);
                await InitializeAsync();
            }
        }

        private void StartDashboardRefreshTimer()
        {
            _loggingService.LogInfo("⏰ Starting dashboard auto-refresh timer...");

            // Refresh dashboard every 5 minutes when automation is NOT running
            _dataRefreshTimer = new Timer(
                async _ =>
                {
                    try
                    {
                        // Only auto-refresh if automation is not running
                        if (!IsAutomationRunning)
                        {
                            _loggingService.LogDebug("🔄 Auto-refreshing dashboard data...");

                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await Dashboard.RefreshDataAsync();
                            });

                            _loggingService.LogDebug("✓ Dashboard auto-refresh complete");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Dashboard auto-refresh error: {ex.Message}");
                    }
                },
                null,
                TimeSpan.FromMinutes(5), 
                TimeSpan.FromMinutes(5)
            );

            _loggingService.LogInfo("✓ Dashboard auto-refresh timer started (every 5 minutes)");
        }

        private void StartMidnightResetTimer()
        {
            _loggingService.LogInfo("⏰ Starting midnight reset timer...");

            // Check for new day every 5 minutes
            _midnightResetTimer = new Timer(
                async _ =>
                {
                    try
                    {
                        await CheckMidnightResetAsync();
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Midnight reset check error: {ex.Message}");
                    }
                },
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5)
            );

            _loggingService.LogInfo("✓ Midnight reset timer started (checks every 5 minutes)");
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
            _automationService.SessionRegistered += OnSessionRegistered;

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

        private void OnSessionRegistered(object? sender, SessionRegisteredEventArgs e)
        {
            _loggingService.LogInfo($"[DASHBOARD] Session {e.SessionTime} registered - updating display");
            
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    // 1. Immediate cache-based updates (fast, no network)
                    Dashboard.IncrementRegisteredSessionCount();
                    _loggingService.LogDebug("[DASHBOARD] ✓ Today's count updated from cache");
                    
                    Dashboard.UpdateNextClassFromCache();
                    _loggingService.LogDebug("[DASHBOARD] ✓ Next class updated from cache");
                    
                    // 2. Full data refresh after short delay (fetches fresh data from server)
                    await Task.Delay(500); // Give server time to process registration
                    
                    try
                    {
                        _loggingService.LogDebug("[DASHBOARD] Refreshing all data from server...");
                        await Dashboard.RefreshDataAsync();
                        _loggingService.LogSuccess("[DASHBOARD] ✓ All data refreshed (today, weekly, monthly, overtime)");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"[DASHBOARD] Full refresh failed: {ex.Message}");
                        // Cache updates already succeeded, so UI is still updated
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"[DASHBOARD] Error updating display: {ex.Message}");
                }
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

            // ✅ Only refresh if cache is stale (no data or new day)
            if (Dashboard.IsCacheStale())
            {
                _loggingService.LogInfo("📊 Cache is stale - refreshing dashboard before automation...");
                StatusMessage = "Oppdaterer data...";

                try
                {
                    await Dashboard.RefreshDataAsync();
                    _lastDataRefresh = DateTime.Now;
                    _loggingService.LogSuccess("✓ Dashboard data refreshed");
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Kunne ikke oppdatere dashboard: {ex.Message}");
                    await _notificationService.ShowNotificationAsync(
                        "Advarsel",
                        "Kunne ikke oppdatere dashboard data. Starter automatisering likevel...",
                        NotificationLevel.Warning
                    );
                }
            }
            else
            {
                _loggingService.LogInfo("✓ Using cached data (from today - still fresh)");
            }

            // Track automation start
            try
            {
                await _analyticsService.TrackAutomationStartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Failed to track automation start: {ex.Message}");
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

            StatusMessage = "Starter automatisering...";
            var result = await _automationService.StartAsync();

            if (!result.Success)
            {
                // Log automation failure for developers
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "automation_start_failure",
                        result.Message ?? "Unknown automation start error"
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Analytics] Failed to log automation start: {ex.Message}");
                }

                await _notificationService.ShowNotificationAsync(
                    "Automatisering feilet",
                    result.Message ?? "Nettverk eller autentiseringsfeil ved start av automatisering",
                    NotificationLevel.Error
                );

                Debug.WriteLine("[MainWindow] Automation start failed");
            }

            // Update UI
            OnPropertyChanged(nameof(IsAutomationRunning));
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
        }

        private async Task StopAutomationAsync()
        {
            // Mark manual stop FIRST
            await SchoolTimeChecker.MarkManualStopAsync();
            
            // Track automation stop
            Debug.WriteLine("[MainWindow] Automation stopped");
            try
            {
                await _analyticsService.TrackAutomationStopAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Failed to track automation stop: {ex.Message}");
            }

            var result = await _automationService.StopAsync();
            
            if (!result.Success)
            {
                await _notificationService.ShowNotificationAsync(
                    "Stopp feilet",
                    result.Message ?? "Kunne ikke stoppe automatisering",
                    NotificationLevel.Warning
                );
            }
            
            // Update UI - waits for UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IsAutomationRunning));
                ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
            });
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

        private Task OpenSettings()
        {
            _loggingService.LogInfo("Åpner innstillinger...");

            // Track settings navigation - removed events tracking
            Debug.WriteLine("[MainWindow] Navigating to settings");

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

        private Task OpenTutorial()
        {
            _loggingService.LogInfo("Åpner veiledning...");

            // Track tutorial navigation - removed events tracking
            Debug.WriteLine("[MainWindow] Navigating to tutorial");

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
                
                // Removed notification - data refresh should be silent

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

        

        private async Task CheckMidnightResetAsync()
        {
            var now = DateTime.Now.Date;
            
            if (now > _currentDay)
            {
                _loggingService.LogInfo($"🌅 NEW DAY DETECTED! Changing from {_currentDay:yyyy-MM-dd} to {now:yyyy-MM-dd}");
                _currentDay = now;
                
                SchoolTimeChecker.ResetDailyCompletion();
                _loggingService.LogInfo("✓ Daily completion flags reset");
                
                Dashboard.ClearCache();
                _loggingService.LogInfo("✓ Dashboard cache cleared for new day");
                
                if (_settingsService.AutoStartAutomation)
                {
                    _loggingService.LogInfo("🔍 Checking if automation should auto-start for new day...");
                    await CheckAutoStartAutomationAsync();
                }
                
            }
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

        private async Task CheckAutoStartAutomationAsync()
        {
            try
            {
                _loggingService.LogInfo("🔍 CHECKING AUTO-START AUTOMATION");
                
                await _settingsService.LoadSettingsAsync();
                var autoStartEnabled = _settingsService.AutoStartAutomation;
                
                _loggingService.LogInfo($"AutoStartAutomation setting: {autoStartEnabled}");
                _loggingService.LogInfo($"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _loggingService.LogInfo($"Current day: {DateTime.Now.DayOfWeek}");
                
                if (!autoStartEnabled)
                {
                    _loggingService.LogInfo("❌ Auto-start is DISABLED in settings");
                    _loggingService.LogInfo("   Periodic checking will NOT run");
                    
                    // Stop timer if it's running - must be on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _autoStartCheckTimer?.Dispose();
                        _autoStartCheckTimer = null;
                    });
                    return;
                }
                
                // Use the proper method that checks manual stops and completion
                var (shouldStart, reason, nextStartTime, shouldNotify) = await SchoolTimeChecker.ShouldAutoStartAutomationAsync(silent: false);
                
                _loggingService.LogInfo($"Auto-start check result: {reason}");
                
                if (shouldStart && !IsAutomationRunning)
                {
                    _loggingService.LogInfo("🚀 Starting automation automatically...");
                    
                    // Mark as started
                    await SchoolTimeChecker.MarkTodayAsStartedAsync();
                    
                    // Must start automation on UI thread
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await Task.Delay(500);
                        await StartAutomationAsync();
                    });
                }
                else if (IsAutomationRunning)
                {
                    _loggingService.LogInfo("✅ Automation is already running");
                }
                else
                {
                    _loggingService.LogInfo($"❌ Not auto-starting: {reason}");
                    if (nextStartTime.HasValue)
                    {
                        _loggingService.LogInfo($"   Next auto-start: {nextStartTime.Value:yyyy-MM-dd HH:mm}");
                    }
                }
                
                // START PERIODIC CHECKING (every 30 seconds) - must be on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_autoStartCheckTimer == null)
                    {
                        _loggingService.LogInfo("⏰ Starting periodic auto-start checker (checks every 30 seconds)");
                        _autoStartCheckTimer = new Timer(
                            async _ => 
                            {
                                try
                                {
                                    await CheckAutoStartAutomationAsync();
                                }
                                catch (Exception ex)
                                {
                                    _loggingService.LogError($"Error in auto-start timer: {ex.Message}");
                                }
                            },
                            null,
                            TimeSpan.FromSeconds(30), // First check after 30 seconds
                            TimeSpan.FromSeconds(30)  // Then every 30 seconds
                        );
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking auto-start: {ex.Message}");
                Debug.WriteLine($"[AUTO-START] Error: {ex}");
            }
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
                        
                        // waits on UI thread
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await StartAutomationAsync();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error refreshing auto-start status: {ex.Message}");
            }
        }

        public async Task CheckForStaleDataAsync()
        {
            try
            {
                // Check if cache is from a different day (computer was asleep/closed)
                if (Dashboard.IsCacheStale())
                {
                    _loggingService.LogInfo("🔄 Window activated - cache is stale, refreshing data...");
                    await Dashboard.RefreshDataAsync();
                }
                else
                {
                    // Cache is fresh, but still update next class display in case timer missed
                    Dashboard.UpdateNextClassFromCache();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking for stale data: {ex.Message}");
            }
        }
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

    // Dispose implementation for RefactoredMainWindowViewModel
    public partial class RefactoredMainWindowViewModel
    {
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Dispose timers
                _dataRefreshTimer?.Dispose();
                _dataRefreshTimer = null;

                _nextClassUpdateTimer?.Dispose();
                _nextClassUpdateTimer = null;

                _midnightResetTimer?.Dispose();
                _midnightResetTimer = null;

                _autoStartCheckTimer?.Dispose();
                _autoStartCheckTimer = null;

                // Dispose update checker
                _updateChecker?.Dispose();
                _updateChecker = null;

                // Note: _httpClient is readonly and shared, so we don't dispose or nullify it

                // Dispose services if they implement IDisposable
                if (_automationService is IDisposable automationDisposable)
                {
                    automationDisposable.Dispose();
                }

                if (_authService is IDisposable authDisposable)
                {
                    authDisposable.Dispose();
                }

                // Dispose FeideViewModel
                FeideViewModel?.Dispose();

                _disposed = true;
            }
            catch (Exception ex)
            {
                _loggingService?.LogError($"Error disposing RefactoredMainWindowViewModel: {ex.Message}");
            }
        }
    }
}
