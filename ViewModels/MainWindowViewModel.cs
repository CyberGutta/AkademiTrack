using AkademiTrack.Services;
using AkademiTrack.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;          
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json; 
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Security;
using OpenQA.Selenium.DevTools.V140.DOM;


namespace AkademiTrack.ViewModels
{
    public class NotificationOverlayWindow : Window
    {
        private Timer? _autoCloseTimer;
        private readonly string _level;

        public ThemeManager ThemeManager => Services.ThemeManager.Instance;
        public NotificationOverlayWindow(string title, string message, string level = "INFO", string imageUrl = null!, string customColor = null!)
        {
            _level = level;

            this.WindowState = WindowState.Normal;
            this.CanResize = false;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.SystemDecorations = SystemDecorations.None;
            this.Width = 400;
            this.Height = 120;

            PositionWindow();
            CreateModernContent(title, message, level, imageUrl, customColor);

            int autoCloseSeconds = title.Contains("Admin") || title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN[") ? 12 : 6;
            _autoCloseTimer = new Timer(AutoClose, null, autoCloseSeconds * 1000, Timeout.Infinite);
        }

        private void CreateModernContent(string title, string message, string level, string imageUrl = null!, string customColor = null!)
        {
            string displayTitle = title;
            bool isAdminNotification = false;

            if (title.StartsWith("[ADMIN]"))
            {
                displayTitle = title.Substring(7);
                isAdminNotification = true;
            }
            else if (title.StartsWith("[ADMIN["))
            {
                var endIndex = title.IndexOf(']', 7);
                if (endIndex > 0)
                {
                    displayTitle = title.Substring(7, endIndex - 7);
                }
                else
                {
                    displayTitle = title.Substring(7);
                }
                isAdminNotification = true;
            }

            IBrush backgroundColor, borderColor, textColor, accentColor;
            SetModernColors(level, isAdminNotification, customColor, out backgroundColor, out borderColor, out textColor, out accentColor);

            this.Background = Brushes.Transparent;

            var mainBorder = new Border
            {
                Background = backgroundColor,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
                BoxShadow = BoxShadows.Parse("0 4 20 0 #00000020, 0 1 3 0 #00000030")
            };

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4, GridUnitType.Pixel)));
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var accentStrip = new Border
            {
                Background = accentColor,
                CornerRadius = new CornerRadius(10, 0, 0, 10)
            };
            Grid.SetColumn(accentStrip, 0);
            outerGrid.Children.Add(accentStrip);

            var contentArea = new Border
            {
                Padding = new Thickness(20, 16)
            };
            Grid.SetColumn(contentArea, 1);

            var layoutGrid = new Grid();
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            layoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            layoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var notificationIcon = CreateModernIcon(level, isAdminNotification, accentColor, textColor, imageUrl);
            if (notificationIcon != null)
            {
                Grid.SetColumn(notificationIcon, 0);
                Grid.SetRowSpan(notificationIcon, 2);
                notificationIcon.Margin = new Thickness(0, 0, 12, 0);
                notificationIcon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                layoutGrid.Children.Add(notificationIcon);
            }

