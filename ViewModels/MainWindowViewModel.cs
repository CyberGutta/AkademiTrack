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
        public string? Level { get; set; } // INFO, SUCCESS, ERROR, DEBUG
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }

    public class NotificationEntry
    {
        public DateTime Timestamp { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Level { get; set; } // INFO, SUCCESS, ERROR, WARNING
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
        private IWebDriver? _webDriver;
        private ObservableCollection<LogEntry> _logEntries;
        private ObservableCollection<NotificationEntry> _notifications;
        private bool _showDetailedLogs = true;
        private NotificationEntry? _currentNotification;
        private int _notificationIdCounter = 0;
        private readonly List<NotificationOverlayWindow> _activeOverlayWindows = new();
        private Timer _updateCheckTimer;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private const string UPDATE_JSON_URL = "https://cybergutta.github.io/AkademietTrack/update.json";
        private readonly ApplicationInfo _applicationInfo;

        private List<ScheduleItem>? _cachedScheduleData;
        private DateTime _scheduleDataFetchTime;

        private Timer _adminNotificationTimer;
        private HashSet<string> _processedNotificationIds = new HashSet<string>();
        private string _processedNotificationsFile;

        private readonly Queue<NotificationQueueItem> _notificationQueue = new Queue<NotificationQueueItem>();
        private bool _isShowingNotification = false;
        private readonly object _notificationLock = new object();

        private readonly SemaphoreSlim _notificationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isProcessingQueue = false;
        private readonly object _queueLock = new object();

        public new event PropertyChangedEventHandler? PropertyChanged;

        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co"; 
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k"; // Replace with your actual anon key


        private string _loginEmail = "";
        private string _loginPassword = "";
        private string _schoolName = "";


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
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _logEntries = new ObservableCollection<LogEntry>();
            _notifications = new ObservableCollection<NotificationEntry>();
            _applicationInfo = new ApplicationInfo();
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            ClearLogsCommand = new SimpleCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new SimpleCommand(ToggleDetailedLogsAsync);
            DismissNotificationCommand = new SimpleCommand(DismissCurrentNotificationAsync);
            ToggleThemeCommand = new SimpleCommand(ToggleThemeAsync);

            LogInfo("Applikasjon er klar");

            var directory = Path.GetDirectoryName(GetCookiesFilePath()) ?? Environment.CurrentDirectory;
            _processedNotificationsFile = Path.Combine(directory, "processed_notifications.json");


            _ = Task.Run(LoadProcessedNotificationIdsAsync);



            _ = Task.Run(CheckAutoStartAutomationAsync);

            _updateCheckTimer = new Timer(CheckForUpdatesAutomatically, null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(30));
            LogInfo("Automatisk oppdateringssjekker startet (sjekker hver 30. minutt)");
        }

        private async Task CheckAutoStartAutomationAsync()
        {
            try
            {
                await Task.Delay(2000);

                var settingsViewModel = new SettingsViewModel();
                await Task.Delay(300); 

                if (settingsViewModel.AutoStartAutomation)
                {
                    LogInfo("Auto-start automatisering er aktivert - starter automatisk...");

                    await LoadCredentialsAsync();
                    bool hasCredentials = !string.IsNullOrEmpty(_loginEmail) &&
                                         !string.IsNullOrEmpty(_loginPassword) &&
                                         !string.IsNullOrEmpty(_schoolName);

                    if (hasCredentials)
                    {
                        LogInfo("Starter automatisering automatisk med lagrede innloggingsopplysninger");
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await StartAutomationAsync();
                        });
                    }
                    else
                    {
                        LogInfo("Auto-start aktivert men ingen lagrede innloggingsopplysninger - venter på manuell start");
                        ShowNotification("Auto-start Aktivert",
                            "Auto-start er aktivert, men ingen lagrede innloggingsopplysninger funnet. Lagre innloggingsopplysninger i innstillinger.",
                            "WARNING");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Feil ved auto-start sjekk: {ex.Message}");
            }
        }

        private async Task LoadCredentialsAsync()
        {
            try
            {
                var settingsViewModel = new SettingsViewModel();

                await Task.Delay(200);

                var credentials = settingsViewModel.GetDecryptedCredentials();

                _loginEmail = credentials.email;
                _loginPassword = credentials.password;
                _schoolName = credentials.school;

                if (!string.IsNullOrEmpty(_loginEmail))
                {
                    LogInfo($"Innloggingsopplysninger lastet for: {_loginEmail}");
                }
                else
                {
                    LogInfo("Ingen lagrede innloggingsopplysninger funnet");
                }
            }
            catch (Exception ex)
            {
                LogError($"Kunne ikke laste innloggingsopplysninger: {ex.Message}");
                LogInfo("Fortsetter uten lagrede innloggingsopplysninger");

                _loginEmail = "";
                _loginPassword = "";
                _schoolName = "";
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

        private async Task LoadProcessedNotificationIdsAsync()
        {
            try
            {
                var filePath = GetProcessedNotificationsFilePath();
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var ids = JsonSerializer.Deserialize<string[]>(json);
                    if (ids != null)
                    {
                        _processedNotificationIds = new HashSet<string>(ids);
                    }
                }
            }
            catch (Exception )
            {
            }
        }

        private async Task SaveProcessedNotificationIdsAsync()
        {
            try
            {
                var filePath = GetProcessedNotificationsFilePath();
                var json = JsonSerializer.Serialize(_processedNotificationIds.ToArray());
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception )
            {
            }
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
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand DismissNotificationCommand { get; }


        private void ShowNotification(string title, string message, string level = "INFO")
        {
            var allowedNotifications = new[]
            {
        "Automatisering startet",
        "Automatisering stoppet",
        "Registrering vellykket",
        "Alle Studietimer Registrert",
        "Ingen STUDIE-økter funnet for i dag",
        "Ingen Flere Økter",
        "Ingen gyldige STU-økter",
        "FEIL: Ikke Tilkoblet Skolens Nettverk",
        "Koble til Skolens Nettverk",
        "Manuell pålogging kreves"
    };

            bool isAdminNotification = title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN[");

            if (allowedNotifications.Contains(title) || isAdminNotification)
            {
                LogDebug($"Queueing notification: {title}");

                bool isHighPriority = DetermineNotificationPriority(title, level);

                _ = Task.Run(() => QueueNotificationAsync(title, message, level, null!, null!, isHighPriority));
            }
            else
            {
                LogDebug($"Notification filtered out: {title}");
            }
        }

        private bool DetermineNotificationPriority(string title, string level)
        {
            if (level == "ERROR" || title.Contains("FEIL:"))
                return true;

            if (title == "Registrering vellykket")
                return true;

            if (title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN["))
                return true;

            if (title.Contains("Nettverk") || title.Contains("WiFi"))
                return true;

            if (title == "Ingen Flere Økter" || title.Contains("Alle Studietimer"))
                return true;

            return false;
        }

        private Task QueueNotificationAsync(string title, string message, string level,
    string? imageUrl = null, string? customColor = null, bool isHighPriority = false)
        {
            var queueItem = new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Level = level,
                ImageUrl = imageUrl ?? "",
                CustomColor = customColor ?? "",
                IsHighPriority = isHighPriority
            };

            lock (_queueLock)
            {
                if (isHighPriority)
                {
                    var tempList = _notificationQueue.ToList();
                    _notificationQueue.Clear();
                    _notificationQueue.Enqueue(queueItem);

                    foreach (var item in tempList)
                    {
                        _notificationQueue.Enqueue(item);
                    }

                    LogDebug($"HIGH PRIORITY notification queued: {title} (Queue size: {_notificationQueue.Count})");
                }
                else
                {
                    _notificationQueue.Enqueue(queueItem);
                    LogDebug($"Notification queued: {title} (Queue size: {_notificationQueue.Count})");
                }
            }

            if (!_isProcessingQueue)
            {
                _ = Task.Run(ProcessNotificationQueueAsync);
            }

            return Task.CompletedTask;
        }

        private async Task ProcessNotificationQueueAsync()
        {
            if (!await _notificationSemaphore.WaitAsync(100))
            {
                LogDebug("Notification processor already running - skipping");
                return;
            }

            try
            {
                _isProcessingQueue = true;
                LogDebug("Started notification queue processing");

                while (true)
                {
                    NotificationQueueItem nextNotification = null!;

                    lock (_queueLock)
                    {
                        if (_notificationQueue.Count == 0)
                        {
                            LogDebug("Queue empty - stopping processor");
                            break;
                        }

                        nextNotification = _notificationQueue.Dequeue();
                    }

                    LogDebug($"Processing notification: {nextNotification.Title} (Remaining: {_notificationQueue.Count})");

                    try
                    {
                        await ShowNotificationWithTimeoutAsync(nextNotification);
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to show notification '{nextNotification.Title}': {ex.Message}");
                    }

                    await Task.Delay(300);
                }
            }
            finally
            {
                _isProcessingQueue = false;
                _notificationSemaphore.Release();
                LogDebug("Notification queue processing completed");
            }
        }

        private async Task ShowNotificationWithTimeoutAsync(NotificationQueueItem item)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    await CreateAndShowNotificationAsync(item, cts.Token);
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await CreateAndShowNotificationAsync(item, cts.Token);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                LogDebug($"Notification '{item.Title}' timed out - continuing with next");
            }
            catch (Exception ex)
            {
                LogError($"Failed to show notification '{item.Title}': {ex.Message}");

                LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
            }
        }

        private Task CreateAndShowNotificationAsync(NotificationQueueItem item, CancellationToken cancellationToken)
        {
            try
            {
                CleanupOldWindows();
                LogDebug($"Creating notification window: {item.Title}");

                NotificationOverlayWindow overlayWindow;
                try
                {
                    overlayWindow = new NotificationOverlayWindow(
                        item.Title!,
                        item.Message!,
                        item.Level!,
                        item.ImageUrl!,
                        item.CustomColor!
                    );
                }
                catch (Exception createEx)
                {
                    LogError($"Failed to create notification window: {createEx.Message}");
                    LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
                    return Task.CompletedTask;
                }

                var windowClosed = false;
                overlayWindow.Closed += (s, e) =>
                {
                    if (!windowClosed)
                    {
                        windowClosed = true;
                        RemoveFromActiveWindows(overlayWindow);
                        LogDebug($"Notification closed: {item.Title}");
                    }
                };

                AddToActiveWindows(overlayWindow);

                try
                {
                    overlayWindow.Show();
                    LogDebug($"✓ Notification shown successfully: {item.Title}");
                }
                catch (Exception showEx)
                {
                    LogError($"Failed to show notification window: {showEx.Message}");
                    RemoveFromActiveWindows(overlayWindow);
                    LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Complete failure in notification display: {ex.Message}");
                LogInfo($"NOTIFICATION (emergency fallback): {item.Title} - {item.Message}");
            }

            return Task.CompletedTask;
        }

        private void AddToActiveWindows(NotificationOverlayWindow window)
        {
            lock (_activeOverlayWindows)
            {
                _activeOverlayWindows.Add(window);
                LogDebug($"Added window to tracking. Total active: {_activeOverlayWindows.Count}");
            }
        }

        private void RemoveFromActiveWindows(NotificationOverlayWindow window)
        {
            lock (_activeOverlayWindows)
            {
                _activeOverlayWindows.Remove(window);
                LogDebug($"Removed window from tracking. Total active: {_activeOverlayWindows.Count}");
            }
        }

        private void CleanupOldWindows()
        {
            try
            {
                lock (_activeOverlayWindows)
                {
                    for (int i = _activeOverlayWindows.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            if (!_activeOverlayWindows[i].IsVisible)
                            {
                                _activeOverlayWindows.RemoveAt(i);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Error checking window visibility: {ex.Message}");
                            _activeOverlayWindows.RemoveAt(i);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error during window cleanup: {ex.Message}");

                lock (_activeOverlayWindows)
                {
                    _activeOverlayWindows.Clear();
                }
            }
        }


        private void ShowSystemOverlayNotification(string title, string message, string level, string? imageUrl = null, string? customColor = null)
        {
            ShowNotification(title, message, level);
        }
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

            if (Dispatcher.UIThread.CheckAccess())
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
            }
            else
            {
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
                });
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
                "HTTP request failed with status"
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

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning) return;

            IsAutomationRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                LogInfo("Starter automatisering...");

                LogInfo("Laster innloggingsopplysninger...");
                await LoadCredentialsAsync();

                bool hasCredentials = !string.IsNullOrEmpty(_loginEmail) &&
                                     !string.IsNullOrEmpty(_loginPassword) &&
                                     !string.IsNullOrEmpty(_schoolName);

                if (hasCredentials)
                {
                    LogInfo($"Innloggingsopplysninger lastet for: {_loginEmail}");
                    ShowNotification("Automatisering startet", "STU tidsregistrering automatisering kjører nå", "SUCCESS");
                }
                else
                {
                    LogInfo("Ingen lagrede innloggingsopplysninger funnet - fortsetter med manuell innlogging");
                    ShowNotification("Manuell pålogging kreves", "Ingen lagrede innloggingsopplysninger - åpner nettleser for manuell innlogging", "INFO");
                }

                Dictionary<string, string> cookies = null!;
                bool needsFreshLogin = false;

                LogDebug("Laster eksisterende cookies fra fil...");
                cookies = await LoadCookiesAsync();

                if (cookies != null)
                {
                    LogInfo($"Fant {cookies.Count} eksisterende cookies, tester gyldighet...");
                    bool cookiesValid = await TestCookiesAsync(cookies);

                    if (cookiesValid)
                    {
                        LogSuccess("Eksisterende cookies er gyldige!");

                        try
                        {
                            LogInfo("Forsøker å laste parametere for gyldig cookie-økt...");
                            _userParameters = await ExtractUserParametersAsync(cookies);

                            if (_userParameters != null && _userParameters.IsComplete)
                            {
                                LogSuccess($"Parametere lastet: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            LogInfo("Parametere krever ny innlogging - cookies slettet");
                            needsFreshLogin = true;
                            cookies = null!;
                        }
                    }
                    else
                    {
                        LogInfo("Eksisterende cookies er ugyldige eller utløpt");
                        needsFreshLogin = true;
                    }
                }
                else
                {
                    LogInfo("Ingen eksisterende cookies funnet");
                    needsFreshLogin = true;
                }

                if (needsFreshLogin || cookies == null)
                {
                    if (hasCredentials)
                    {
                        LogInfo("Åpner nettleser for automatisk innlogging...");
                    }
                    else
                    {
                        LogInfo("Åpner nettleser for manuell innlogging...");
                    }

                    cookies = await GetCookiesViaBrowserAsync();

                    if (cookies == null)
                    {
                        LogError("Kunne ikke få cookies fra nettleser innlogging");
                        return;
                    }

                    if (hasCredentials)
                    {
                        LogSuccess($"Fikk {cookies.Count} nye cookies via automatisk innlogging");
                    }
                    else
                    {
                        LogSuccess($"Fikk {cookies.Count} nye cookies via manuell innlogging");
                    }
                }

                if (_userParameters == null || !_userParameters.IsComplete)
                {
                    LogError("KRITISK: Mangler gyldige parametere etter innlogging");
                    LogError("Dette bør ikke skje - sjekk parameter-capture logikk");
                    return;
                }

                LogSuccess("Autentisering og parametere fullført - starter overvåkingssløyfe...");
                LogInfo($"Bruker parametere: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");

                await RunMonitoringLoopAsync(_cancellationTokenSource.Token, cookies);
            }
            catch (OperationCanceledException)
            {
                LogInfo("Automatisering stoppet av bruker");
                ShowNotification("Automatisering stoppet", "Overvåking har blitt stoppet", "INFO");
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
                LogInfo("Automatisering stoppet");
            }
        }

        private Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                LogInfo("Stopp forespurt - stopper automatisering...");
                ShowNotification("Automatisering stoppet", "Automatisering har blitt stoppet av bruker", "INFO");
            }
            return Task.CompletedTask;
        }

        private async Task OpenSettingsAsync()
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                var settingsViewModel = new SettingsViewModel();

                settingsViewModel.SetLogEntries(this.LogEntries);
                settingsViewModel.ShowDetailedLogs = this.ShowDetailedLogs;

                settingsWindow.DataContext = settingsViewModel;

                settingsViewModel.CloseRequested += (s, e) => settingsWindow.Close();

                settingsViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SettingsViewModel.ShowDetailedLogs))
                    {
                        this.ShowDetailedLogs = settingsViewModel.ShowDetailedLogs;
                    }
                };

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var mainWindow = desktop.MainWindow;

                    if (mainWindow != null && mainWindow.IsVisible)
                    {
                        await settingsWindow.ShowDialog(mainWindow);
                    }
                    else
                    {
                        LogDebug("Main window not ready - showing settings as standalone window");
                        settingsWindow.Show();
                    }
                }
                else
                {
                    settingsWindow.Show();
                }

            }
            catch (Exception ex)
            {
                LogError($"Kunne ikke åpne innstillinger vindu: {ex.Message}");

                try
                {
                    var settingsWindow = new SettingsWindow();
                    var settingsViewModel = new SettingsViewModel();
                    settingsViewModel.SetLogEntries(this.LogEntries);
                    settingsViewModel.ShowDetailedLogs = this.ShowDetailedLogs;
                    settingsWindow.DataContext = settingsViewModel;
                    settingsViewModel.CloseRequested += (s, e) => settingsWindow.Close();
                    settingsWindow.Show();
                }
                catch (Exception fallbackEx)
                {
                    LogError($"Critical error opening settings: {fallbackEx.Message}");
                }
            }
        }

        private string GetCookiesFilePath()
        {
            string appSupportPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = Path.Combine(appSupportPath, "AkademiTrack");
            
            Directory.CreateDirectory(appDataDir);
            
            return Path.Combine(appDataDir, "cookies.json");
        }

        private async Task<Dictionary<string, string>> LoadCookiesAsync()
        {
            try
            {
                string cookiesPath = GetCookiesFilePath();
                
                if (!File.Exists(cookiesPath))
                {
                    LogDebug($"No cookies.json file found at: {cookiesPath}");
                    return null!;
                }

                var json = await File.ReadAllTextAsync(cookiesPath);
                var cookieArray = JsonSerializer.Deserialize<Cookie[]>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug($"Loaded {cookieArray?.Length ?? 0} cookies from file: {cookiesPath}");
                return cookieArray?.ToDictionary(
                    c => c.Name ?? "",         
                    c => c.Value ?? ""         
                ) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                LogError($"Failed to load cookies: {ex.Message}");
                return null!;
            }
        }

        private async Task<bool> TestCookiesAsync(Dictionary<string, string> cookies)
        {
            try
            {
                LogDebug("Testing cookies by fetching schedule data...");
                var scheduleData = await GetScheduleDataAsync(cookies);
                bool isValid = scheduleData?.Items != null;

                if (isValid)
                {
                    LogDebug($"Cookie test successful - found {scheduleData?.Items?.Count} schedule items");
                }
                else
                {
                    LogDebug("Cookie test failed - no schedule data returned");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                LogDebug($"Cookie test failed with exception: {ex.Message}");
                return false;
            }
        }


        private async Task<Dictionary<string, string>> GetCookiesViaBrowserAsync()
        {
            IWebDriver localWebDriver = null!;

            try
            {
                bool shouldTryAutomatic = !string.IsNullOrEmpty(_loginEmail) &&
                                         !string.IsNullOrEmpty(_loginPassword) &&
                                         !string.IsNullOrEmpty(_schoolName);

                var options = new ChromeOptions();
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--disable-web-security");
                options.AddArgument("--disable-features=VizDisplayCompositor");

                if (shouldTryAutomatic)
                {
                    options.AddArgument("--headless");
                    LogInfo("Forsøker automatisk innlogging i bakgrunnen...");
                }
                else
                {
                    options.AddArgument("--start-maximized");
                    LogInfo("Åpner synlig nettleser for manuell innlogging...");
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    options.AddArgument("--disable-gpu");
                }

                localWebDriver = new ChromeDriver(options);
                _webDriver = localWebDriver;

                LogInfo("Navigerer til innloggingsside: https://iskole.net/elev/?ojr=login");
                _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                await Task.Delay(1000);

                bool loginSuccess = false;

                if (shouldTryAutomatic)
                {
                    LogInfo("Utfører rask automatisk innlogging...");
                    loginSuccess = await PerformFastAutomaticLoginAsync();

                    if (loginSuccess)
                    {
                        LogSuccess("Automatisk innlogging fullført!");
                    }
                    else
                    {
                        LogInfo("Automatisk innlogging mislyktes - skifter til synlig modus for manuell innlogging");

                        ShowNotification("Manuell pålogging kreves",
                        "Automatisk innlogging mislyktes. Nettleseren åpnes for manuell innlogging.",
                        "WARNING");

                        await CleanupWebDriverAsync(localWebDriver);

                        var visibleOptions = new ChromeOptions();
                        visibleOptions.AddArgument("--no-sandbox");
                        visibleOptions.AddArgument("--disable-dev-shm-usage");
                        visibleOptions.AddArgument("--start-maximized");

                        localWebDriver = new ChromeDriver(visibleOptions);
                        _webDriver = localWebDriver;

                        _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");
                        await Task.Delay(1000);

                        LogInfo("Vennligst fullfør innloggingsprosessen i nettleseren");
                        var targetReached = await WaitForTargetUrlAsync();
                        if (!targetReached)
                        {
                            LogError("Tidsavbrudd - innlogging ble ikke fullført innen 10 minutter");
                            return null!;
                        }
                    }
                }
                else
                {
                    LogInfo("Vennligst fullfør innloggingsprosessen i nettleseren");
                    var targetReached = await WaitForTargetUrlAsync();
                    if (!targetReached)
                    {
                        LogError("Tidsavbrudd - innlogging ble ikke fullført innen 10 minutter");
                        return null!;
                    }
                }

                LogSuccess("Innlogging fullført!");

                await QuickParameterCapture();

                LogInfo("Ekstraherer cookies fra nettleser økten...");

                if (!IsWebDriverValid(_webDriver))
                {
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet etter innlogging");
                    ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return null!;
                }

                var seleniumCookies = _webDriver.Manage().Cookies.AllCookies;
                var cookieDict = seleniumCookies.ToDictionary(c => c.Name, c => c.Value);

                LogDebug($"Ekstraherte cookies: {string.Join(", ", cookieDict.Keys)}");

                await SaveCookiesAsync(seleniumCookies.Select(c => new Cookie { Name = c.Name, Value = c.Value }).ToArray());

                LogSuccess($"Ekstraherte og lagret {cookieDict.Count} cookies");
                return cookieDict;
            }
            catch (WebDriverException webEx) when (webEx.Message.Contains("no such window") ||
                                                webEx.Message.Contains("target window already closed") ||
                                                webEx.Message.Contains("Session info: chrome") ||
                                                webEx.Message.Contains("disconnected"))
            {
                LogError("Automatisering stoppet - bruker lukket innloggingsvinduet under prosessen");
                ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                await ForceStopAutomationAsync();
                return null!;
            }
            catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                        invEx.Message.Contains("no such session"))
            {
                LogError("Automatisering stoppet - bruker lukket innloggingsvinduet under prosessen");
                ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                await ForceStopAutomationAsync();
                return null!;
            }
            catch (Exception ex)
            {
                LogError($"Nettleser innlogging feilet: {ex.Message}");
                LogDebug($"Exception type: {ex.GetType().Name}");
                ShowNotification("Automatisering stoppet", "Innlogging feilet - automatisering stoppet", "ERROR");
                await ForceStopAutomationAsync();
                return null!;
            }
            finally
            {
                await CleanupWebDriverAsync(localWebDriver);
            }
        }

        private async Task<bool> PerformFastAutomaticLoginAsync()
        {
            try
            {
                if (_webDriver == null || !IsWebDriverValid(_webDriver))
                {
                    LogDebug("WebDriver not valid for automatic login");
                    return false;
                }

                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(6));

                LogDebug("Starting fast automatic login process...");

                try
                {
                    LogDebug("Looking for FEIDE button...");
                    var feideButton = wait.Until(driver =>
                    {
                        try
                        {
                            var selectors = new[]
                            {
                        "//span[contains(@class, 'feide_icon')]/ancestor::button",
                        "//button[contains(., 'FEIDE')]",
                        "//button[contains(@class, 'button')]"
                    };

                            foreach (var selector in selectors)
                            {
                                var elements = driver.FindElements(By.XPath(selector));
                                foreach (var btn in elements)
                                {
                                    if (btn.Displayed && btn.Enabled &&
                                        (btn.Text.Contains("FEIDE") || btn.GetAttribute("innerHTML").Contains("FEIDE")))
                                    {
                                        return btn;
                                    }
                                }
                            }
                            return null;
                        }
                        catch (NoSuchElementException)
                        {
                            return null;
                        }
                    });

                    if (feideButton != null)
                    {
                        LogDebug("Found FEIDE button - clicking...");
                        feideButton.Click();
                        await Task.Delay(800);
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    LogDebug("FEIDE button not found within timeout, continuing...");
                }

                if (await HandleFastOrganizationSelectionAsync())
                {
                    LogDebug("Organization selection completed");
                }

                return await HandleFastFeideLoginFormAsync();
            }
            catch (Exception ex)
            {
                LogError($"Error during fast automatic login: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> HandleFastOrganizationSelectionAsync()
        {
            try
            {
                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(5));

                LogDebug("Fast organization selection check...");

                IWebElement? orgSearchField = null;
                try
                {
                    orgSearchField = wait.Until(driver =>
                    {
                        try
                        {
                            var element = driver.FindElement(By.Id("org_selector_filter"));
                            return element.Displayed ? element : null;
                        }
                        catch (NoSuchElementException)
                        {
                            return null;
                        }
                    });
                }
                catch (WebDriverTimeoutException)
                {
                    LogDebug("Organization selection not needed");
                    return true;
                }
                if (orgSearchField == null)
                {
                    LogDebug("Organization search field not found");
                    return true;
                }

                if (orgSearchField != null)
                {
                    LogDebug($"Fast search for '{_schoolName}'...");

                    orgSearchField.Clear();
                    orgSearchField.SendKeys(_schoolName);
                    await Task.Delay(500); 

                    try
                    {
                        var schoolOption = wait.Until(driver =>
                        {
                            var selectors = new[]
                            {
                        $"//li[contains(text(), '{_schoolName}')]",
                        $"//div[contains(text(), '{_schoolName}')]",
                        $"//*[contains(text(), 'Akademiet Drammen')]"
                    };

                            foreach (var selector in selectors)
                            {
                                try
                                {
                                    var elements = driver.FindElements(By.XPath(selector));
                                    foreach (var elem in elements)
                                    {
                                        if (elem.Displayed && elem.Enabled)
                                        {
                                            return elem;
                                        }
                                    }
                                }
                                catch { continue; }
                            }
                            return null;
                        });

                        if (schoolOption != null)
                        {
                            LogDebug("Found school - clicking...");
                            schoolOption.Click();
                            await Task.Delay(300);

                            try
                            {
                                var continueButton = _webDriver?.FindElement(By.Id("selectorg_button"));
                                if (continueButton?.Enabled == true)
                                {
                                    continueButton.Click();
                                    await Task.Delay(800);
                                    return true;
                                }
                            }
                            catch (NoSuchElementException)
                            {
                                LogDebug("Continue button not found");
                            }
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        LogError($"Timeout while searching for '{_schoolName}'");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error during fast organization selection: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> HandleFastFeideLoginFormAsync()
        {
            try
            {
                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(8));

                LogDebug("Fast Feide form detection...");

                IWebElement? usernameField = null;
                IWebElement? passwordField = null;
                IWebElement? loginButton = null;

                try
                {
                    usernameField = wait.Until(driver =>
                    {
                        try
                        {
                            var field = driver.FindElement(By.Id("username"));
                            if (field.Displayed) return field;
                        }
                        catch { }

                        try
                        {
                            var altField = driver.FindElement(By.Name("feidename"));
                            if (altField.Displayed) return altField;
                        }
                        catch { }

                        return null;
                    });

                    try
                    {
                        if (_webDriver == null)
                        {
                            LogError("WebDriver is null, cannot find password field");
                            return false;
                        }

                        passwordField = _webDriver.FindElement(By.Id("password"));
                        if (!passwordField.Displayed)
                        {
                            passwordField = _webDriver.FindElement(By.Name("password"));
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        LogDebug("Password field not found");
                    }

                    var buttonSelectors = new[]
                    {
                "//button[@type='submit']",
                "//button[contains(text(), 'Logg inn')]",
                "//input[@value='Logg inn']"
            };

                    foreach (var selector in buttonSelectors)
                    {

                        try
                        {
                            var button = _webDriver?.FindElement(By.XPath(selector));
                            if (button != null && button.Displayed && button.Enabled)
                            {
                                loginButton = button;
                                break;
                            }
                        }
                        catch (NoSuchElementException)
                        {
                            loginButton = null;
                        }
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    LogDebug("Login form not found within timeout");
                    return false;
                }

                if (usernameField != null && passwordField != null)
                {
                    LogDebug("Fast form filling...");

                    try
                    {
                        usernameField.Clear();
                        usernameField.SendKeys(_loginEmail);

                        passwordField.Clear();
                        passwordField.SendKeys(_loginPassword);

                        await Task.Delay(200); 

                        if (loginButton != null)
                        {
                            LogDebug("Clicking login button...");
                            loginButton.Click();
                        }
                        else
                        {
                            LogDebug("Using Enter key...");
                            passwordField.SendKeys(Keys.Enter);
                        }

                        LogDebug("Form submitted - checking for success...");

                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(200);



                            var currentUrl = _webDriver?.Url;
                            if (string.IsNullOrEmpty(currentUrl))
                            {
                                LogDebug("Current URL is null or empty, cannot verify login success");
                                return false;
                            }

                            if (currentUrl.Contains("isFeideinnlogget=true") ||
                                currentUrl.Contains("ojr=timeplan") ||
                                (!currentUrl.Contains("login") && !currentUrl.Contains("feide") && !currentUrl.Contains("org_selector")))
                            {
                                LogSuccess("Fast Feide login successful!");
                                return true;
                            }
                        }

                        var finalUrl = _webDriver?.Url;
                        if (string.IsNullOrEmpty(finalUrl))
                        {
                            LogDebug("Final URL is null or empty, cannot verify login success");
                            return false;
                        }

                        if (finalUrl.Contains("isFeideinnlogget=true") ||
                            finalUrl.Contains("ojr=timeplan") ||
                            (!finalUrl.Contains("login") && !finalUrl.Contains("feide") && !finalUrl.Contains("org_selector")))
                        {
                            LogSuccess("Fast Feide login successful!");
                            return true;
                        }
                        else
                        {
                            LogDebug("Fast login appears to have failed");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error during fast form submission: {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    LogDebug("Complete login form not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in fast Feide form handling: {ex.Message}");
                return false;
            }
        }
        private async Task<UserParameters?> QuickParameterCapture()
        {
            try
            {
                if (_webDriver == null || !IsWebDriverValid(_webDriver))
                    return null;
                LogDebug("Fanger parametere fra nettverkstrafikk...");

                var jsExecutor = (IJavaScriptExecutor)_webDriver;

                var result = jsExecutor.ExecuteScript(@"
            try {
                var entries = performance.getEntries();
                
                for (var i = 0; i < entries.length; i++) {
                    var entry = entries[i];
                    if (entry.name && 
                        (entry.name.includes('VoTimeplan_elev') || entry.name.includes('RESTFilter')) &&
                        entry.name.includes('fylkeid=')) {
                        
                        console.log('Found network request:', entry.name);
                        
                        // Extract parameters from the URL
                        var match = entry.name.match(/fylkeid=([^,&]+)[^,]*,planperi=([^,&]+)[^,]*,skoleid=([^,&]+)/);
                        if (match && match.length >= 4) {
                            return {
                                fylkeid: match[1],
                                planperi: match[2], 
                                skoleid: match[3],
                                source: 'network'
                            };
                        }
                    }
                }
                
                return { waiting: true };
                
            } catch (e) {
                console.error('Error capturing parameters:', e);
                return null;
            }
        ");

                for (int attempt = 0; attempt < 5 && result is Dictionary<string, object> waiting && waiting.ContainsKey("waiting"); attempt++)
                {
                    LogDebug($"Venter på nettverkstrafikk... forsøk {attempt + 1}/5");
                    await Task.Delay(1000);

                    result = jsExecutor.ExecuteScript(@"
                try {
                    var entries = performance.getEntries();
                    for (var i = 0; i < entries.length; i++) {
                        var entry = entries[i];
                        if (entry.name && 
                            (entry.name.includes('VoTimeplan_elev') || entry.name.includes('RESTFilter')) &&
                            entry.name.includes('fylkeid=')) {
                            
                            var match = entry.name.match(/fylkeid=([^,&]+)[^,]*,planperi=([^,&]+)[^,]*,skoleid=([^,&]+)/);
                            if (match && match.length >= 4) {
                                return {
                                    fylkeid: match[1],
                                    planperi: match[2], 
                                    skoleid: match[3],
                                    source: 'network'
                                };
                            }
                        }
                    }
                    return null;
                } catch (e) {
                    return null;
                }
            ");
                }

                if (result is Dictionary<string, object> resultDict &&
                    resultDict.ContainsKey("fylkeid") &&
                    resultDict.ContainsKey("planperi") &&
                    resultDict.ContainsKey("skoleid"))
                {
                    var parameters = new UserParameters
                    {
                        FylkeId = resultDict["fylkeid"]?.ToString(),
                        PlanPeri = resultDict["planperi"]?.ToString(),
                        SkoleId = resultDict["skoleid"]?.ToString()
                    };

                    LogSuccess($"Fanget parametere fra nettverkstrafikk: fylkeid={parameters.FylkeId}, planperi={parameters.PlanPeri}, skoleid={parameters.SkoleId}");

                    await SaveParametersAsync(parameters);

                    _userParameters = parameters;

                    return parameters;
                }
                else
                {
                    LogDebug("Ingen nettverkstrafikk funnet med parametere");
                    return null!;
                }
            }
            catch (Exception ex)
            {
                LogError($"Parameter capture feilet: {ex.Message}");
                return null!;
            }
        }

        private string GetCurrentSchoolYear()
        {
            var now = DateTime.Now;
            var currentYear = now.Year;

            
            var schoolYearStart = now.Month >= 8 ? currentYear : currentYear - 1;
            var schoolYearEnd = schoolYearStart + 1;

            return $"{schoolYearStart}-{schoolYearEnd.ToString().Substring(2)}";
        }

        private async Task<bool> WaitForTargetUrlAsync()
        {
            var timeout = DateTime.Now.AddMinutes(10);
            // var targetUrl = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan";
            int checkCount = 0;

            while (DateTime.Now < timeout && !_cancellationTokenSource?.Token.IsCancellationRequested == true)
            {
                try
                {
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        LogInfo("Automatisering ble stoppet under venting på innlogging");
                        return false;
                    }

                    if (_webDriver == null || !IsWebDriverValid(_webDriver))
                    {
                        LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                        ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                        return false;
                    }

                    checkCount++;
                    var currentUrl = _webDriver.Url;

                    if (checkCount % 15 == 0) 
                    {
                        var elapsed = checkCount * 2;
                        LogInfo($"Venter på innlogging... ({elapsed} sekunder)");
                    }

                    if (currentUrl.Contains("isFeideinnlogget=true") && currentUrl.Contains("ojr=timeplan"))
                    {
                        LogDebug($"Target URL reached after {checkCount * 2} seconds");
                        return true;
                    }
                }
                catch (WebDriverException webEx) when (webEx.Message.Contains("no such window") ||
                                                    webEx.Message.Contains("target window already closed") ||
                                                    webEx.Message.Contains("disconnected"))
                {
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                    ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return false;
                }
                catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                            invEx.Message.Contains("no such session"))
                {
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                    ShowNotification("Automatisering stoppet", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return false;
                }
                catch (Exception ex)
                {
                    LogDebug($"Error checking URL: {ex.Message}");

                    if (_webDriver == null || !IsWebDriverValid(_webDriver))
                    {
                        LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                        ShowNotification(
                            "Automatisering stoppet",
                            "Innlogging avbrutt av bruker - automatisering stoppet",
                            "WARNING"
                        );
                        return false;
                    }
                }

                await Task.Delay(2000);
            }

            LogError("Automatisering stoppet - innlogging tidsavbrudd");
            ShowNotification("Automatisering stoppet", "Innlogging tidsavbrudd - automatisering stoppet", "ERROR");
            return false;
        }

        private bool IsWebDriverValid(IWebDriver driver)
        {
            if (driver == null) return false;

            try
            {
                var _ = driver.CurrentWindowHandle;
                return true;
            }
            catch (WebDriverException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task CleanupWebDriverAsync(IWebDriver driverToCleanup)
        {
            try
            {
                LogInfo("Rydder opp nettleser ressurser...");

                if (driverToCleanup != null)
                {
                    try
                    {
                        if (IsWebDriverValid(driverToCleanup))
                        {
                            LogDebug("Lukker nettleser...");
                            driverToCleanup.Quit();
                        }
                        else
                        {
                            LogDebug("Nettleser allerede lukket av bruker");
                        }
                    }
                    catch (Exception quitEx)
                    {
                        LogDebug($"Feil ved lukking av nettleser: {quitEx.Message}");
                    }
                    finally
                    {
                        try
                        {
                            driverToCleanup.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            LogDebug($"Feil ved dispose av nettleser: {disposeEx.Message}");
                        }
                    }
                }

                _webDriver = null;
                LogDebug("Nettleser opprydding fullført");
            }
            catch (Exception ex)
            {
                LogDebug($"Feil under opprydding av nettleser: {ex.Message}");

                _webDriver = null;
            }

            await Task.Delay(500);
        }

        private Task ForceStopAutomationAsync()
        {
            try
            {
                LogInfo("Stopper automatisering på grunn av nettleser problem...");

                if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }

                IsAutomationRunning = false;
                StatusMessage = "Automatisering stoppet - nettleser ble lukket";

                LogInfo("Automatisering stoppet - klar for ny start");
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved tvungen stopp av automatisering: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private async Task SaveCookiesAsync(Cookie[] cookies)
        {
            try
            {
                string cookiesPath = GetCookiesFilePath();
                
                var json = JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(cookiesPath, json);
                
                LogDebug($"Cookies saved to: {cookiesPath}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to save cookies: {ex.Message}");
                throw;
            }
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
                ShowNotification("Ingen STUDIE-økter funnet for i dag",
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
                ShowNotification("Ingen gyldige STU-økter",
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
                                        ShowNotification("Registrering vellykket",
                                            $"Registrert for STU {stuSession.StartKl}-{stuSession.SluttKl}", "SUCCESS");

                                        registeredSessions.Add(sessionKey);
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

                        if (registeredSessions.Count > 0)
                        {
                            ShowNotification("Alle Studietimer Registrert",
                                $"Alle {validStuSessions.Count} gyldige STU-økter er fullført og registrert!", "SUCCESS");
                        }
                        else
                        {
                            ShowNotification("Ingen Flere Økter",
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
                    LogInfo("Henter brukerparametere...");
                    _userParameters = await ExtractUserParametersAsync(cookies);
                    
                    if (_userParameters == null || !_userParameters.IsComplete)
                    {
                        LogError("Kunne ikke få brukerparametere - bruker fallback verdier");
                        _userParameters = new UserParameters
                        {
                            FylkeId = "00",
                            PlanPeri = "2025-26", 
                            SkoleId = "312"
                        };
                    }
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

        

       public class UserParameters
        {
            public string? FylkeId { get; set; }
            public string? PlanPeri { get; set; }
            public string? SkoleId { get; set; }
            
            public bool IsComplete => !string.IsNullOrEmpty(FylkeId) && 
                                      !string.IsNullOrEmpty(PlanPeri) && 
                                      !string.IsNullOrEmpty(SkoleId);
        }

        private UserParameters? _userParameters;

        private async Task<UserParameters> ExtractUserParametersAsync(Dictionary<string, string> cookies)
        {
            try
            {
                LogDebug("Sjekker cached parametere...");

                var cachedParams = await LoadValidParametersAsync();
                if (cachedParams != null && cachedParams.IsComplete)
                {
                    LogInfo($"Bruker gyldige cached parametere for skoleår {cachedParams.PlanPeri}");
                    return cachedParams;
                }

                if (_webDriver != null && IsWebDriverValid(_webDriver))
                {
                    LogInfo("Ingen gyldige cached parametere - utvinner fra nettleser...");
                    var extractedParams = await QuickParameterCapture();

                    if (extractedParams != null && extractedParams.IsComplete)
                    {
                        LogSuccess($"Fant nye parametere fra nettleser for skoleår {extractedParams.PlanPeri} - lagrer");
                        await SaveParametersAsync(extractedParams);
                        return extractedParams;
                    }
                }

                LogError("Kunne ikke finne gyldige parametere - fallback ikke tillatt");
                LogError("Sletter cookies for å tvinge ny innlogging med parameterinnhenting");

                await DeleteCookiesAsync();

                throw new InvalidOperationException("Parameters required - forcing fresh login");
            }
            catch (InvalidOperationException)
            {
                throw; 
            }
            catch (Exception ex)
            {
                LogError($"Kritisk feil ved parameterhåndtering: {ex.Message}");
                LogError("Tvinger ny innlogging for å sikre korrekte parametere");
                await DeleteCookiesAsync();
                throw new InvalidOperationException("Parameters required - forcing fresh login");
            }
        }

        private Task DeleteCookiesAsync()
        {
            try
            {
                var cookiesPath = GetCookiesFilePath();
                if (File.Exists(cookiesPath))
                {
                    File.Delete(cookiesPath);
                    LogDebug("Slettet cookies for å tvinge ny innlogging");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Kunne ikke slette cookies: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private string GetUserParametersFilePath()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "user_parameters.json");
        }

        private async Task<UserParameters?> LoadValidParametersAsync()
        {
            try
            {
                var filePath = GetUserParametersFilePath();
                if (!File.Exists(filePath))
                {
                    LogDebug("Ingen lagrede parametere funnet");
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var savedData = JsonSerializer.Deserialize<SavedParameterData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (savedData?.Parameters == null)
                {
                    LogDebug("Ugyldig parameterdata");
                    return null;
                }

                var age = DateTime.Now - savedData.SavedAt;

                if (savedData.Parameters.PlanPeri != null &&
    !IsCurrentSchoolYear(savedData.Parameters.PlanPeri))
                {
                    LogInfo($"Lagrede parametere er for gammelt skoleår ({savedData.Parameters.PlanPeri}) - trenger oppdatering");
                    File.Delete(filePath);
                    return null;
                }

                LogSuccess($"Lastet gyldige parametere fra cache (alder: {age.TotalDays:F0} dager, skoleår: {savedData.Parameters.PlanPeri})");
                LogDebug($"Parametere: fylkeid={savedData.Parameters.FylkeId}, planperi={savedData.Parameters.PlanPeri}, skoleid={savedData.Parameters.SkoleId}");
                return savedData.Parameters;
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved lasting av parametere: {ex.Message}");
                return null!;
            }
        }

        private bool IsCurrentSchoolYear(string planPeri)
        {
            try
            {
                var currentSchoolYear = GetCurrentSchoolYear();
                return planPeri == currentSchoolYear;
            }
            catch
            {
                return false;
            }
        }

        private async Task SaveParametersAsync(UserParameters parameters)
        {
            try
            {
                if (parameters == null || !parameters.IsComplete)
                {
                    LogDebug("Kan ikke lagre ufullstendige parametere");
                    return;
                }

                var currentSchoolYear = GetCurrentSchoolYear();
                if (parameters.PlanPeri != currentSchoolYear)
                {
                    LogInfo($"ADVARSEL: Lagrer parametere for skoleår {parameters.PlanPeri}, men nåværende skoleår er {currentSchoolYear}");
                    LogInfo("Dette kan være normalt hvis du tester på slutten/begynnelsen av skoleåret");
                }

                var saveData = new SavedParameterData
                {
                    Parameters = parameters,
                    SavedAt = DateTime.Now,
                    SchoolYear = parameters.PlanPeri
                };

                var json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(GetUserParametersFilePath(), json);

                LogSuccess($"Lagret parametere for skoleår {parameters.PlanPeri} med timestamp: {saveData.SavedAt}");
            }
            catch (Exception ex)
            {
                LogDebug($"Kunne ikke lagre parametere: {ex.Message}");
            }
        }

        public class SavedParameterData
        {
            public UserParameters? Parameters { get; set; }
            public DateTime SavedAt { get; set; }
            public string? SchoolYear { get; set; } 
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

                    ShowNotification(
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

        private async void CheckForUpdatesAutomatically(object? state)
        {
            try
            {
                if ((DateTime.Now - _lastUpdateCheck).TotalMinutes < 25)
                {
                    LogDebug("Hopper over oppdateringssjekk - for tidlig siden forrige sjekk");
                    return;
                }

                _lastUpdateCheck = DateTime.Now;
                LogDebug($"Sjekker automatisk etter oppdateringer fra {UPDATE_JSON_URL}");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync(UPDATE_JSON_URL, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    LogDebug($"Kunne ikke hente oppdateringsinfo: {response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.latest_version))
                {
                    LogDebug("Ugyldig oppdateringsinfo mottatt");
                    return;
                }

                var latestVersion = updateInfo.latest_version.TrimStart('v');
                var currentVersion = _applicationInfo.Version.TrimStart('v');

                LogDebug($"Nåværende versjon: {currentVersion}, Siste versjon: {latestVersion}");

                if (IsNewerVersion(latestVersion, currentVersion))
                {
                    LogSuccess($"Ny versjon tilgjengelig: v{latestVersion}");

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ShowUpdateNotification(updateInfo);
                    });
                }
                else
                {
                    LogDebug("Ingen nye oppdateringer tilgjengelig");
                }
            }
            catch (TaskCanceledException)
            {
                LogDebug("Oppdateringssjekk tidsavbrudd - hopper over");
            }
            catch (HttpRequestException ex)
            {
                LogDebug($"Nettverksfeil ved oppdateringssjekk: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved automatisk oppdateringssjekk: {ex.Message}");
            }
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
            var title = "[ADMIN]Ny Oppdatering Tilgjengelig!";
            var message = $"Versjon {updateInfo.latest_version} er tilgjengelig!\n\n";

            LogInfo($"Viser oppdateringsvarsel: {updateInfo.latest_version}");
            ShowNotification(title, message, "INFO");
        }

        public void Dispose()
        {
            LogInfo("Disposing notification resources...");
            
            _updateCheckTimer?.Dispose();

            _isProcessingQueue = false;

            lock (_activeOverlayWindows)
            {
                foreach (var window in _activeOverlayWindows.ToList())
                {
                    try
                    {
                        if (window.IsVisible)
                        {
                            window.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error closing window during disposal: {ex.Message}");
                    }
                }
                _activeOverlayWindows.Clear();
            }

            _adminNotificationTimer?.Dispose();
            _isProcessingQueue = false;
            _notificationSemaphore?.Dispose();
            _cancellationTokenSource?.Cancel();
            _webDriver?.Quit();
            _webDriver?.Dispose();
            _httpClient?.Dispose();
        }
    }

    public class Cookie
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
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