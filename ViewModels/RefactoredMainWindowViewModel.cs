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
using AkademiTrack.Services.DependencyInjection;
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
        private readonly UserConfirmationService _userConfirmationService;
        #endregion

        #region Private Fields
        private readonly HttpClient _httpClient;
        private AuthenticationService? _authService;
        private UserParameters? _userParameters;
        private bool _isLoading = true;
        private bool _isAuthenticated = false;
        private string _statusMessage = "Ready";
        private bool _disposed = false;
        private Timer? _dataRefreshTimer;
        private Timer? _nextClassUpdateTimer; 
        private Timer? _midnightResetTimer;
        private DateTime _lastDataRefresh = DateTime.MinValue;
        private DateTime _currentDay = DateTime.Now.Date;

        // Thread-safe refresh management
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
        private volatile bool _isRefreshingData = false;
        private DateTime _lastManualRefresh = DateTime.MinValue;
        private const int REFRESH_COOLDOWN_SECONDS = 10; // Minimum 10 seconds between refreshes

        // Thread-safe initialization management
        private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
        private volatile bool _isInitializing = false;

        // Navigation state with thread safety
        private readonly object _navigationLock = new object();
        private bool _showDashboard = true;
        private bool _showSettings = false;
        private bool _showTutorial = false;
        private bool _showFeide = false;
        
        // Tab navigation for main content
        private string _selectedTab = "Dashboard"; // Dashboard, Kalender, Fravær
        
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
                    ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
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
                    ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        // Navigation with thread safety
        public bool ShowDashboard
        {
            get 
            { 
                lock (_navigationLock) 
                { 
                    return _showDashboard; 
                } 
            }
            set 
            { 
                lock (_navigationLock) 
                { 
                    SetProperty(ref _showDashboard, value); 
                } 
            }
        }

        public bool ShowSettings
        {
            get 
            { 
                lock (_navigationLock) 
                { 
                    return _showSettings; 
                } 
            }
            set 
            { 
                lock (_navigationLock) 
                { 
                    SetProperty(ref _showSettings, value); 
                } 
            }
        }

        public bool ShowTutorial
        {
            get 
            { 
                lock (_navigationLock) 
                { 
                    return _showTutorial; 
                } 
            }
            set 
            { 
                lock (_navigationLock) 
                { 
                    SetProperty(ref _showTutorial, value); 
                } 
            }
        }

        public bool ShowFeide
        {
            get 
            { 
                lock (_navigationLock) 
                { 
                    return _showFeide; 
                } 
            }
            set 
            { 
                lock (_navigationLock) 
                { 
                    SetProperty(ref _showFeide, value); 
                } 
            }
        }

        // Tab Navigation
        public string SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    OnPropertyChanged(nameof(IsDashboardTab));
                    OnPropertyChanged(nameof(IsCalendarTab));
                    OnPropertyChanged(nameof(IsAbsenceTab));
                    
                    if (value == "Kalender" && Calendar != null)
                    {
                        _loggingService.LogInfo("[TAB] Switching to Calendar - loading data");
                        _ = Calendar.LoadCalendarDataAsync();
                    }
                    else if (value == "Kalender" && Calendar == null)
                    {
                        _loggingService.LogWarning("[TAB] Calendar not initialized yet - waiting for authentication");
                    }
                }
            }
        }

        public bool IsDashboardTab => _selectedTab == "Dashboard";
        public bool IsCalendarTab => _selectedTab == "Kalender";
        public bool IsAbsenceTab => _selectedTab == "Fravær";

        // Automation
        public bool IsAutomationRunning => _automationService?.IsRunning ?? false;
        private Timer? _autoStartCheckTimer;

        // Daily confirmation
        private bool _isConfirmationNeeded = false;
        public bool IsConfirmationNeeded
        {
            get => _isConfirmationNeeded;
            private set => SetProperty(ref _isConfirmationNeeded, value);
        }

        // Controls when the confirmation overlay should actually be visible
        private bool _shouldShowConfirmationOverlay = false;
        public bool ShouldShowConfirmationOverlay
        {
            get => _shouldShowConfirmationOverlay;
            private set 
            {
                if (_shouldShowConfirmationOverlay != value)
                {
                    _loggingService?.LogInfo($"🔄 ShouldShowConfirmationOverlay changing from {_shouldShowConfirmationOverlay} to {value}");
                }
                SetProperty(ref _shouldShowConfirmationOverlay, value);
            }
        }

        // Track if we're waiting to show overlay after notifications are handled
        private bool _pendingOverlayShow = false;
        private DateTime _lastNotificationTime = DateTime.MinValue;
        private bool _hasShownInitialConfirmationNotification = false;

        public async Task<bool> IsConfirmationNeededAsync()
        {
            try
            {
                // First check if today is an enabled automation day
                var (shouldStart, _, _, _, needsConfirmation) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: true);
                
                // Only need confirmation if automation should start and needs confirmation
                return shouldStart && needsConfirmation;
            }
            catch
            {
                return false;
            }
        }

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
        public CalendarViewModel? Calendar { get; private set; }
        public FeideWindowViewModel FeideViewModel { get; set; }

        // Application Info
        public string Greeting => "AkademiTrack - STU Tidsregistrering";

        #endregion

        #region Commands
        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand ConfirmPresenceCommand { get; }
        public ICommand BackToDashboardCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenFeideCommand { get; }
        public ICommand OpenTutorialCommand { get; }
        public ICommand RefreshDataCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand DismissNotificationCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ToggleClassViewCommand { get; }
        public ICommand SelectTabCommand { get; }
        #endregion

        #region Constructor
        public RefactoredMainWindowViewModel(bool skipInitialization = false)
        {
            // Get services from service locator
            _loggingService = ServiceContainer.GetService<ILoggingService>();
            _notificationService = ServiceContainer.GetService<INotificationService>();
            _settingsService = ServiceContainer.GetService<ISettingsService>();
            _automationService = ServiceContainer.GetService<IAutomationService>();
            _analyticsService = ServiceContainer.GetService<AnalyticsService>();
            _userConfirmationService = ServiceContainer.GetService<UserConfirmationService>();
            
            // Initialize other services
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(Constants.Network.HTTP_TIMEOUT_SECONDS);
            
            // Initialize ViewModels
            SettingsViewModel = new SettingsViewModel();
            Dashboard = new DashboardViewModel();
            FeideViewModel = new FeideWindowViewModel();

            // Initialize Commands
            StartAutomationCommand = new AsyncRelayCommand(StartAutomationAsync, () => IsAuthenticated && !IsAutomationRunning);
            StopAutomationCommand = new AsyncRelayCommand(StopAutomationAsync, () => IsAutomationRunning);
            ConfirmPresenceCommand = new AsyncRelayCommand(ConfirmPresenceAsync);
            BackToDashboardCommand = new AsyncRelayCommand(BackToDashboardAsync);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettings);
            OpenFeideCommand = new AsyncRelayCommand(OpenFeideAsync);
            OpenTutorialCommand = new AsyncRelayCommand(OpenTutorial);
            RefreshDataCommand = new AsyncRelayCommand(RefreshDataAsync);
            ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new AsyncRelayCommand(ToggleDetailedLogsAsync);
            DismissNotificationCommand = new AsyncRelayCommand(DismissCurrentNotificationAsync);
            ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
            ToggleClassViewCommand = new AsyncRelayCommand(ToggleClassViewAsync);
            SelectTabCommand = new AsyncRelayCommand<string>(SelectTabAsync);

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
        private const int MAX_RETRY_ATTEMPTS = Constants.Network.MAX_RETRY_ATTEMPTS;
        private bool _userCancelledAuth = false;

        public int InitializationRetryCount
        {
            get => _initializationRetryCount;
            private set
            {
                _initializationRetryCount = value;
                OnPropertyChanged(nameof(InitializationRetryCount));
            }
        }

        private async Task InitializeAsync()
        {
            // Check if user cancelled authentication
            if (_userCancelledAuth)
            {
                _loggingService.LogInfo("User cancelled authentication - stopping retry loop");
                IsLoading = false;
                return;
            }

            // Prevent concurrent initialization
            if (_isInitializing)
            {
                _loggingService.LogDebug("Initialization already in progress, skipping duplicate call");
                return;
            }

            if (!await _initializationSemaphore.WaitAsync(100))
            {
                _loggingService.LogWarning("Initialization semaphore timeout - another initialization in progress");
                return;
            }

            try
            {
                if (_isInitializing)
                {
                    _loggingService.LogDebug("Double-check: initialization already in progress");
                    return;
                }

                _isInitializing = true;
                IsLoading = true;
                _loggingService.LogInfo($"Starter AkademiTrack (Forsøk {_initializationRetryCount + 1}/{MAX_RETRY_ATTEMPTS})");

                Debug.WriteLine("[MainWindow] App initialization started");

                // Initialize authentication service
                _authService = new AuthenticationService(_notificationService, false);

                // Authentication now runs on background thread - UI won't freeze
                var authResult = await _authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                {
                    _loggingService.LogSuccess("Autentisering fullført");
                    _loggingService.LogDebug($"[INIT] Auth result: Cookies={authResult.Cookies.Count}, Params complete={authResult.Parameters.IsComplete}");

                    Debug.WriteLine("[MainWindow] App initialization successful");

                    IsAuthenticated = true;
                    _userParameters = authResult.Parameters;
                    
                    _loggingService.LogDebug($"[INIT] Set state: IsAuthenticated={IsAuthenticated}, _userParameters complete={_userParameters.IsComplete}");

                    ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();

                    var servicesUserParams = new Services.UserParameters
                    {
                        FylkeId = authResult.Parameters.FylkeId,
                        PlanPeri = authResult.Parameters.PlanPeri,
                        SkoleId = authResult.Parameters.SkoleId
                    };

                    Dashboard.SetCredentials(servicesUserParams, authResult.Cookies);
                    
                    var attendanceService = new AttendanceDataService();
                    attendanceService.SetCredentials(servicesUserParams, authResult.Cookies);
                    Calendar = new CalendarViewModel(attendanceService, _loggingService);
                    OnPropertyChanged(nameof(Calendar));
                    _loggingService.LogInfo("[INIT] Calendar ViewModel initialized");
                    
                    if (IsCalendarTab)
                    {
                        _ = Calendar.LoadCalendarDataAsync();
                    }

                    _loggingService.LogInfo("Laster cached data for rask visning...");
                    try
                    {
                        await Dashboard.LoadCachedDataAsync();
                        _loggingService.LogSuccess("Cached data vist - UI er klar!");
                    }
                    catch (Exception cacheEx)
                    {
                        _loggingService.LogWarning($"Could not load cached data: {cacheEx.Message}");
                        _loggingService.LogInfo("Ingen cached data - henter fersk data...");
                        try
                        {
                            await Dashboard.RefreshDataAsync();
                            Dashboard.UpdateNextClassFromCache();
                            _loggingService.LogSuccess("Initial data hentet!");
                        }
                        catch (Exception initialEx)
                        {
                            _loggingService.LogError($"Initial data fetch failed: {initialEx.Message}");
                        }
                    }

                    IsLoading = false;
                    _loggingService.LogSuccess("Autentisering fullført - UI er klar");

                    _loggingService.LogInfo("Oppdaterer data i bakgrunnen...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Dashboard.RefreshDataAsync();
                            Dashboard.UpdateNextClassFromCache();
                            _loggingService.LogSuccess("Dashboard data oppdatert!");
                        }
                        catch (Exception dashboardEx)
                        {
                            _loggingService.LogError($"Dashboard refresh failed: {dashboardEx.Message}");
                        }
                    });

                    _loggingService.LogSuccess("Applikasjon er klar!");
                    StatusMessage = "Klar til å starte";

                    try
                    {
                        await UpdateConfirmationStatusAsync();
                        await CheckAutoStartAutomationAsync();

                        StartDashboardRefreshTimer();
                        StartMidnightResetTimer();
                    }
                    catch (Exception serviceEx)
                    {
                        _loggingService.LogError($"Service startup failed: {serviceEx.Message}");
                    }

                    InitializationRetryCount = 0;
                }
                else
                {
                    await HandleInitializationFailure(authResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                await HandleInitializationException(ex);
            }
            finally
            {
                _isInitializing = false;
                _initializationSemaphore.Release();
                
                if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task HandleInitializationFailure(string? errorMessage)
        {
            InitializationRetryCount++;

            Debug.WriteLine("[MainWindow] App initialization failed");

            string finalErrorMessage = !string.IsNullOrEmpty(errorMessage)
                ? errorMessage
                : "Kunne ikke autentisere med iskole.net. Sjekk innloggingsdata i innstillinger.";

            // After 2 failed attempts, suggest opening settings
            if (_initializationRetryCount == 2)
            {
                _loggingService.LogWarning("Flere mislykkede innloggingsforsøk");
                
                // Track multiple login failures
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "authentication_multiple_failures",
                        $"Multiple login failures - user prompted to check settings"
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log multiple login failures: {analyticsEx.Message}");
                }
                
                await _notificationService.ShowNotificationAsync(
                    "Innlogging mislyktes",
                    "Klikk på innstillinger-knappen for å sjekke innloggingsdata.",
                    NotificationLevel.Warning
                );
            }

            if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "app_initialization_critical_failure",
                        $"Failed after {MAX_RETRY_ATTEMPTS} attempts: {finalErrorMessage}"
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Analytics] Failed to log initialization failure: {ex.Message}");
                }

                _loggingService.LogError($"Autentisering mislyktes etter {MAX_RETRY_ATTEMPTS} forsøk - stopper automatiske forsøk");
                _loggingService.LogError($"Feilmelding: {finalErrorMessage}");
                _loggingService.LogError("Dette kan skyldes feil brukernavn eller passord, at brukerkontoen er låst, eller nettverksproblemer.");
                await _notificationService.ShowNotificationAsync(
                    "Innlogging mislyktes",
                    $"Etter {MAX_RETRY_ATTEMPTS} forsøk. Sjekk innloggingsdata i innstillinger.",
                    NotificationLevel.Error
                );

                StatusMessage = "Innlogging mislyktes - klikk innstillinger";
                IsLoading = false;
                return;
            }

            _loggingService.LogError($"Autentisering mislyktes (forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS}) - prøver igjen om {3 * _initializationRetryCount} sekunder");
            _loggingService.LogError($"Feilmelding: {finalErrorMessage}");
            
            // Track retry attempt
            try
            {
                await _analyticsService.LogErrorAsync(
                    "authentication_retry_attempt",
                    $"Login retry {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS}: {finalErrorMessage}"
                );
            }
            catch (Exception analyticsEx)
            {
                Debug.WriteLine($"[Analytics] Failed to log retry attempt: {analyticsEx.Message}");
            }
            
            await _notificationService.ShowNotificationAsync(
                "Innlogging mislyktes",
                $"Forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS}. Prøver igjen...",
                NotificationLevel.Warning
            );

            await Task.Delay(Constants.Time.UI_DELAY_EXTRA_LONG_MS * _initializationRetryCount);
            await InitializeAsync();
        }

        private async Task HandleInitializationException(Exception ex)
        {
            InitializationRetryCount++;

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
            
            // Check if we've exceeded max retries
            if (_initializationRetryCount >= MAX_RETRY_ATTEMPTS)
            {
                _loggingService.LogError($"Oppstart mislyktes etter {MAX_RETRY_ATTEMPTS} forsøk - stopper");
                await _notificationService.ShowNotificationAsync(
                    "Oppstartsfeil",
                    $"Kritisk feil etter {MAX_RETRY_ATTEMPTS} forsøk. Sjekk innstillinger eller nettverk.",
                    NotificationLevel.Error
                );
                
                StatusMessage = "Oppstart mislyktes - sjekk innstillinger eller nettverk";
                IsLoading = false;
                return;
            }

            await _notificationService.ShowNotificationAsync(
                "Oppstartsfeil",
                $"En kritisk feil oppstod under oppstart. Prøver igjen... (forsøk {_initializationRetryCount}/{MAX_RETRY_ATTEMPTS})",
                NotificationLevel.Error
            );

            await Task.Delay(3000 * _initializationRetryCount);
            await InitializeAsync();
        }

        private void StartDashboardRefreshTimer()
        {
            _loggingService.LogInfo("Starting dashboard auto-refresh timer...");

            // Refresh dashboard every 5 minutes when automation is NOT running
            _dataRefreshTimer = new Timer(
                async _ =>
                {
                    try
                    {
                        // Always check confirmation status first
                        await UpdateConfirmationStatusAsync();
                        
                        // Additional check: if confirmation is needed but overlay is not showing, 
                        // and automation should be running, show overlay immediately
                        if (IsConfirmationNeeded && !ShouldShowConfirmationOverlay)
                        {
                            var (shouldStart, _, _, _, needsConfirmation) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: true);
                            if (shouldStart && needsConfirmation)
                            {
                                _loggingService.LogInfo("Confirmation needed and automation should be running - showing overlay");
                                _hasShownInitialConfirmationNotification = false;
                                await RequestOverlayShowAsync();
                            }
                        }
                        
                        // If confirmation is needed and automation is running, stop it
                        if (IsConfirmationNeeded && IsAutomationRunning)
                        {
                            _loggingService.LogWarning("Confirmation lost while automation running - stopping automation");
                            
                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await StopAutomationAsync(markAsManual: false);
                                _loggingService.LogInfo("Automation stopped due to lost confirmation");
                                
                                // Reset notification flag and show overlay immediately
                                _hasShownInitialConfirmationNotification = false;
                                await RequestOverlayShowAsync();
                                _loggingService.LogInfo("Confirmation overlay shown after stopping automation");
                            });
                        }
                        
                        // Only auto-refresh dashboard if automation is not running
                        if (!IsAutomationRunning)
                        {
                            _loggingService.LogDebug("Auto-refreshing dashboard data...");

                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await Dashboard.RefreshDataAsync();
                            });

                            _loggingService.LogDebug("Dashboard auto-refresh complete");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Dashboard auto-refresh error: {ex.Message}");
                    }
                },
                null,
                TimeSpan.FromMinutes(1), // Check every minute instead of 5 for better responsiveness
                TimeSpan.FromMinutes(1)
            );

            _loggingService.LogInfo("Dashboard auto-refresh timer started (every 1 minute)");
        }

        private void StartMidnightResetTimer()
        {
            _loggingService.LogInfo("Starting midnight reset timer...");

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

            _loggingService.LogInfo("Midnight reset timer started (checks every 5 minutes)");
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

            // Subscribe to confirmation events
            _userConfirmationService.ConfirmationLost += OnConfirmationLost;
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
            _lastNotificationTime = DateTime.Now;
            _loggingService.LogDebug($"Notification added: {e.Notification.Title}");

            var isConfirmationNotification = e.Notification.Title?.Contains("Bekreftelse") == true || 
                                           e.Notification.Title?.Contains("BEKREFT") == true ||
                                           e.Notification.Message?.Contains("Trykk 'Ja, jeg er her'") == true;

            if (ShouldShowConfirmationOverlay && !isConfirmationNotification)
            {
                _loggingService.LogInfo("Notification appeared - IMMEDIATELY hiding confirmation overlay");
                ShouldShowConfirmationOverlay = false;
                _pendingOverlayShow = true;
            }
            else if (isConfirmationNotification)
            {
                _loggingService.LogDebug("Confirmation notification - keeping overlay visible");
            }
        }

        private async Task RequestOverlayShowAsync()
        {
            var hasActiveNotifications = _notificationService.HasActiveNotifications;

            if (hasActiveNotifications)
            {
                var activeNotifications = _notificationService.GetActiveNotifications();
                var hasNonConfirmationNotifications = activeNotifications.Any(n => 
                    n.Title?.Contains("Bekreftelse") != true && 
                    n.Title?.Contains("BEKREFT") != true && 
                    n.Message?.Contains("Trykk 'Ja, jeg er her'") != true);

                if (hasNonConfirmationNotifications)
                {
                    _loggingService.LogInfo("BLOCKING overlay - non-confirmation notifications must be handled first. Clearing them now.");

                    // Clear only non-confirmation notifications to allow overlay to show
                    _loggingService.LogInfo("Clearing non-confirmation notifications to allow overlay display");
                    await _notificationService.ClearNonConfirmationNotificationsAsync();
                    
                    // Wait a moment for the UI to update
                    await Task.Delay(100);
                    
                    _loggingService.LogInfo("Non-confirmation notifications cleared - proceeding with overlay");
                }
                else
                {
                    _loggingService.LogDebug("Only confirmation notifications active - allowing overlay to show");
                }
            }

            _loggingService.LogInfo("Showing confirmation overlay now");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShouldShowConfirmationOverlay = true;
            });

            _loggingService.LogInfo($"Confirmation overlay set to visible: {ShouldShowConfirmationOverlay}");

            if (!_hasShownInitialConfirmationNotification)
            {
                _hasShownInitialConfirmationNotification = true;
                _loggingService.LogInfo("Showing confirmation notification - user needs to confirm presence");

                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    await _notificationService.ShowNotificationAsync(
                        "Bekreftelse Påkrevd",
                        "Trykk 'Ja, jeg er her' for å starte automatisk registrering.",
                        NotificationLevel.Warning,
                        isHighPriority: true
                    );
                    _loggingService.LogInfo("Confirmation notification sent");
                });
            }
            else
            {
                _loggingService.LogDebug("Notification already shown - not showing again");
            }
        }

        private async void CheckPendingOverlayShow()
        {
            if (!_pendingOverlayShow) return;

            var hasActiveNotifications = _notificationService.HasActiveNotifications;

            if (hasActiveNotifications)
            {
                var activeNotifications = _notificationService.GetActiveNotifications();
                var hasNonConfirmationNotifications = activeNotifications.Any(n => 
                    n.Title?.Contains("Bekreftelse") != true && 
                    n.Title?.Contains("BEKREFT") != true && 
                    n.Message?.Contains("Trykk 'Ja, jeg er her'") != true);

                if (hasNonConfirmationNotifications)
                {
                    _loggingService.LogDebug("Still have non-confirmation notifications - clearing them to allow overlay");
                    await _notificationService.ClearNonConfirmationNotificationsAsync();
                    await Task.Delay(100); // Give UI time to update
                }
            }

            var timeSinceLastNotification = DateTime.Now - _lastNotificationTime;
            var enoughTimeHasPassed = timeSinceLastNotification.TotalSeconds >= 0.5;

            if (enoughTimeHasPassed)
            {
                _loggingService.LogInfo("No blocking notifications and enough time passed - showing pending overlay");
                _pendingOverlayShow = false;
                ShouldShowConfirmationOverlay = true;
            }
            else
            {
                _loggingService.LogDebug($"Waiting for time to pass - {timeSinceLastNotification.TotalSeconds}s since last notification");
            }
        }

        private void OnNotificationDismissed(object? sender, NotificationEventArgs e)
        {
            _loggingService.LogDebug($"Notification dismissed: {e.Notification.Title}");
            
            CheckPendingOverlayShow();
            
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
            else if (e.PropertyName == nameof(_settingsService.AutoStartAutomation))
            {
                _loggingService.LogInfo($"AutoStartAutomation setting changed to: {e.NewValue}");
                
                // Restart auto-start checking when the setting changes
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CheckAutoStartAutomationAsync();
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Error restarting auto-start check: {ex.Message}");
                    }
                });
            }
            else if (e.PropertyName?.Contains("SchoolHours") == true || e.PropertyName?.Contains("Day") == true)
            {
                _loggingService.LogInfo($"School hours setting changed: {e.PropertyName}");
                
                // Invalidate cache and re-check auto-start when school hours change
                _ = Task.Run(async () =>
                {
                    try
                    {
                        SchoolTimeChecker.InvalidateSchoolHoursCache();
                        
                        // Clear manual stop flag when school hours change - this allows auto-start to work again
                        await SchoolTimeChecker.ClearManualStopAsync();
                        _loggingService.LogInfo("Manual stop flag cleared due to school hours change");
                        
                        // Perform immediate auto-start check
                        await PerformAutoStartCheckAsync();
                        
                        // Reschedule timer with new school hours
                        await RescheduleAutoStartTimer();
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Error handling school hours change: {ex.Message}");
                    }
                });
            }
        }

        private void OnAutomationStatusChanged(object? sender, AutomationStatusChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                StatusMessage = e.Status;
                OnPropertyChanged(nameof(IsAutomationRunning));
                ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                
                // Track automation stop when it completes naturally (not just manual stop)
                if (!e.IsRunning)
                {
                    try
                    {
                        await _analyticsService.TrackAutomationStopAsync();
                        Debug.WriteLine("[Analytics] Automation stopped - analytics updated");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Analytics] Failed to track automation stop: {ex.Message}");
                    }
                }
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
                    _loggingService.LogDebug("[DASHBOARD] Today's count updated from cache");
                    
                    Dashboard.UpdateNextClassFromCache();
                    _loggingService.LogDebug("[DASHBOARD] Next class updated from cache");
                    
                    // 2. Full data refresh after short delay (fetches fresh data from server)
                    await Task.Delay(500); // Give server time to process registration
                    
                    try
                    {
                        _loggingService.LogDebug("[DASHBOARD] Refreshing all data from server...");
                        await Dashboard.RefreshDataAsync();
                        _loggingService.LogSuccess("[DASHBOARD] All data refreshed (today, weekly, monthly, overtime)");
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

        private async void OnConfirmationLost(object? sender, EventArgs e)
        {
            _loggingService.LogWarning("🚨 CONFIRMATION LOST EVENT RECEIVED - stopping automation and showing overlay immediately");

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Clear any blocking notifications first
                await _notificationService.ClearAllNotificationsAsync();
                _loggingService.LogInfo("Cleared all notifications to ensure overlay can show");

                // Update confirmation status first
                await UpdateConfirmationStatusAsync();

                // Stop automation if running (but don't mark as manual stop)
                if (IsAutomationRunning)
                {
                    await StopAutomationAsync(markAsManual: false);
                    _loggingService.LogInfo("Automation stopped due to lost confirmation");
                }

                // Reset the initial notification flag so it shows again
                _hasShownInitialConfirmationNotification = false;
                _loggingService.LogInfo("Reset notification flag - will show notification again");

                // Force the overlay to show immediately
                ShouldShowConfirmationOverlay = true;
                _loggingService.LogInfo("Overlay forced to show due to lost confirmation");

                // Also call RequestOverlayShowAsync to trigger notification
                await RequestOverlayShowAsync();
                _loggingService.LogInfo("Overlay requested due to lost confirmation");

                // Start aggressive checking to ensure overlay stays visible
                StartAggressiveOverlayChecking();
                _loggingService.LogInfo("Started aggressive overlay checking");
            });
        }


        /// <summary>
        /// Checks if the overlay should be shown based on current conditions
        /// </summary>
        private async Task CheckIfOverlayShouldShowAsync()
                {
                    try
                    {
                        // Simple check: Does the user need to confirm for today?
                        var today = DateTime.Now.Date;
                        var isConfirmed = await _userConfirmationService.IsConfirmedForDateAsync(today);

                        if (!isConfirmed)
                        {
                            _loggingService.LogInfo("User needs confirmation - showing overlay");
                            await RequestOverlayShowAsync();

                            // Keep aggressive checking since confirmation is needed
                            StartAggressiveOverlayChecking();
                        }
                        else
                        {
                            _loggingService.LogDebug("User is confirmed - no overlay needed");

                            // Stop aggressive checking since user is confirmed
                            StopAggressiveOverlayChecking();
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Error checking if overlay should show: {ex.Message}");
                    }
                }
        /// <summary>
        /// Forces a check of whether the overlay should be shown (useful for testing/debugging)
        /// </summary>
        public async Task ForceCheckOverlayStatusAsync()
        {
            _loggingService.LogInfo("Force checking overlay status...");
            
            var isConfirmationNeeded = await IsConfirmationNeededAsync();
            _loggingService.LogDebug($"Force check - IsConfirmationNeeded: {isConfirmationNeeded}, ShouldShowConfirmationOverlay: {ShouldShowConfirmationOverlay}");
            
            if (isConfirmationNeeded && !ShouldShowConfirmationOverlay)
            {
                _loggingService.LogInfo("🚨 FORCE CLEARING ALL NOTIFICATIONS AND SHOWING OVERLAY");
                
                // Force clear ALL notifications
                await _notificationService.ClearAllNotificationsAsync();
                await Task.Delay(200); // Give more time for UI to update
                
                // Force show overlay
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ShouldShowConfirmationOverlay = true;
                });
                
                _loggingService.LogInfo("🚨 FORCE OVERLAY COMPLETED");
            }
            
            await CheckIfOverlayShouldShowAsync();
        }
        // Timer for frequent overlay checks when confirmation is needed
        private Timer? _overlayCheckTimer;

        /// <summary>
        /// Starts aggressive overlay checking when confirmation is needed
        /// </summary>
        private void StartAggressiveOverlayChecking()
                {
                    if (_overlayCheckTimer != null)
                        return;

                    _loggingService.LogDebug("Starting aggressive overlay checking (every 2 seconds) - user needs to confirm");
                    
                    int checkCount = 0; // Counter for force clearing

                    _overlayCheckTimer = new Timer(
                        async _ =>
                        {
                            try
                            {
                                checkCount++;
                                
                                // Check if user has confirmed for today
                                var today = DateTime.Now.Date;
                                var isConfirmed = await _userConfirmationService.IsConfirmedForDateAsync(today);

                                if (!isConfirmed)
                                {
                                    _loggingService.LogDebug("Aggressive check: User not confirmed");

                                    if (!ShouldShowConfirmationOverlay)
                                    {
                                        // User hasn't confirmed and overlay is not showing - show it
                                        _loggingService.LogWarning("🚨 User confirmation missing - showing overlay (detected by aggressive checking)");

                                        // Stop automation if running
                                        if (IsAutomationRunning)
                                        {
                                            _loggingService.LogWarning("Stopping automation - user confirmation required");
                                            await Dispatcher.UIThread.InvokeAsync(async () =>
                                            {
                                                await StopAutomationAsync(markAsManual: false);
                                            });
                                        }

                                        // Reset notification flag so it shows again
                                        _hasShownInitialConfirmationNotification = false;

                                        // Every 5th attempt, force clear all notifications
                                        if (checkCount % 5 == 0)
                                        {
                                            _loggingService.LogWarning("🚨 FORCE CLEARING - 5th attempt to show overlay");
                                            await ForceCheckOverlayStatusAsync();
                                        }
                                        else
                                        {
                                            // Show overlay immediately
                                            await RequestOverlayShowAsync();
                                        }
                                        
                                        _loggingService.LogInfo("Confirmation overlay shown - user must confirm presence");
                                    }
                                }
                                else
                                {
                                    _loggingService.LogDebug("Aggressive check: User is confirmed");

                                    // User is confirmed but overlay is showing - hide it
                                    if (ShouldShowConfirmationOverlay)
                                    {
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            ShouldShowConfirmationOverlay = false;
                                            _loggingService.LogInfo("User confirmed - hiding overlay");
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _loggingService.LogError($"Error in aggressive overlay check: {ex.Message}");
                            }
                        },
                        null,
                        TimeSpan.FromSeconds(2), // First check after 2 seconds
                        TimeSpan.FromSeconds(2)  // Then every 2 seconds
                    );
                }

        /// <summary>
        /// Stops aggressive overlay checking
        /// </summary>
        private void StopAggressiveOverlayChecking()
        {
            if (_overlayCheckTimer != null)
            {
                _loggingService.LogDebug("Stopping aggressive overlay checking");
                _overlayCheckTimer.Dispose();
                _overlayCheckTimer = null;
            }
        }
        #endregion

        #region Command Implementations
        private async Task StartAutomationAsync()
        {
            _loggingService.LogDebug($"[START] Checking authentication state: IsAuthenticated={IsAuthenticated}, _userParameters null={_userParameters == null}, complete={_userParameters?.IsComplete ?? false}");
            
            // Clear manual stop flag when user manually starts automation
            await SchoolTimeChecker.ClearManualStopAsync();
            _loggingService.LogInfo("Manual stop flag cleared - user manually started automation");
            
            if (!IsAuthenticated || _userParameters == null || !_userParameters.IsComplete)
            {
                _loggingService.LogError($"[START] Ikke autentisert - kan ikke starte automatisering. IsAuth={IsAuthenticated}, Params={_userParameters != null}, Complete={_userParameters?.IsComplete ?? false}");
                
                // Track authentication error on start
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "automation_start_not_authenticated",
                        $"Cannot start automation - not authenticated. IsAuth={IsAuthenticated}, ParamsComplete={_userParameters?.IsComplete ?? false}"
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log auth error: {analyticsEx.Message}");
                }
                
                await _notificationService.ShowNotificationAsync(
                    "Autentiseringsfeil",
                    "Du må være innlogget for å starte automatisering",
                    NotificationLevel.Error
                );
                return;
            }

            if (Dashboard.IsCacheStale())
            {
                _loggingService.LogInfo("Cache is stale - refreshing dashboard before automation...");
                StatusMessage = "Oppdaterer data";

                try
                {
                    await Dashboard.RefreshDataAsync();
                    _lastDataRefresh = DateTime.Now;
                    _loggingService.LogSuccess("Dashboard data refreshed");
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Kunne ikke oppdatere dashboard: {ex.Message}");
                    
                    // Track dashboard refresh failure
                    try
                    {
                        await _analyticsService.LogErrorAsync(
                            "dashboard_refresh_failed_before_automation",
                            ex.Message,
                            ex
                        );
                    }
                    catch (Exception analyticsEx)
                    {
                        Debug.WriteLine($"[Analytics] Failed to log dashboard error: {analyticsEx.Message}");
                    }
                    
                    await _notificationService.ShowNotificationAsync(
                        "Advarsel",
                        "Kunne ikke oppdatere dashboard data. Starter automatisering likevel...",
                        NotificationLevel.Warning
                    );
                }
            }
            else
            {
                _loggingService.LogInfo("Using cached data (from today - still fresh)");
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
                _loggingService.LogDebug("[MAIN] Loading fresh credentials for automation...");
                
                var cookies = await SecureCredentialStorage.LoadCookiesAsync();
                _loggingService.LogDebug($"[MAIN] Loaded {cookies?.Count ?? 0} cookies from storage");
                
                // Also reload user parameters in case they were updated by AuthenticationService
                Services.UserParameters? freshUserParams = null;
                
                try
                {
                    var appSupportDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AkademiTrack"
                    );
                    var filePath = Path.Combine(appSupportDir, "user_parameters.json");
                    
                    if (File.Exists(filePath))
                    {
                        var json = await File.ReadAllTextAsync(filePath);
                        freshUserParams = JsonSerializer.Deserialize<Services.UserParameters>(json);
                        _loggingService.LogDebug($"[MAIN] Reloaded fresh user parameters from file: FylkeId={freshUserParams?.FylkeId}, SkoleId={freshUserParams?.SkoleId}");
                    }
                    else
                    {
                        // Fallback: try keychain (old method)
                        var userParamsJson = await SecureCredentialStorage.GetCredentialAsync("user_parameters");
                        if (!string.IsNullOrEmpty(userParamsJson))
                        {
                            freshUserParams = JsonSerializer.Deserialize<Services.UserParameters>(userParamsJson);
                            _loggingService.LogDebug($"[MAIN] Reloaded fresh user parameters from keychain: FylkeId={freshUserParams?.FylkeId}, SkoleId={freshUserParams?.SkoleId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"[MAIN] Failed to load user parameters: {ex.Message}");
                }
                
                if (freshUserParams == null)
                {
                    _loggingService.LogDebug("[MAIN] No user parameters found in storage, using cached ones");
                }
                
                // Use fresh parameters if available, otherwise fall back to cached ones
                var paramsToUse = freshUserParams ?? _userParameters;
                
                if (cookies != null && paramsToUse != null)
                {
                    automationService.SetCredentials(paramsToUse, cookies);
                    
                    // Also update Dashboard with fresh credentials
                    Dashboard.SetCredentials(paramsToUse, cookies);
                    
                    // Initialize Calendar if not already initialized
                    if (Calendar == null)
                    {
                        var attendanceService = new AttendanceDataService();
                        attendanceService.SetCredentials(paramsToUse, cookies);
                        Calendar = new CalendarViewModel(attendanceService, _loggingService);
                        OnPropertyChanged(nameof(Calendar));
                        _loggingService.LogInfo("[MAIN] Calendar ViewModel initialized from cached credentials");
                    }
                    
                    // Credentials set successfully - no need for verbose logging
                }
                else
                {
                    _loggingService.LogError($"[MAIN] Missing credentials for automation: cookies={cookies?.Count ?? 0}, params={paramsToUse != null}");
                }
            }

            StatusMessage = "Starter automatisering";
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

                // Automation failed - log but don't show notification to user
                _loggingService.LogError($"Automation start failed: {result.Message ?? "Network or authentication error"}");

                Debug.WriteLine("[MainWindow] Automation start failed");
            }

            // Update UI
            OnPropertyChanged(nameof(IsAutomationRunning));
            ((AsyncRelayCommand)StartAutomationCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopAutomationCommand).RaiseCanExecuteChanged();
        }

        private async Task StopAutomationAsync()
        {
            await StopAutomationAsync(markAsManual: true);
        }

        private async Task StopAutomationAsync(bool markAsManual)
        {
            if (markAsManual)
            {
                // Mark manual stop FIRST
                await SchoolTimeChecker.MarkManualStopAsync();
            }
            
            // Note: TrackAutomationStopAsync is now called automatically by OnAutomationStatusChanged event
            Debug.WriteLine("[MainWindow] Automation stopped");

            var result = await _automationService.StopAsync();
            
            if (!result.Success)
            {
                // Track stop failure
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "automation_stop_failed",
                        result.Message ?? "Failed to stop automation"
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log stop error: {analyticsEx.Message}");
                }
                
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

        private async Task ConfirmPresenceAsync()
        {
            try
            {
                _loggingService.LogInfo("User manually confirmed presence");

                var confirmed = await _userConfirmationService.ConfirmPresenceAsync();

                if (confirmed)
                {
                    // Hide the confirmation overlay
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ShouldShowConfirmationOverlay = false;
                    });

                    // Stop aggressive overlay checking since confirmation is done
                    StopAggressiveOverlayChecking();
                    
                    // Reset the initial notification flag for next time
                    _hasShownInitialConfirmationNotification = false;

                    // Update confirmation status
                    await UpdateConfirmationStatusAsync();

                    // Check if automation should start now that presence is confirmed
                    var (shouldStart, reason, _, _, _) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: false);

                    if (shouldStart && !IsAutomationRunning)
                    {
                        _loggingService.LogInfo("Starting automation after manual confirmation");

                        // Mark as started
                        await SchoolTimeChecker.MarkTodayAsStartedAsync();

                        // Start automation
                        await StartAutomationAsync();
                    }
                    else if (IsAutomationRunning)
                    {
                        // Automation already running - no need for notification
                        _loggingService.LogInfo("Presence confirmed - automation already running");
                    }
                    else
                    {
                        // Presence confirmed but automation not running - log only
                        _loggingService.LogSuccess($"Presence confirmed for today. {reason}");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error confirming presence: {ex.Message}");

                await _notificationService.ShowNotificationAsync(
                    "Bekreftelse feilet",
                    "Kunne ikke bekrefte tilstedeværelse. Prøv igjen.",
                    NotificationLevel.Error
                );
            }
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

            // DON'T cancel ongoing authentication - let it continue in background
            // Only the CredentialsSaved event should trigger a new authentication attempt

            // Connect SettingsViewModel to logging service
            SettingsViewModel.ConnectToMainViewModel(this);

            ShowDashboard = false;
            ShowTutorial = false;
            ShowFeide = false;
            ShowSettings = true;

            // Subscribe to close event
            SettingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            SettingsViewModel.CloseRequested += OnSettingsCloseRequested;

            // Subscribe to school hours changes
            SettingsViewModel.SchoolHoursChanged -= OnSchoolHoursChanged;
            SettingsViewModel.SchoolHoursChanged += OnSchoolHoursChanged;

            // Subscribe to credentials saved event
            SettingsViewModel.CredentialsSaved -= OnCredentialsSaved;
            SettingsViewModel.CredentialsSaved += OnCredentialsSaved;

            return Task.CompletedTask;
        }

        private void OnSchoolHoursChanged(object? sender, EventArgs e)
        {
            _loggingService.LogInfo("🕐 School hours changed - clearing manual stop flag and re-checking auto-start");
            
            // Clear manual stop flag when school hours change - this allows auto-start to work again
            _ = Task.Run(async () =>
            {
                try
                {
                    await SchoolTimeChecker.ClearManualStopAsync();
                    _loggingService.LogInfo("Manual stop flag cleared due to school hours change");
                    
                    // Perform immediate auto-start check
                    await PerformAutoStartCheckAsync();
                    
                    // Reschedule timer with new school hours
                    await RescheduleAutoStartTimer();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Error re-checking auto-start after school hours change: {ex.Message}");
                }
            });
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
            // Prevent concurrent refreshes with timeout
            if (!await _refreshSemaphore.WaitAsync(100))
            {
                _loggingService.LogDebug("Data refresh already in progress, skipping duplicate request");
                return;
            }

            try
            {
                // Double-check with volatile flag
                if (_isRefreshingData)
                {
                    _loggingService.LogDebug("Double-check: refresh already in progress");
                    return;
                }

                // Check cooldown period
                var timeSinceLastRefresh = DateTime.Now - _lastManualRefresh;
                if (timeSinceLastRefresh.TotalSeconds < REFRESH_COOLDOWN_SECONDS)
                {
                    var remainingCooldown = REFRESH_COOLDOWN_SECONDS - (int)timeSinceLastRefresh.TotalSeconds;
                    _loggingService.LogInfo($"Refresh cooldown active - wait {remainingCooldown} more seconds");
                    StatusMessage = $"Vent {remainingCooldown}s før ny oppdatering";
                    return;
                }

                _isRefreshingData = true;
                _lastManualRefresh = DateTime.Now;

                _loggingService.LogInfo("Oppdaterer data");
                StatusMessage = "Oppdaterer";

                await Dashboard.RefreshDataAsync();
                
                _loggingService.LogSuccess("Data oppdatert!");
                StatusMessage = "Data oppdatert!";
                
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
                
                // Track manual refresh failure
                try
                {
                    await _analyticsService.LogErrorAsync(
                        "manual_data_refresh_failed",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log refresh error: {analyticsEx.Message}");
                }
                
                await _notificationService.ShowNotificationAsync(
                    "Oppdateringsfeil",
                    "Kunne ikke oppdatere data. Prøv igjen senere.",
                    NotificationLevel.Warning
                );
            }
            finally
            {
                _isRefreshingData = false;
                _refreshSemaphore.Release();
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

        private Task ToggleClassViewAsync()
        {
            Dashboard.ToggleClassView();
            return Task.CompletedTask;
        }

        private Task SelectTabAsync(string? tabName)
        {
            if (!string.IsNullOrEmpty(tabName))
            {
                SelectedTab = tabName;
                _loggingService.LogInfo($"Switched to {tabName} tab");
            }
            return Task.CompletedTask;
        }

        private async Task CheckMidnightResetAsync()
        {
            var now = DateTime.Now.Date;

            if (now > _currentDay)
            {
                _loggingService.LogInfo($"NEW DAY DETECTED! Changing from {_currentDay:yyyy-MM-dd} to {now:yyyy-MM-dd}");
                _currentDay = now;

                SchoolTimeChecker.ResetDailyCompletion();
                _loggingService.LogInfo("Daily completion flags reset");

                Dashboard.ClearCache();
                _loggingService.LogInfo("Dashboard cache cleared for new day");

                // Clear daily confirmation for new day
                await _userConfirmationService.ClearConfirmationAsync();
                await UpdateConfirmationStatusAsync();
                _loggingService.LogInfo("Daily confirmation cleared for new day");
                
                // Reset initial notification flag for new day
                _hasShownInitialConfirmationNotification = false;

                if (_settingsService.AutoStartAutomation)
                {
                    _loggingService.LogInfo("Checking if automation should auto-start for new day...");
                    await CheckAutoStartAutomationAsync();

                    // Also check if overlay should show after new day reset
                    await CheckIfOverlayShouldShowAsync();
                }
            }
        }

        private async Task UpdateConfirmationStatusAsync()
                {
                    try
                    {
                        var today = DateTime.Now.Date;
                        var isConfirmed = await _userConfirmationService.IsConfirmedForDateAsync(today);
                        var needsConfirmation = !isConfirmed;

                        _loggingService.LogDebug($"Confirmation status - Is confirmed: {isConfirmed}, Needs confirmation: {needsConfirmation}");

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            IsConfirmationNeeded = needsConfirmation;

                            if (!needsConfirmation)
                            {
                                // User is confirmed - hide overlay and stop checking
                                ShouldShowConfirmationOverlay = false;
                                StopAggressiveOverlayChecking();
                                _loggingService.LogDebug("User is confirmed - overlay hidden");
                            }
                            else
                            {
                                // User needs to confirm - start checking and show overlay immediately
                                _loggingService.LogInfo("User needs confirmation - showing overlay and starting checks");
                                StartAggressiveOverlayChecking();

                                // Show overlay immediately since user needs to confirm
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(500); // Brief delay to let initialization complete
                                    await RequestOverlayShowAsync();
                                });
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError($"Confirmation status error: {ex.Message}");
                    }
                }

        #endregion

        #region Event Handlers
        private async void OnSettingsCloseRequested(object? sender, EventArgs e)
        {
            // Only go back to dashboard - don't retry authentication
            // Authentication retry is handled by OnCredentialsSaved when user clicks "Lagre"
            _ = BackToDashboardAsync();
        }

        private async void OnCredentialsSaved(object? sender, EventArgs e)
        {
            _loggingService.LogInfo("Innloggingsdata lagret - starter re-autentisering");
            
            // Delete cached cookies to force fresh login
            try
            {
                await SecureCredentialStorage.DeleteCookiesAsync();
                _loggingService.LogInfo("Cached cookies deleted - will perform fresh login");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to delete cookies: {ex.Message}");
            }
            
            // Reset authentication state
            _userCancelledAuth = false;
            InitializationRetryCount = 0;
            IsAuthenticated = false;
            
            // Close settings and show dashboard so loading overlay is visible
            ShowSettings = false;
            ShowDashboard = true;
            
            // Small delay to ensure UI updates
            await Task.Delay(200);
            
            // Trigger re-authentication
            _ = InitializeAsync();
        }

        private void OnFeideSetupCompleted(object? sender, FeideSetupCompletedEventArgs e)
        {
            if (e.Success)
            {
                _loggingService.LogSuccess($"Feide-oppsett fullført for {e.UserEmail}");

                // Mark that an interactive Feide login just happened
                // This will be used when the user presses the confirmation button
                _userConfirmationService.MarkInteractiveFeideLoginCompleted();

                // Reset retry count
                InitializationRetryCount = 0;
                // Note: Navigation and StartInitializationAsync() is handled by App.axaml.cs
            }
            else
            {
                _loggingService.LogError("Feide-oppsett mislyktes");
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
                "Starter automatisering",
                "Automatisering stoppet",
                "Automatisering fullført",
                "Innlogging fullført",
                "Autentisering og parametere fullført",
                "Registreringsvindu er åpent",
                "Forsøker å registrere oppmøte",
                "Alle STU-økter er håndtert",
                "Sjekker registreringsvinduer"
            };

            return importantMessages.Any(important => message.StartsWith(important));
        }

        private async Task CheckAutoStartAutomationAsync()
        {
            try
            {
                _loggingService.LogInfo("CHECKING AUTO-START AUTOMATION");

                await _settingsService.LoadSettingsAsync();
                var autoStartEnabled = _settingsService.AutoStartAutomation;

                _loggingService.LogInfo($"AutoStartAutomation setting: {autoStartEnabled}");
                _loggingService.LogInfo($"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _loggingService.LogInfo($"Current day: {DateTime.Now.DayOfWeek}");

                if (!autoStartEnabled)
                {
                    _loggingService.LogInfo("Auto-start is DISABLED in settings");
                    _loggingService.LogInfo("   Periodic checking will NOT run");

                    // Stop timer if it's running - must be on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _autoStartCheckTimer?.Dispose();
                        _autoStartCheckTimer = null;
                    });
                    return;
                }

                // If auto-start is enabled and app is restarting, clear manual stop flag
                // This handles the case where app crashed while user had manually started automation
                await SchoolTimeChecker.ClearManualStopAsync();
                _loggingService.LogInfo("Manual stop flag cleared on app restart (auto-start enabled)");

                // Use the confirmation-aware method that checks manual stops, completion, and confirmation
                var (shouldStart, reason, nextStartTime, shouldNotify, needsConfirmation) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: false);

                _loggingService.LogInfo($"Auto-start check result: {reason}");

                if (needsConfirmation)
                {
                    _loggingService.LogInfo("Daily confirmation required - but NOT showing overlay during initial startup");

                    // DON'T show overlay during initial startup - only during periodic checks
                    // The periodic timer will handle showing the overlay when it's actually time
                }
                else if (shouldStart && !IsAutomationRunning)
                {
                    _loggingService.LogInfo("Starting automation automatically...");

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
                    _loggingService.LogInfo("Automation is already running");
                }
                else
                {
                    _loggingService.LogInfo($"Not auto-starting: {reason}");
                    if (nextStartTime.HasValue)
                    {
                        _loggingService.LogInfo($"   Next auto-start: {nextStartTime.Value:yyyy-MM-dd HH:mm}");
                    }
                }

                // SMART PERIODIC CHECKING - checks frequently near start times, less frequently otherwise
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_autoStartCheckTimer == null)
                    {
                        _loggingService.LogInfo("Starting smart auto-start checker (frequent checks near start times)");
                        _autoStartCheckTimer = new Timer(
                            async _ => 
                            {
                                try
                                {
                                    // Check for wake from sleep first
                                    if (SchoolTimeChecker.DetectWakeFromSleep())
                                    {
                                        _loggingService.LogInfo("Wake from sleep detected - checking auto-start conditions");
                                    }

                                    // Perform the auto-start check (this one CAN show overlay)
                                    await PerformAutoStartCheckAsync();

                                    // Reschedule next check based on how close we are to start time
                                    await RescheduleAutoStartTimer();
                                }
                                catch (Exception ex)
                                {
                                    _loggingService.LogError($"Error in auto-start timer: {ex.Message}");

                                    // Fallback rescheduling on error
                                    try
                                    {
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            _autoStartCheckTimer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                                        });
                                    }
                                    catch (Exception rescheduleEx)
                                    {
                                        _loggingService.LogError($"Error rescheduling timer after failure: {rescheduleEx.Message}");
                                    }
                                }
                            },
                            null,
                            TimeSpan.FromSeconds(5), // First check after 5 seconds
                            Timeout.InfiniteTimeSpan  // We'll reschedule manually for smart timing
                        );
                    }
                    else
                    {
                        _loggingService.LogDebug("Smart auto-start checker already running");
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error checking auto-start: {ex.Message}");
                Debug.WriteLine($"[AUTO-START] Error: {ex}");
            }
        }


        private async Task PerformAutoStartCheckAsync()
        {
            try
            {
                await _settingsService.LoadSettingsAsync();
                var autoStartEnabled = _settingsService.AutoStartAutomation;

                if (!autoStartEnabled)
                {
                    _loggingService.LogDebug("Auto-start disabled in settings");
                    return;
                }

                // Use the confirmation-aware method that checks manual stops, completion, and confirmation
                var (shouldStart, reason, nextStartTime, shouldNotify, needsConfirmation) = await SchoolTimeChecker.ShouldAutoStartAutomationWithConfirmationAsync(silent: true);

                if (needsConfirmation)
                {
                    // Check if it's actually time to start automation (ignoring confirmation requirement)
                    var (wouldStartWithoutConfirmation, _, _, _) = await SchoolTimeChecker.ShouldAutoStartAutomationAsync(silent: true);

                    if (wouldStartWithoutConfirmation)
                    {
                        _loggingService.LogDebug("It's time to start automation but confirmation needed - checking if overlay can be shown");

                        // STRICT CHECK: Only request overlay if NO notifications are active
                        var hasActiveNotifications = _notificationService.HasActiveNotifications;
                        if (hasActiveNotifications)
                        {
                            _loggingService.LogInfo("CANNOT show overlay - active notifications present. User must handle notifications first.");
                            return; // Don't show overlay, don't set pending - just wait for next check
                        }

                        // Safe to show overlay
                        await RequestOverlayShowAsync();
                    }
                    else
                    {
                        _loggingService.LogDebug($"Confirmation needed but not time to start yet: {reason}");
                    }

                    return;
                }
                else if (shouldStart && !IsAutomationRunning)
                {
                    _loggingService.LogInfo($"Auto-starting automation: {reason}");

                    // Mark as started
                    await SchoolTimeChecker.MarkTodayAsStartedAsync();

                    // Start automation on UI thread
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await StartAutomationAsync();
                    });
                }
                else if (shouldStart && IsAutomationRunning)
                {
                    _loggingService.LogDebug("Automation already running");
                }
                else
                {
                    _loggingService.LogDebug($"Not auto-starting: {reason}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error in auto-start check: {ex.Message}");
            }
        }

        private async Task RescheduleAutoStartTimer()
        {
            try
            {
                if (_autoStartCheckTimer == null) return;

                // Get the next start time to determine optimal check frequency
                var (_, _, nextStartTime, _) = await SchoolTimeChecker.ShouldAutoStartAutomationAsync(silent: true);
                
                TimeSpan nextCheckInterval;
                
                if (nextStartTime.HasValue)
                {
                    var timeUntilStart = nextStartTime.Value - DateTime.Now;
                    
                    if (timeUntilStart.TotalMinutes <= 2)
                    {
                        // Very close to start time - check every 5 seconds for precision
                        nextCheckInterval = TimeSpan.FromSeconds(5);
                        _loggingService.LogDebug($"Next check in 5s (start time in {timeUntilStart.TotalMinutes:F1} min)");
                    }
                    else if (timeUntilStart.TotalMinutes <= 10)
                    {
                        nextCheckInterval = TimeSpan.FromSeconds(15);
                        _loggingService.LogDebug($"Next check in 15s (start time in {timeUntilStart.TotalMinutes:F0} min)");
                    }
                    else if (timeUntilStart.TotalHours <= 1)
                    {
                        nextCheckInterval = TimeSpan.FromMinutes(1);
                        _loggingService.LogDebug($"Next check in 1m (start time in {timeUntilStart.TotalHours:F1} hours)");
                    }
                    else
                    {
                        nextCheckInterval = TimeSpan.FromMinutes(5);
                        _loggingService.LogDebug($"Next check in 5m (start time in {timeUntilStart.TotalHours:F1} hours)");
                    }
                }
                else
                {
                    nextCheckInterval = TimeSpan.FromSeconds(30);
                    _loggingService.LogDebug("Next check in 30s (no scheduled start time)");
                }

                // Reschedule the timer on UI thread to avoid threading issues
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        _autoStartCheckTimer?.Change(nextCheckInterval, Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Timer was disposed, ignore
                        _loggingService.LogDebug("Timer was disposed during rescheduling");
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error rescheduling auto-start timer: {ex.Message}");
                
                // Fallback to 30-second interval
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _autoStartCheckTimer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                    });
                }
                catch (Exception fallbackEx)
                {
                    _loggingService.LogError($"Error in fallback timer rescheduling: {fallbackEx.Message}");
                }
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
                    _loggingService.LogInfo("Window activated - cache is stale, refreshing data...");
                    await Dashboard.RefreshDataAsync();
                }
                else
                {
                    Dashboard.UpdateNextClassFromCache();
                }
                
                // Always refresh widget heartbeat when window is activated
                // This helps recover from any widget communication issues
                try
                {
                    var widgetDataService = Services.DependencyInjection.ServiceContainer.GetService<WidgetDataService>();
                    if (widgetDataService != null)
                    {
                        await widgetDataService.ForceRefreshWidgetAsync();
                        _loggingService.LogDebug("Widget force refreshed on window activation");
                    }
                }
                catch (Exception widgetEx)
                {
                    _loggingService.LogWarning($"Failed to refresh widget on window activation: {widgetEx.Message}");
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

    // Generic version for commands with parameters
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Func<T?, bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke((T?)parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                await _execute((T?)parameter);
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

                _overlayCheckTimer?.Dispose();
                _overlayCheckTimer = null;

                // Dispose semaphores
                _refreshSemaphore?.Dispose();
                _initializationSemaphore?.Dispose();

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