            var titlePanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6
            };

            if (isAdminNotification)
            {
                var adminBadge = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#FF3B30")),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                var badgeText = new TextBlock
                {
                    Text = "ADMIN",
                    FontSize = 8,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                adminBadge.Child = badgeText;
                titlePanel.Children.Add(adminBadge);
            }

            var titleBlock = new TextBlock
            {
                Text = displayTitle,
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                Foreground = textColor,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            titlePanel.Children.Add(titleBlock);
            Grid.SetColumn(titlePanel, 1);
            Grid.SetRow(titlePanel, 0);
            layoutGrid.Children.Add(titlePanel);

            var messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 13.5,
                Foreground = textColor,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
                LineHeight = 15.0,
                Margin = new Thickness(0, 4, 0, 0), 
                MaxWidth = 280
            };

            Grid.SetColumn(messageBlock, 1);
            Grid.SetRow(messageBlock, 1);
            layoutGrid.Children.Add(messageBlock);

            var closeButton = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                FontSize = 14,
                Width = 32,
                Height = 32,
                Foreground = textColor,
                CornerRadius = new CornerRadius(16),
                Opacity = 0.6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            closeButton.PointerEntered += (s, e) =>
            {
                closeButton.Opacity = 1.0;
                closeButton.Background = new SolidColorBrush(Colors.Black) { Opacity = 0.1 };
            };
            closeButton.PointerExited += (s, e) =>
            {
                closeButton.Opacity = 0.6;
                closeButton.Background = Brushes.Transparent;
            };
            closeButton.Click += (s, e) => Close();

            Grid.SetColumn(closeButton, 2);
            Grid.SetRow(closeButton, 0);
            layoutGrid.Children.Add(closeButton);

            contentArea.Child = layoutGrid;
            outerGrid.Children.Add(contentArea);
            mainBorder.Child = outerGrid;
            this.Content = mainBorder;
        }

        private void SetModernColors(string level, bool isAdmin, string customColor, out IBrush backgroundColor, out IBrush borderColor, out IBrush textColor, out IBrush accentColor)
        {
            if (!string.IsNullOrEmpty(customColor))
            {
                try
                {
                    var customColorParsed = Color.Parse(customColor);
                    backgroundColor = new SolidColorBrush(Colors.White);
                    borderColor = new SolidColorBrush(customColorParsed) { Opacity = 0.3 };
                    textColor = new SolidColorBrush(Color.Parse("#1A1A1A"));
                    accentColor = new SolidColorBrush(customColorParsed);
                    return;
                }
                catch { }
            }

            if (isAdmin)
            {
                backgroundColor = new SolidColorBrush(Color.Parse("#FAFAFA"));
                borderColor = new SolidColorBrush(Color.Parse("#FF3B30"));
                textColor = new SolidColorBrush(Color.Parse("#1A1A1A"));
                accentColor = new SolidColorBrush(Color.Parse("#FF3B30"));
            }
            else
            {
                backgroundColor = new SolidColorBrush(Colors.White);
                textColor = new SolidColorBrush(Color.Parse("#1A1A1A"));

                switch (level)
                {
                    case "SUCCESS":
                        borderColor = new SolidColorBrush(Color.Parse("#34C759"));
                        accentColor = new SolidColorBrush(Color.Parse("#34C759"));
                        break;
                    case "WARNING":
                        borderColor = new SolidColorBrush(Color.Parse("#FF9500"));
                        accentColor = new SolidColorBrush(Color.Parse("#FF9500"));
                        break;
                    case "ERROR":
                        borderColor = new SolidColorBrush(Color.Parse("#FF3B30"));
                        accentColor = new SolidColorBrush(Color.Parse("#FF3B30"));
                        break;
                    default:
                        borderColor = new SolidColorBrush(Color.Parse("#007AFF"));
                        accentColor = new SolidColorBrush(Color.Parse("#007AFF"));
                        break;
                }
            }
        }

        private Border CreateModernIcon(string level, bool isAdmin, IBrush accentColor, IBrush textColor, string imageUrl)
        {
            var iconBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Colors.LightGray) { Opacity = 0.15 },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            try
            {
                if (accentColor is SolidColorBrush solidBrush)
                {
                    iconBorder.Background = new SolidColorBrush(solidBrush.Color) { Opacity = 0.15 };
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    var image = new Avalonia.Controls.Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(imageUrl),
                        Width = 20,
                        Height = 20,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    iconBorder.Child = image;
                    return iconBorder;
                }
                catch { }
            }

            string iconText = level switch
            {
                "SUCCESS" => "✓",
                "WARNING" => "⚠",
                "ERROR" => "✕",
                _ when isAdmin => "★",
                _ => "ⓘ"
            };

            var iconTextBlock = new TextBlock
            {
                Text = iconText,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = accentColor,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            iconBorder.Child = iconTextBlock;
            return iconBorder;
        }

        private void PositionWindow()
        {
            try
            {
                var screen = this.Screens.Primary;
                if (screen != null)
                {
                    var workingArea = screen.WorkingArea;
                    var scaling = screen.Scaling;

                    var margin = (int)(20 * scaling);
                    var windowWidth = (int)(this.Width * scaling);
                    var windowHeight = (int)(this.Height * scaling);

                    var x = (int)(workingArea.Right - windowWidth - margin);
                    var y = (int)(workingArea.Y + margin);

                    if (x < workingArea.X) x = (int)(workingArea.X + margin);
                    if (y + windowHeight > workingArea.Bottom) y = (int)(workingArea.Bottom - windowHeight - margin);

                    this.Position = new PixelPoint(x, y);
                }
                else
                {
                    this.Position = new PixelPoint(100, 50);
                }
            }
            catch
            {
                this.Position = new PixelPoint(200, 50);
            }
        }

        private void AutoClose(object? state)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { Close(); }
                catch { Close(); }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer?.Dispose();
            base.OnClosed(e);
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string? Message { get; set; }
        public string? Level { get; set; }
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }

    public class NotificationEntry
    {
        public DateTime Timestamp { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Level { get; set; }
        public bool IsVisible { get; set; } = true;
        public int Id { get; set; }
    }

    public class SimpleCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null!)
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

    public partial class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isAutomationRunning;
        private string _statusMessage = "Ready";
        private ObservableCollection<LogEntry> _logEntries;
        private ObservableCollection<NotificationEntry> _notifications;
        private bool _showDetailedLogs = true;
        private NotificationEntry? _currentNotification;
        private Timer _updateCheckTimer;
        private Timer _schoolHoursCheckTimer;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private const string UPDATE_JSON_URL = "https://cybergutta.github.io/AkademietTrack/update.json";
        private readonly ApplicationInfo _applicationInfo;
        private bool _autoStartTriggered = false;
        private readonly object _autoStartLock = new object();

        private DateTime _lastAutoStartCheck = DateTime.MinValue;
        private readonly object _autoStartCheckLock = new object();
        private bool _hasPerformedInitialAutoStartCheck = false;

        private List<ScheduleItem>? _cachedScheduleData;
        private DateTime _scheduleDataFetchTime;

        private readonly List<NotificationOverlayWindow> _activeOverlayWindows = new();
        private readonly object _activeOverlayWindowsLock = new();

        private HashSet<string> _processedNotificationIds = new HashSet<string>();
        private string _processedNotificationsFile;

        private readonly Queue<NotificationQueueItem> _notificationQueue = new Queue<NotificationQueueItem>();
        private readonly object _notificationLock = new object();

        private readonly SemaphoreSlim _notificationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isProcessingQueue = false;
        private readonly object _queueLock = new object();

        public new event PropertyChangedEventHandler? PropertyChanged;

        public SettingsViewModel SettingsViewModel { get; set; }
        public DashboardViewModel Dashboard { get; private set; }

        private static DateTime _lastDailyLogTime = DateTime.MinValue;

        private Process? _caffeinateProcess;
        private readonly object _caffeinateLock = new object();

        // NEW: Authentication state
        private bool _isLoading = true;
        private bool _isAuthenticated = false;
        private AuthenticationService? _authService;

        // User parameters (now set during authentication)
        private UserParameters? _userParameters;

        public class NotificationQueueItem
        {
            public string? Title { get; set; }
            public string? Message { get; set; }
            public string? Level { get; set; }
            public string? ImageUrl { get; set; }
            public string? CustomColor { get; set; }
            public bool IsHighPriority { get; set; }
            public DateTime QueuedAt { get; set; } = DateTime.Now;
            public string UniqueId { get; set; } = Guid.NewGuid().ToString();
        }

        public class UpdateInfo
        {
            public string? latest_version { get; set; }
            public string? download_url { get; set; }
            public string? notes { get; set; }
            public string? published_at { get; set; }
            public string? timestamp { get; set; }
        }

        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {   
            _processedNotificationsFile = GetProcessedNotificationsFilePath();
            SettingsViewModel = new SettingsViewModel();
            Dashboard = new DashboardViewModel();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _logEntries = new ObservableCollection<LogEntry>();
            _notifications = new ObservableCollection<NotificationEntry>();
            _applicationInfo = new ApplicationInfo();
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync);
            BackToDashboardCommand = new SimpleCommand(BackToDashboardAsync);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            ClearLogsCommand = new SimpleCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new SimpleCommand(ToggleDetailedLogsAsync);
            DismissNotificationCommand = new SimpleCommand(DismissCurrentNotificationAsync);
            ToggleThemeCommand = new SimpleCommand(ToggleThemeAsync);
            OpenTutorialCommand = new SimpleCommand(OpenTutorialAsync);
            RefreshDataCommand = new SimpleCommand(RefreshDataAsync);


            // Start authentication flow instead of logging "ready"
            _ = InitializeAsync();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(2000);
                    await CheckAndRequestNotificationPermissionAsync();
                });
            }

            _updateCheckTimer = new Timer(
                async (state) => await CheckForUpdatesAutomatically(state),
                null,
                (int)TimeSpan.FromMinutes(1).TotalMilliseconds,
                (int)TimeSpan.FromMinutes(30).TotalMilliseconds
            );

            _schoolHoursCheckTimer = new Timer(
                CheckSchoolHoursAutoRestartSilent,  
                null,
                (int)TimeSpan.FromSeconds(5).TotalMilliseconds,  
                (int)TimeSpan.FromSeconds(10).TotalMilliseconds    
            );
        }

        // NEW: Loading state property
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            private set
            {
                if (_isAuthenticated != value)
                {
                    _isAuthenticated = value;
                    OnPropertyChanged();
                }
            }
        }

        // Tutorial navigation properties
        private bool _showDashboard = true;
        private bool _showSettings = false;
        private bool _showTutorial = false;

        public bool ShowDashboard
        {
            get => _showDashboard;
            set
            {
                if (_showDashboard != value)
                {
                    _showDashboard = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowSettings
        {
            get => _showSettings;
            set
            {
                if (_showSettings != value)
                {
                    _showSettings = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowTutorial
        {
            get => _showTutorial;
            set
            {
                if (_showTutorial != value)
                {
                    _showTutorial = value;
                    OnPropertyChanged();
                }
            }
        }


        // NEW: Initialize authentication on startup
        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;

                LogInfo("🚀 Starter AkademiTrack...");

                // Initialize authentication service
                _authService = new AuthenticationService();
                

                // Perform authentication
                var authResult = await _authService.AuthenticateAsync();

                if (authResult.Success && authResult.Cookies != null && authResult.Parameters != null)
                {
                    LogSuccess("✓ Autentisering fullført!");
                    
                    IsAuthenticated = true;
                    
                    // Store parameters for automation use
                    _userParameters = authResult.Parameters;
                    
                    // Initialize dashboard with credentials
                    var servicesUserParams = new Services.UserParameters
                    {
                        FylkeId = authResult.Parameters.FylkeId,
                        PlanPeri = authResult.Parameters.PlanPeri,
                        SkoleId = authResult.Parameters.SkoleId
                    };
                    
                    Dashboard.SetCredentials(servicesUserParams, authResult.Cookies);
                    
                    LogInfo("📊 Laster dashboard data...");
                    await Dashboard.RefreshDataAsync();
                    
                    LogSuccess("✓ Applikasjon er klar!");
                    
                    await Task.Delay(500); // Brief delay to show success message
                    
                    IsLoading = false;
                }
                else
                {
                    LogError("❌ Autentisering mislyktes - kan ikke starte applikasjon");
                    NativeNotificationService.Show(
                        "Autentisering mislyktes",
                        "Kunne ikke autentisere med iskole.net. Vennligst prøv igjen.",
                        "ERROR"
                    );
                    
                    // Stay on loading screen with error
                    await Task.Delay(3000);
                    
                    // Retry authentication
                    await InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                LogError($"Kritisk feil under oppstart: {ex.Message}");
                NativeNotificationService.Show(
                    "Oppstartsfeil",
                    "En kritisk feil oppstod under oppstart. Prøver igjen...",
                    "ERROR"
                );
                
                await Task.Delay(3000);
                await InitializeAsync();
            }
        }

        private async Task CheckAndRequestNotificationPermissionAsync()
        {
            try
            {
                var status = await Services.NotificationPermissionChecker.CheckMacNotificationPermissionAsync();

                LogDebug($"Notification permission status: {status}");

                if (status == Services.NotificationPermissionChecker.PermissionStatus.NotDetermined ||
                    status == Services.NotificationPermissionChecker.PermissionStatus.Denied)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            var dialog = new NotificationPermissionDialog();

                            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            {
                                var mainWindow = desktop.MainWindow;
                                if (mainWindow != null)
                                {
                                    await dialog.ShowDialog(mainWindow);

                                    if (dialog.UserGrantedPermission)
                                    {
                                        LogInfo("Bruker aktiverte varseltillatelser");
                                    }
                                    else
                                    {
                                        LogInfo("Bruker utsatte varseltillatelser");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to show notification permission dialog: {ex.Message}");
                        }
                    });
                }
                else if (status == Services.NotificationPermissionChecker.PermissionStatus.Authorized)
                {
                    LogDebug("Notification permissions already granted");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error checking notification permissions: {ex.Message}");
            }
        }

        private void CheckSchoolHoursAutoRestartSilent(object? state)
        {
            _ = CheckSchoolHoursAutoRestartSilentAsync();
        }

        private Task CheckSchoolHoursAutoRestartSilentAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (IsAutomationRunning)
                        return;

                    var settingsFile = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AkademiTrack",
                        "settings.json"
                    );

                    if (!System.IO.File.Exists(settingsFile))
                        return;

                    var json = await System.IO.File.ReadAllTextAsync(settingsFile);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

                    if (settings?.AutoStartAutomation != true)
                        return;

                    var result = await Services.SchoolTimeChecker.ShouldAutoStartAutomationAsync(silent: true);
                    
                    if (result.shouldStart)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            if (!IsAutomationRunning)
                            {
                                LogInfo("[AUTO-RESTART] Conditions met - starting automation");
                                
                                if (IsAuthenticated && _userParameters != null && _userParameters.IsComplete)
                                {
                                    LogInfo($"Auto-restart: {result.reason}");
                                    
                                    NativeNotificationService.Show(
                                        "Auto-restart aktivert", 
                                        result.reason,
                                        "SUCCESS"
                                    );

                                    await StartAutomationAsync();
                                }
                                else
                                {
                                    LogInfo("Auto-restart aktivert men autentisering kreves");
                                    NativeNotificationService.Show(
                                        "Autentisering kreves",
                                        "Auto-start er aktivert, men du må autentisere først.",
                                        "WARNING"
                                    );
                                }
                            }
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    }
                    else if (result.shouldNotify && result.nextStartTime.HasValue)
                    {
                        var now = DateTime.Now;
                        var hoursSinceLastLog = (now - _lastDailyLogTime).TotalHours;
                        
                        if (hoursSinceLastLog >= 12) 
                        {
                            _lastDailyLogTime = now;
                            
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                LogInfo($"Auto-start status: {result.reason}");
                                
                                var timeUntil = result.nextStartTime.Value - now;
                                if (timeUntil.TotalHours < 24)
                                {
                                    NativeNotificationService.Show(
                                        "Auto-start planlagt",
                                        result.reason,
                                        "INFO"
                                    );
                                }
                            }, Avalonia.Threading.DispatcherPriority.Background);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LogError($"Auto-restart check error: {ex.Message}");
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            });
        }

        public async Task RefreshAutoStartStatusAsync()
        {
            try
            {
                LogDebug("[AUTO-START] Manual refresh triggered - checking status immediately");

                lock (_autoStartCheckLock)
                {
                    _hasPerformedInitialAutoStartCheck = false;
                }

                await CheckSchoolHoursAutoRestartSilentAsync();
            }
            catch (Exception ex)
            {
                LogError($"Error refreshing auto-start status: {ex.Message}");
            }
        }

        private void StartCaffeinate()
        {
            lock (_caffeinateLock)
            {
                if (_caffeinateProcess != null)
                {
                    LogDebug("Caffeinate already running");
                    return;
                }

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    LogDebug("Caffeinate only works on macOS - skipping");
                    return;
                }

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/caffeinate",
                        Arguments = "-dims",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _caffeinateProcess = Process.Start(startInfo);
                    LogInfo("✓ Caffeinate started - Mac will not sleep during automation");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to start caffeinate: {ex.Message}");
                }
            }
        }

        private void StopCaffeinate()
        {
            lock (_caffeinateLock)
            {
                if (_caffeinateProcess == null)
                    return;

                try
                {
                    if (!_caffeinateProcess.HasExited)
                    {
                        _caffeinateProcess.Kill();
                        _caffeinateProcess.WaitForExit(1000);
                        LogInfo("✓ Caffeinate stopped - Mac can sleep normally");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error stopping caffeinate: {ex.Message}");
                }
                finally
                {
                    _caffeinateProcess?.Dispose();
                    _caffeinateProcess = null;
                }
            }
        }

        private string GetProcessedNotificationsFilePath()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "processed_notifications.json");
        }

        public class EnhancedAdminNotification
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public string? Message { get; set; }
            public string? Priority { get; set; }
            public string? Target_Email { get; set; }
            public string? Image_Url { get; set; }
            public string? Custom_Color { get; set; }
            public DateTime Created_At { get; set; }
        }

        public class AdminNotification
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public string? Message { get; set; }
            public string? Priority { get; set; }
            public string? Target_Email { get; set; }
            public DateTime Created_At { get; set; }
        }

        public class AdminNotificationWithDelivery : AdminNotification
        {
            public List<NotificationDelivery>? Notification_Deliveries { get; set; }
        }

        public class NotificationDelivery
        {
            public string? User_Email { get; set; }
            public string? Status { get; set; }
            public DateTime Delivered_At { get; set; }
        }

        public string Greeting => "AkademiTrack - STU Tidsregistrering";

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAutomationRunning
        {
            get => _isAutomationRunning;
            private set
            {
                if (_isAutomationRunning != value)
                {
                    _isAutomationRunning = value;
                    OnPropertyChanged();
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<LogEntry> LogEntries
        {
            get => _logEntries;
            private set
            {
                _logEntries = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<NotificationEntry> Notifications
        {
            get => _notifications;
            private set
            {
                _notifications = value;
                OnPropertyChanged();
            }
        }

        private Task ToggleThemeAsync()
        {
            Services.ThemeManager.Instance.ToggleTheme();
            LogInfo($"Theme changed to {(Services.ThemeManager.Instance.IsDarkMode ? "dark" : "light")} mode");
            return Task.CompletedTask;
        }

        public NotificationEntry? CurrentNotification
        {
            get => _currentNotification;
            private set
            {
                _currentNotification = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentNotification));
            }
        }

        public bool HasCurrentNotification => _currentNotification != null;

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set
            {
                _showDetailedLogs = value;
                OnPropertyChanged();
            }
        }

        public ICommand StartAutomationCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand DismissNotificationCommand { get; }
        public ICommand OpenTutorialCommand { get; }
        public ICommand RefreshDataCommand { get; }

        private Task DismissCurrentNotificationAsync()
        {
            CurrentNotification = null!;
            return Task.CompletedTask;
        }

        private void LogInfo(string message)
        {
            AddLogEntry(message, "INFO");
        }

        private void LogSuccess(string message)
        {
            AddLogEntry(message, "SUCCESS");
        }

        private void LogError(string message)
        {
            AddLogEntry(message, "ERROR");
        }

        private void LogDebug(string message)
        {
            if (ShowDetailedLogs)
            {
                AddLogEntry(message, "DEBUG");
            }
        }

        private void AddLogEntry(string message, string level)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            };

            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Add(logEntry);

                if (ShouldShowInStatus(message, level))
                {
                    StatusMessage = logEntry.FormattedMessage;
                }

                while (LogEntries.Count > 100)
                {
                    LogEntries.RemoveAt(0);
                }
            }, Avalonia.Threading.DispatcherPriority.Background);  
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
                "Stopper automatisering på grunn av nettleser problem...",
                "Automatisering vil bli stoppet - start på nytt for å prøve igjen",
                "Automatisering fullført - alle STU-økter håndtert",
                "Ingen STUDIE-økter funnet for i dag - viser melding og stopper automatisering",
                "Vennligst fullfør innloggingsprosessen i nettleseren",
                "Innlogging fullført!",
                "Autentisering og parametere fullført - starter overvåkingssløyfe...",
                "Registreringsvindu er ÅPENT for STU økt",
                "Forsøker å registrere oppmøte...",
                "Alle STU-økter er håndtert for i dag!",
                "Automatisering fullført - alle STU-økter håndtert",
                "Syklus #"
            };

            foreach (var important in importantMessages)
            {
                if (message.StartsWith(important))
                {
                    return true;
                }
            }

            if (message.Contains("STU-økter for i dag") && message.Contains("Fant"))
            {
                return true;
            }

            if (message.Contains("Syklus #") || message.Contains("Status:"))
            {
                return true;
            }

            if (message.Contains("Status:") && (message.Contains("åpne") || message.Contains("registrerte") || message.Contains("venter")))
            {
                return true;
            }

            if (level == "DEBUG")
            {
                return false;
            }

            var skipMessages = new[]
            {
                "Logger tømt",
                "Detaljert logging",
                "Innstillinger vindu åpnet",
                "Settings window opened",
                "Innstillinger",
                "Admin notification system initialized",
                "Notification window closed:",
                "Notification window shown successfully:",
                "Creating overlay window for:",
                "Skipping notification",
                "Notification filtered out:",
                "Showing enhanced system overlay notification:",
                "Ekstraherte cookies:",
                "Cookies saved to:",
                "Loaded",
                "cookies from file:",
                "Making request with parameters:",
                "Request URL:",
                "Response received",
                "JSON deserialized successfully",
                "Sending HTTP request...",
                "Response status:",
                "Public IP:",
                "Fetching public IP address...",
                "Using public IP:",
                "Registration payload:",
                "Sending registration request...",
                "Registration response:",
                "Bruker parametere:",
                "Henter timeplandata for hele dagen...",
                "Hentet",
                "timeplan elementer for hele dagen",
                "STU økt:",
                "Registreringsvindu ikke åpnet ennå for",
                "Registreringsvindu lukket for",
                "Venter 1 minutt før nytt forsøk...",
                "Rydder opp nettleser ressurser...",
                "Nettleser opprydding fullført",
                "Disposing resources...",
                "vindu åpnet",
                "window opened",
            };

            foreach (var skip in skipMessages)
            {
                if (message.StartsWith(skip) || message.Contains(skip))
                {
                    return false;
                }
            }

            if (level == "INFO")
            {
                if (level == "WARNING")
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        private Task ClearLogsAsync()
        {
            LogEntries.Clear();
            LogInfo("Logger tømt");
            return Task.CompletedTask;
        }

        private Task ToggleDetailedLogsAsync()
        {
            ShowDetailedLogs = !ShowDetailedLogs;
            LogInfo($"Detaljert logging {(ShowDetailedLogs ? "aktivert" : "deaktivert")}");
            return Task.CompletedTask;
        }

        private async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                LogDebug("Checking internet connectivity...");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.GetAsync("https://www.google.com", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task StartAutomationAsync()
        {
            lock (_autoStartLock)
            {
                if (IsAutomationRunning)
                {
                    LogInfo("Automatisering kjører allerede - ignorerer ny startforespørsel");
                    return;
                }

                IsAutomationRunning = true;
            }

            StartCaffeinate();

            // CHECK: Ensure we're authenticated
            if (!IsAuthenticated || _userParameters == null || !_userParameters.IsComplete)
            {
                LogError("Ikke autentisert - kan ikke starte automatisering");
                NativeNotificationService.Show(
                    "Autentiseringsfeil",
                    "Du må være innlogget for å starte automatisering",
                    "ERROR"
                );
                IsAutomationRunning = false;
                StopCaffeinate();
                return;
            }

            bool hasInternet = await CheckInternetConnectionAsync();
            if (!hasInternet)
            {
                LogError("Ingen internettforbindelse - kan ikke starte automatisering");
                NativeNotificationService.Show(
                    "Ingen Internett",
                    "Automatisering kan ikke starte uten internettforbindelse. Vennligst koble til internett og prøv igjen.",
                    "ERROR"
                );
                IsAutomationRunning = false;
                StopCaffeinate();
                return;
            }

            await Services.SchoolTimeChecker.MarkTodayAsStartedAsync();
            LogDebug("[AUTO-START] Marked today as started to prevent duplicate auto-starts");

            _cancellationTokenSource = new CancellationTokenSource();

            var (shouldAutoStart, autoStartReason, nextStartTime, shouldNotify) = await Services.SchoolTimeChecker.ShouldAutoStartAutomationAsync();

            if (!shouldAutoStart && _cancellationTokenSource == null)
            {
                LogInfo($"Manuell start utenfor skoletid: {autoStartReason}");
            }
            else if (shouldAutoStart)
            {
                LogInfo($"Auto-start check passed: {autoStartReason}");
            }

            try
            {
                LogInfo("Starter automatisering...");
                NativeNotificationService.Show("Automatisering startet", "STU tidsregistrering automatisering kjører nå", "SUCCESS");

                var cookies = await SecureCredentialStorage.LoadCookiesAsync();

                if (cookies == null || cookies.Count == 0)
                {
                    LogError("Ingen gyldige cookies funnet - autentisering kreves");
                    LogInfo("Starter re-autentisering...");
                    
                    // Re-authenticate and get new cookies
                    _authService = new AuthenticationService();
                    var authResult = await _authService.AuthenticateAsync();
                    
                    if (authResult.Success && authResult.Cookies != null && authResult.Cookies.Count > 0)
                    {
                        LogSuccess("✓ Re-autentisering fullført!");
                        LogInfo($"Fikk {authResult.Cookies.Count} cookies");
                        
                        // Use the new cookies
                        cookies = authResult.Cookies;
                        
                        // Check if we got parameters
                        if (authResult.Parameters != null && authResult.Parameters.IsComplete)
                        {
                            LogSuccess("✓ Parametere mottatt fra autentisering");
                            _userParameters = authResult.Parameters;
                        }
                        else
                        {
                            LogInfo("Ingen parametere fra autentisering - bruker eksisterende parametere");
                            // Keep existing _userParameters - they're still valid
                        }
                        
                        // Update dashboard credentials
                        var servicesUserParams = new Services.UserParameters
                        {
                            FylkeId = _userParameters.FylkeId,
                            PlanPeri = _userParameters.PlanPeri,
                            SkoleId = _userParameters.SkoleId
                        };
                        
                        Dashboard.SetCredentials(servicesUserParams, cookies);
                        await Dashboard.RefreshDataAsync();
                        
                        LogSuccess($"✓ Autentisering vellykket - fortsetter med automatisering");
                    }
                    else
                    {
                        LogError($"Re-autentisering mislyktes - Success: {authResult.Success}, Cookies: {authResult.Cookies?.Count ?? 0}");
                        NativeNotificationService.Show(
                            "Autentisering mislyktes",
                            "Kunne ikke autentisere. Vennligst prøv igjen.",
                            "ERROR"
                        );
                        IsAutomationRunning = false;
                        StopCaffeinate();
                        return;
                    }
                }
                else
                {
                    LogSuccess($"✓ Cookies lastet - {cookies.Count} cookies funnet");
                }

                LogSuccess("Autentisering og parametere fullført - starter overvåkingssløyfe...");
                LogInfo($"Bruker parametere: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");

                await RunMonitoringLoopAsync(_cancellationTokenSource.Token, cookies);
            }
            catch (OperationCanceledException)
            {
                LogInfo("Automatisering stoppet av bruker");
                NativeNotificationService.Show("Automatisering stoppet", "Overvåking har blitt stoppet", "INFO");
            }
            catch (Exception ex)
            {
                LogError($"Automatisering feil: {ex.Message}");
                if (ShowDetailedLogs)
                {
                    LogDebug($"Stack trace: {ex.StackTrace}");
                }
            }
            finally
            {
                IsAutomationRunning = false;
                _cancellationTokenSource?.Dispose();
                StopCaffeinate();

                if (_cancellationTokenSource != null)
                {
                    LogInfo("Automatisering fullført");
                }
            }
        }

        private Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                StopCaffeinate();
                LogInfo("Stopp forespurt - stopper automatisering...");
                NativeNotificationService.Show("Automatisering stoppet", "Automatisering har blitt stoppet av bruker", "INFO");
            }
            return Task.CompletedTask;
        }

        private bool HasConflictingClass(ScheduleItem stuSession, List<ScheduleItem> allScheduleItems)
        {
            try
            {
                if (!TimeSpan.TryParse(stuSession.StartKl, out var stuStartTime) ||
                    !TimeSpan.TryParse(stuSession.SluttKl, out var stuEndTime))
                {
                    LogDebug($"Could not parse STU session times: {stuSession.StartKl}-{stuSession.SluttKl}");
                    return false;
                }

                var conflictingClasses = allScheduleItems
                    .Where(item => item.Dato == stuSession.Dato &&
                                  item.KNavn != "STU" &&
                                  item.Id != stuSession.Id)
                    .ToList();

                foreach (var otherClass in conflictingClasses)
                {
                    if (!TimeSpan.TryParse(otherClass.StartKl, out var otherStartTime) ||
                        !TimeSpan.TryParse(otherClass.SluttKl, out var otherEndTime))
                    {
                        LogDebug($"Could not parse class times for {otherClass.KNavn}: {otherClass.StartKl}-{otherClass.SluttKl}");
                        continue;
                    }

                    bool hasOverlap = stuStartTime < otherEndTime && otherStartTime < stuEndTime;

                    if (hasOverlap)
                    {
                        LogInfo($"CONFLICT DETECTED: STU session {stuSession.StartKl}-{stuSession.SluttKl} overlaps with class {otherClass.KNavn} ({otherClass.StartKl}-{otherClass.SluttKl})");
                        LogInfo($"Skipping STU registration - student must attend regular class: {otherClass.KNavn}");
                        return true;
                    }
                }

                LogDebug($"No conflicts found for STU session {stuSession.StartKl}-{stuSession.SluttKl}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Error checking for class conflicts: {ex.Message}");
                return false;
            }
        }

        private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken, Dictionary<string, string> cookies)
        {
            int cycleCount = 0;

            LogInfo("Henter timeplandata for hele dagen...");
            _cachedScheduleData = await GetFullDayScheduleDataAsync(cookies);
            _scheduleDataFetchTime = DateTime.Now;

            if (_cachedScheduleData == null)
            {
                LogError("Kunne ikke hente timeplandata - cookies kan være utløpt");
                LogInfo("Automatisering vil stoppe - start på nytt for å autentisere igjen");
                return;
            }

            LogSuccess($"Hentet {_cachedScheduleData.Count} timeplan elementer for hele dagen");

            var today = DateTime.Now.ToString("yyyyMMdd");
            var todaysStuSessions = _cachedScheduleData
                .Where(item => item.Dato == today && item.KNavn == "STU")
                .ToList();

            LogInfo($"Fant {todaysStuSessions.Count} STU-økter for i dag ({DateTime.Now:yyyy-MM-dd})");

            if (todaysStuSessions.Count == 0)
            {
                LogInfo("Ingen STUDIE-økter funnet for i dag - viser melding og stopper automatisering");
                NativeNotificationService.Show("Ingen STUDIE-økter funnet for i dag",
                    "Det er ingen STU-økter å registrere for i dag. Automatiseringen stopper.", "INFO");
                return;
            }

            var validStuSessions = new List<ScheduleItem>();
            foreach (var stuSession in todaysStuSessions)
            {
                if (HasConflictingClass(stuSession, _cachedScheduleData))
                {
                    LogInfo($"STU session {stuSession.StartKl}-{stuSession.SluttKl} has conflicting class - excluded from registration");
                }
                else
                {
                    validStuSessions.Add(stuSession);
                }
            }

            if (validStuSessions.Count == 0)
            {
                LogInfo("Alle STU-økter har konflikter med andre timer - ingen å registrere");
                NativeNotificationService.Show("Ingen gyldige STU-økter",
                    "Alle STU-økter overlapper med andre klasser. Ingen registreringer vil bli gjort.", "WARNING");
                return;
            }

            LogInfo($"Etter konflikt-sjekking: {validStuSessions.Count} av {todaysStuSessions.Count} STU-økter er gyldige (ingen klassekonflikt)");

            foreach (var stuTime in validStuSessions)
            {
                LogInfo($"Gyldig STU økt: {stuTime.StartKl}-{stuTime.SluttKl}, Registreringsvindu: {stuTime.TidsromTilstedevaerelse}");
            }

            var registeredSessions = new HashSet<string>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    cycleCount++;
                    var currentTime = DateTime.Now.ToString("HH:mm");
                    LogInfo($"Syklus #{cycleCount} - Sjekker STU registreringsvinduer (kl. {currentTime})");

                    bool allSessionsComplete = true;
                    int openWindows = 0;
                    int closedWindows = 0;
                    int notYetOpenWindows = 0;

                    foreach (var stuSession in validStuSessions)
                    {
                        var sessionKey = $"{stuSession.StartKl}-{stuSession.SluttKl}";

                        if (registeredSessions.Contains(sessionKey))
                        {
                            closedWindows++;
                            continue;
                        }

                        var registrationStatus = GetRegistrationWindowStatus(stuSession);

                        switch (registrationStatus)
                        {
                            case RegistrationWindowStatus.Open:
                                openWindows++;
                                allSessionsComplete = false;
                                LogInfo($"Registreringsvindu er ÅPENT for STU økt {stuSession.StartKl}-{stuSession.SluttKl}");
                                LogInfo("Forsøker å registrere oppmøte...");

                                try
                                {
                                    var registrationResult = await RegisterAttendanceAsync(stuSession, cookies);

                                    if (registrationResult)
                                    {
                                        LogSuccess($"Registrerte oppmøte for {stuSession.StartKl}-{stuSession.SluttKl}!");
                                        NativeNotificationService.Show("Registrering vellykket",
                                            $"Registrert for STU {stuSession.StartKl}-{stuSession.SluttKl}", "SUCCESS");

                                        registeredSessions.Add(sessionKey);
                                        await Dashboard.RefreshDataAsync();

                                    }
                                    else
                                    {
                                        LogDebug($"Registrering ikke fullført for {stuSession.StartKl}-{stuSession.SluttKl} - prøver igjen neste syklus");
                                    }
                                }
                                catch (Exception regEx)
                                {
                                    LogError($"Registrering feilet: {regEx.Message}");
                                }
                                break;

                            case RegistrationWindowStatus.NotYetOpen:
                                notYetOpenWindows++;
                                allSessionsComplete = false;
                                var now = DateTime.Now.ToString("HH:mm");
                                LogDebug($"Registreringsvindu ikke åpnet ennå for {stuSession.StartKl}-{stuSession.SluttKl} (nåværende tid: {now}, vindu: {stuSession.TidsromTilstedevaerelse})");
                                break;

                            case RegistrationWindowStatus.Closed:
                                closedWindows++;
                                var currentTimeForClosed = DateTime.Now.ToString("HH:mm");
                                LogDebug($"Registreringsvindu lukket for {stuSession.StartKl}-{stuSession.SluttKl} (nåværende tid: {currentTimeForClosed}, vindu: {stuSession.TidsromTilstedevaerelse})");
                                break;
                        }
                    }

                    if (allSessionsComplete || registeredSessions.Count == validStuSessions.Count)
                    {
                        LogSuccess($"Alle {validStuSessions.Count} gyldige STU-økter er håndtert for i dag!");
                        LogInfo($"Registrerte økter: {registeredSessions.Count}, Totalt gyldige: {validStuSessions.Count}");

                        await Services.SchoolTimeChecker.MarkTodayAsCompletedAsync();
                        LogInfo("✓ Dagens registreringer merket som fullført - auto-start vil ikke kjøre igjen før i morgen");

                        if (registeredSessions.Count > 0)
                        {
                            NativeNotificationService.Show("Alle Studietimer Registrert",
                                $"Alle {validStuSessions.Count} gyldige STU-økter er fullført og registrert!", "SUCCESS");
                        }
                        else
                        {
                            NativeNotificationService.Show("Ingen Flere Økter",
                                $"Alle {validStuSessions.Count} gyldige STU-økter har passert registreringsvinduet. Ingen flere å registrere i dag.", "INFO");
                        }
                        break;
                    }

                    LogInfo($"Status: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} lukkede/registrerte - neste sjekk om 30s");

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Overvåkingsfeil: {ex.Message}");
                    LogInfo("Venter 30 sekunder før nytt forsøk...");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        private async Task<List<ScheduleItem>> GetFullDayScheduleDataAsync(Dictionary<string, string> cookies)
        {
            try
            {
                var scheduleResponse = await GetScheduleDataAsync(cookies);
                return scheduleResponse?.Items ?? new List<ScheduleItem>();
            }
            catch (Exception ex)
            {
                LogError($"Error getting full day schedule data: {ex.Message}");
                return null!;
            }
        }

        private enum RegistrationWindowStatus
        {
            NotYetOpen,
            Open,
            Closed
        }

        private RegistrationWindowStatus GetRegistrationWindowStatus(ScheduleItem stuTime)
        {
            if (stuTime.TidsromTilstedevaerelse == null)
            {
                LogDebug($"No registration time window defined for session {stuTime.StartKl}-{stuTime.SluttKl}");
                return RegistrationWindowStatus.Closed;
            }

            var parts = stuTime.TidsromTilstedevaerelse.Split(" - ");
            if (parts.Length != 2)
            {
                LogDebug($"Invalid time window format: {stuTime.TidsromTilstedevaerelse}");
                return RegistrationWindowStatus.Closed;
            }

            if (!TimeSpan.TryParse(parts[0], out var startTime) ||
                !TimeSpan.TryParse(parts[1], out var endTime))
            {
                LogDebug($"Could not parse time window: {stuTime.TidsromTilstedevaerelse}");
                return RegistrationWindowStatus.Closed;
            }

            var now = DateTime.Now.TimeOfDay;

            if (now < startTime)
            {
                return RegistrationWindowStatus.NotYetOpen;
            }
            else if (now >= startTime && now <= endTime)
            {
                return RegistrationWindowStatus.Open;
            }
            else
            {
                return RegistrationWindowStatus.Closed;
            }
        }

        private async Task<ScheduleResponse> GetScheduleDataAsync(Dictionary<string, string> cookies)
        {
            try
            {
                if (_userParameters == null || !_userParameters.IsComplete)
                {
                    LogError("Missing user parameters - cannot fetch schedule");
                    return null!;
                }

                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&offset=0&totalResults=true";

                LogDebug($"Making request with parameters: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");
                LogDebug($"Request URL: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                LogDebug("Sending HTTP request...");
                var response = await _httpClient.SendAsync(request);

                LogDebug($"Response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    LogError($"HTTP request failed with status {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"Error response: {errorContent}");
                    return null!;
                }

                var json = await response.Content.ReadAsStringAsync();
                LogDebug($"Response received ({json.Length} characters)");

                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug("JSON deserialized successfully");
                return scheduleResponse!;
            }
            catch (Exception ex)
            {
                LogError($"Error getting schedule data: {ex.Message}");
                return null!;
            }
        }

        private async Task<bool> RegisterAttendanceAsync(ScheduleItem stuTime, Dictionary<string, string> cookies)
        {
            try
            {
                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

                var publicIp = await GetPublicIpAsync();
                LogDebug($"Using public IP: {publicIp}");

                var payload = new
                {
                    name = "lagre_oppmote",
                    parameters = new object[]
                    {
                        new { fylkeid = _userParameters?.FylkeId ?? "00" },
                        new { skoleid = _userParameters?.SkoleId ?? "312" },
                        new { planperi = _userParameters?.PlanPeri ?? "2025-26" },
                        new { ansidato = stuTime.Dato },
                        new { stkode = stuTime.Stkode },
                        new { kl_trinn = stuTime.KlTrinn },
                        new { kl_id = stuTime.KlId },
                        new { k_navn = stuTime.KNavn },
                        new { gruppe_nr = stuTime.GruppeNr },
                        new { timenr = stuTime.Timenr },
                        new { fravaerstype = "M" },
                        new { ip = publicIp }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                LogDebug($"Registration payload: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Origin", "https://iskole.net");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                LogDebug("Sending registration request...");
                var response = await _httpClient.SendAsync(request);

                var responseContent = await response.Content.ReadAsStringAsync();
                LogDebug($"Registration response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    if (await CheckForNetworkErrorInResponse(responseContent, stuTime))
                    {
                        return false;
                    }

                    var registrationTime = DateTime.Now.ToString("HH:mm:ss");

                    return true;
                }
                else
                {
                    LogError($"Registration failed with status {response.StatusCode}");
                    LogDebug($"Error response: {responseContent}");

                    if (await CheckForNetworkErrorInResponse(responseContent, stuTime))
                    {
                        return false;
                    }

                    throw new Exception($"Registration failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Registration error: {ex.Message}");
                throw;
            }
        }

        private Task<bool> CheckForNetworkErrorInResponse(string responseContent, ScheduleItem stuTime)
        {
            try
            {
                if (responseContent.Contains("Du må være koblet på skolens nettverk for å kunne registrere fremmøte") ||
                    responseContent.Contains("retval\":50") ||
                    responseContent.Contains("må være koblet på skolens nettverk"))
                {
                    LogError($"NETTVERKSFEIL: Må være tilkoblet skolens nettverk for å registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}");
                    LogInfo("Automatiseringen fortsetter å kjøre - koble til skolens WiFi for å registrere");

                    NativeNotificationService.Show(
                        "Koble til Skolens Nettverk",
                        $"Du må være tilkoblet skolens WiFi for å registrere STU {stuTime.StartKl}-{stuTime.SluttKl}. " +
                        "Automatiseringen fortsetter å kjøre - koble til skolens nettverk så prøver den igjen.",
                        "WARNING"
                    );

                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                LogDebug($"Error checking for network error in response: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                LogDebug("Fetching public IP address...");
                var ip = await _httpClient.GetStringAsync("https://api.ipify.org");
                LogDebug($"Public IP: {ip}");
                return ip;
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to get public IP: {ex.Message}, using fallback");
                return "127.0.0.1";
            }
        }

        private async Task CheckForUpdatesAutomatically(object? state)
        {
            try
            {
                if ((DateTime.Now - _lastUpdateCheck).TotalMinutes < 25) return;

                _lastUpdateCheck = DateTime.Now;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync(UPDATE_JSON_URL, cts.Token);

                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.latest_version)) return;

                var latestVersion = updateInfo.latest_version.TrimStart('v');
                var currentVersion = _applicationInfo.Version.TrimStart('v');

                if (IsNewerVersion(latestVersion, currentVersion))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ShowUpdateNotification(updateInfo);
                    });
                }
            }
            catch { }
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                var latestParts = latestVersion.Split('.').Select(int.Parse).ToArray();
                var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
                {
                    if (latestParts[i] > currentParts[i])
                        return true;
                    if (latestParts[i] < currentParts[i])
                        return false;
                }

                return latestParts.Length > currentParts.Length;
            }
            catch (Exception ex)
            {
                LogDebug($"Kunne ikke sammenligne versjoner: {ex.Message}");
                return false;
            }
        }

        private void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            var title = "Ny Oppdatering Tilgjengelig!";
            var message = $"Versjon {updateInfo.latest_version} er tilgjengelig!\n\n";

            LogInfo($"Viser oppdateringsvarsel: {updateInfo.latest_version}");
            NativeNotificationService.Show(title, message, "INFO");
        }


        private Task OpenTutorialAsync()
        {
            LogInfo("Åpner veiledning...");
        
            ShowDashboard = false;
            ShowSettings = false;
            ShowTutorial = true;

            return Task.CompletedTask;
        }

        private Task OpenSettingsAsync()
        {
            LogInfo("Åpner innstillinger...");

            ShowDashboard = false;
            ShowTutorial = false;
            ShowSettings = true;

            // Subscribe to close event if not already subscribed
            SettingsViewModel.CloseRequested -= OnSettingsCloseRequested;
            SettingsViewModel.CloseRequested += OnSettingsCloseRequested;

            return Task.CompletedTask;
        }

        private void OnSettingsCloseRequested(object? sender, EventArgs e)
        {
            _ = BackToDashboardAsync();
        }

        private Task BackToDashboardAsync()
        {
            LogInfo("Tilbake til dashboard...");
        
            ShowTutorial = false;
            ShowSettings = false;
            ShowDashboard = true;
        
            return Task.CompletedTask;
        }


    private async Task RefreshDataAsync()
    {
        try
        {
            LogInfo("🔄 Oppdaterer data...");
            StatusMessage = "Oppdaterer...";

            // Check if we have valid cookies
            var cookies = await SecureCredentialStorage.LoadCookiesAsync();

            if (cookies == null || cookies.Count == 0)
            {
                LogInfo("Ingen cookies funnet - autentiserer på nytt...");
                await ReauthenticateAsync();
                return;
            }

            // Test if cookies are still valid by trying to fetch data
            var attendanceService = new AttendanceDataService();
            
            var servicesUserParams = new Services.UserParameters
            {
                FylkeId = _userParameters?.FylkeId,
                PlanPeri = _userParameters?.PlanPeri,
                SkoleId = _userParameters?.SkoleId
            };
            
            attendanceService.SetCredentials(servicesUserParams, cookies);
            
            // Try to fetch summary to test cookies
            var summary = await attendanceService.GetAttendanceSummaryAsync();
            
            if (summary == null)
            {
                LogInfo("Cookies er utløpt - autentiserer på nytt i bakgrunnen...");
                await ReauthenticateAsync();
                return;
            }

            // Cookies are valid, refresh dashboard
            LogInfo("📊 Henter fersk data...");
            await Dashboard.RefreshDataAsync();
            
            LogSuccess("✓ Data oppdatert!");
            StatusMessage = "Data oppdatert!";
            
            NativeNotificationService.Show(
                "Data Oppdatert",
                "Dashboard-data er oppdatert med fersk informasjon.",
                "SUCCESS"
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
            LogError($"Feil ved oppdatering: {ex.Message}");
            StatusMessage = "Oppdatering mislyktes";
            
            NativeNotificationService.Show(
                "Oppdateringsfeil",
                "Kunne ikke oppdatere data. Prøver å autentisere på nytt...",
                "WARNING"
            );
            
            await Task.Delay(1000);
            await ReauthenticateAsync();
        }
    }

    private async Task ReauthenticateAsync()
    {
        try
        {
            LogInfo("🔐 Starter re-autentisering...");
            StatusMessage = "Autentiserer...";

            // Create NEW authentication service
            _authService = new AuthenticationService();

            LogInfo("Åpner nettleser for innlogging...");
            
            // This will actually open the browser and perform full authentication
            var authResult = await _authService.AuthenticateAsync();

            // Check if we got valid cookies (most important!)
            if (authResult.Success && authResult.Cookies != null && authResult.Cookies.Count > 0)
            {
                LogSuccess($"✓ Autentisering fullført! Fikk {authResult.Cookies.Count} cookies");

                // If we got NEW parameters, use them
                if (authResult.Parameters != null && authResult.Parameters.IsComplete)
                {
                    LogSuccess("✓ Nye parametere mottatt fra autentisering");
                    _userParameters = authResult.Parameters;
                    LogInfo($"Parametere oppdatert: fylkeid={authResult.Parameters.FylkeId}, planperi={authResult.Parameters.PlanPeri}, skoleid={authResult.Parameters.SkoleId}");
                }
                else
                {
                    // No new parameters, but that's OK - we keep the existing ones
                    LogInfo("Ingen nye parametere fra autentisering - bruker eksisterende parametere");
                    
                    // If we don't have ANY parameters at all, that's a problem
                    if (_userParameters == null || !_userParameters.IsComplete)
                    {
                        LogError("Ingen gyldige parametere tilgjengelig - kan ikke fortsette");
                        StatusMessage = "Mangler parametere";
                        
                        NativeNotificationService.Show(
                            "Parameterfeil",
                            "Kunne ikke hente nødvendige parametere. Vennligst restart applikasjonen.",
                            "ERROR"
                        );
                        return;
                    }
                }

                // Update dashboard credentials with NEW cookies
                var servicesUserParams = new Services.UserParameters
                {
                    FylkeId = _userParameters.FylkeId,
                    PlanPeri = _userParameters.PlanPeri,
                    SkoleId = _userParameters.SkoleId
                };

                Dashboard.SetCredentials(servicesUserParams, authResult.Cookies);

                // Refresh dashboard data with new credentials
                LogInfo("📊 Laster dashboard data med nye cookies...");
                await Dashboard.RefreshDataAsync();

                LogSuccess("✓ Alt er oppdatert!");
                StatusMessage = "Oppdatert!";

                NativeNotificationService.Show(
                    "Oppdatering Vellykket",
                    "Nye cookies hentet og data oppdatert.",
                    "SUCCESS"
                );

                await Task.Delay(3000);
                if (StatusMessage == "Oppdatert!")
                {
                    StatusMessage = IsAutomationRunning ? "Automatisering kjører..." : "Klar til å starte";
                }
            }
            else
            {
                LogError($"❌ Autentisering mislyktes - Success: {authResult.Success}, Cookies: {authResult.Cookies?.Count ?? 0}");
                StatusMessage = "Autentisering mislyktes";
                
                NativeNotificationService.Show(
                    "Autentisering Mislyktes",
                    "Kunne ikke hente nye cookies. Vennligst prøv igjen.",
                    "ERROR"
                );
            }
        }
        catch (Exception ex)
        {
            LogError($"Re-autentisering feil: {ex.Message}");
            if (ShowDetailedLogs)
            {
                LogDebug($"Stack trace: {ex.StackTrace}");
            }
            StatusMessage = "Autentisering feilet";
            
            NativeNotificationService.Show(
                "Autentiseringsfeil",
                "En feil oppstod under autentisering. Prøv igjen senere.",
                "ERROR"
            );
        }
    }

        public void Dispose()
        {
            LogInfo("Disposing resources...");

            StopCaffeinate();

            _updateCheckTimer?.Dispose();
            _schoolHoursCheckTimer?.Dispose();

            _isProcessingQueue = false;

            lock (_activeOverlayWindowsLock)
            {
                foreach (var w in _activeOverlayWindows.ToList())
                {
                    try { if (w.IsVisible) w.Close(); } catch { }
                }
                _activeOverlayWindows.Clear();
            }

            _notificationSemaphore?.Dispose();
            _cancellationTokenSource?.Cancel();
            
            _authService?.Dispose();
            
            _httpClient?.Dispose();
            
            Dashboard?.Dispose();
        }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem>? Items { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string? Fag { get; set; }
        public string? Stkode { get; set; }
        public string? KlTrinn { get; set; }
        public string? KlId { get; set; }
        public string? KNavn { get; set; }
        public string? GruppeNr { get; set; }
        public string? Dato { get; set; }
        public string? StartKl { get; set; }
        public string? SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string? Typefravaer { get; set; }
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string? TidsromTilstedevaerelse { get; set; }
        public int Timenr { get; set; }
    }
}