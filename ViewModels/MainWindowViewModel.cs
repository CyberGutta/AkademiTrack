using AkademiTrack.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
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
    // Overlay notification window class
    public class NotificationOverlayWindow : Window
    {
        private Timer? _autoCloseTimer;
        private readonly string _level;

        public NotificationOverlayWindow(string title, string message, string level = "INFO", string imageUrl = null, string customColor = null)
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

        private void CreateModernContent(string title, string message, string level, string imageUrl = null, string customColor = null)
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

            // BACK TO YOUR ORIGINAL GRID - NO CHANGES
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
                Margin = new Thickness(0, 4, 0, 0), // BIGGER MARGIN TO PUSH TEXT DOWN
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

                    // Position from top-right
                    var x = (int)(workingArea.Right - windowWidth - margin);
                    var y = (int)(workingArea.Y + margin);

                    // Ensure bounds
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

        private void AutoClose(object state)
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
        public string Message { get; set; }
        public string Level { get; set; } // INFO, SUCCESS, ERROR, DEBUG
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }

    public class NotificationEntry
    {
        public DateTime Timestamp { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Level { get; set; } // INFO, SUCCESS, ERROR, WARNING
        public bool IsVisible { get; set; } = true;
        public int Id { get; set; }
    }

    public class SimpleCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !_isExecuting && _canExecute();

        public async void Execute(object parameter)
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
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isAutomationRunning;
        private string _statusMessage = "Ready";
        private IWebDriver _webDriver;
        private ObservableCollection<LogEntry> _logEntries;
        private ObservableCollection<NotificationEntry> _notifications;
        private bool _showDetailedLogs = true;
        private NotificationEntry _currentNotification;
        private int _notificationIdCounter = 0;
        private readonly List<NotificationOverlayWindow> _activeOverlayWindows = new();

        // New fields for cached schedule data
        private List<ScheduleItem> _cachedScheduleData;
        private DateTime _scheduleDataFetchTime;

        private Timer _adminNotificationTimer;
        private HashSet<string> _processedNotificationIds = new HashSet<string>();
        private string _processedNotificationsFile;

        public event PropertyChangedEventHandler PropertyChanged;

        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co"; // Replace with your actual URL
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k"; // Replace with your actual anon key
        private string _userEmail = "TESTGMAIL"; // Replace with actual user email
        private string _userPassword = "TESTPASSWORD";

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {
            _httpClient = new HttpClient();
            _logEntries = new ObservableCollection<LogEntry>();
            _notifications = new ObservableCollection<NotificationEntry>();
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            ClearLogsCommand = new SimpleCommand(ClearLogsAsync);
            ToggleDetailedLogsCommand = new SimpleCommand(ToggleDetailedLogsAsync);
            DismissNotificationCommand = new SimpleCommand(DismissCurrentNotificationAsync);


            LogInfo("Applikasjon er klar");

            _processedNotificationsFile = Path.Combine(
            Path.GetDirectoryName(GetCookiesFilePath()),
            "processed_notifications.json"
            );

            // Load previously processed notifications
            _ = Task.Run(LoadProcessedNotificationIdsAsync);

            // Start checking for admin notifications every 30 seconds
            _adminNotificationTimer = new Timer(CheckForAdminNotifications, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

            LogInfo("Admin notification system initialized");
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
            catch (Exception ex)
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
            catch (Exception ex)
            {
            }
        }

        

        private async void CheckForAdminNotifications(object state)
        {
            try
            {
                await CheckForNewAdminNotificationsAsync();
            }
            catch (Exception ex)
            {
            }
        }

        // Method to fetch and display new admin notifications
        private async Task CheckForNewAdminNotificationsAsync()
        {
            try
            {
                var userEmail = await GetUserEmailFromActivationAsync();
                if (string.IsNullOrEmpty(userEmail))
                {
                    LogDebug("No user email found for admin notifications");
                    return;
                }

                var since = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var url = $"{_supabaseUrl}/rest/v1/admin_notifications?or=(target_email.eq.{userEmail},target_email.eq.all)&created_at=gte.{since}&order=created_at.desc&limit=20";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    var notifications = JsonSerializer.Deserialize<EnhancedAdminNotification[]>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (notifications != null)
                    {
                        bool hasNewNotifications = false;

                        foreach (var notification in notifications)
                        {
                            if (!_processedNotificationIds.Contains(notification.Id))
                            {
                                _processedNotificationIds.Add(notification.Id);
                                hasNewNotifications = true;

                                var notificationLevel = notification.Priority?.ToUpper() switch
                                {
                                    "HIGH" => "ERROR",
                                    "MEDIUM" => "WARNING",
                                    "LOW" => "SUCCESS",
                                    _ => "INFO"
                                };

                                // Fixed: Use proper admin title format
                                var adminTitle = $"[ADMIN]{notification.Title}";

                                // Show notification with enhanced features
                                ShowSystemOverlayNotification(adminTitle, notification.Message, notificationLevel,
                                    notification.Image_Url, notification.Custom_Color);

                                LogInfo($"Enhanced admin notification displayed: {notification.Title}");
                            }
                        }

                        if (hasNewNotifications)
                        {
                            await SaveProcessedNotificationIdsAsync();
                        }
                    }
                }
                else
                {
                    LogDebug($"Failed to fetch admin notifications: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"Error response: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in CheckForNewAdminNotificationsAsync: {ex.Message}");
            }
        }

        public class EnhancedAdminNotification
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public string Priority { get; set; }
            public string Target_Email { get; set; }
            public string Image_Url { get; set; }    // New: URL to image
            public string Custom_Color { get; set; }  // New: Custom hex color
            public DateTime Created_At { get; set; }
        }

        // Method to mark notification as delivered
        

        public class AdminNotification
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public string Priority { get; set; }
            public string Target_Email { get; set; }
            public DateTime Created_At { get; set; }
        }

        public class AdminNotificationWithDelivery : AdminNotification
        {
            public List<NotificationDelivery> Notification_Deliveries { get; set; }
        }

        public class NotificationDelivery
        {
            public string User_Email { get; set; }
            public string Status { get; set; }
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

        public NotificationEntry CurrentNotification
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
        public ICommand ClearLogsCommand { get; }
        public ICommand ToggleDetailedLogsCommand { get; }
        public ICommand DismissNotificationCommand { get; }

        public ICommand TestSupabaseCommand { get; }


        private void ShowNotification(string title, string message, string level = "INFO")
        {
            // Allow these specific system notifications OR any notification marked as admin
            var allowedNotifications = new[]
            {
                "Automation Started",
                "Automation Stopped",
                "Registration Success",
                "Alle Studietimer Registrert",
                "Ingen STUDIE-økter funnet for i dag", // Add this new title
                "Ingen Flere Økter"     // Alternative title
            };

            // Check if this is an admin notification - handle both correct and malformed formats
            bool isAdminNotification = title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN[");

            if (allowedNotifications.Contains(title) || isAdminNotification)
            {
                LogDebug($"Showing enhanced system overlay notification: {title} - {message}");
                ShowSystemOverlayNotification(title, message, level);
            }
            else
            {
                LogDebug($"Notification filtered out: {title}");
            }
        }

        private void ShowSystemOverlayNotification(string title, string message, string level, string imageUrl = null, string customColor = null)
        {
            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    CreateOverlayWindow(title, message, level, imageUrl, customColor);
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            CreateOverlayWindow(title, message, level, imageUrl, customColor);
                        }
                        catch (Exception innerEx)
                        {
                            LogDebug($"Failed to create overlay window on UI thread: {innerEx.Message}");
                            Task.Delay(1000).ContinueWith(_ =>
                            {
                                try
                                {
                                    Dispatcher.UIThread.Post(() => CreateOverlayWindow(title, message, level, imageUrl, customColor));
                                }
                                catch
                                {
                                    LogError($"Complete failure to show notification: {title}");
                                }
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to show system overlay notification: {ex.Message}");
                LogInfo($"NOTIFICATION (fallback): {title} - {message}");
            }
        }

        private void CreateOverlayWindow(string title, string message, string level, string imageUrl = null, string customColor = null)
        {
            try
            {
                try
                {
                    for (int i = _activeOverlayWindows.Count - 1; i >= 0; i--)
                    {
                        if (!_activeOverlayWindows[i].IsVisible)
                        {
                            _activeOverlayWindows.RemoveAt(i);
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    LogDebug($"Error during window cleanup: {cleanupEx.Message}");
                    _activeOverlayWindows.Clear();
                }

                bool isRegistrationSuccess = title == "Registration Success";
                bool isPriorityNotification = title == "Ingen Flere Økter" || title.Contains("Ingen STUDIE-økter");

                if (!isRegistrationSuccess && !isPriorityNotification && _activeOverlayWindows.Count > 0)
                {
                    LogDebug($"Skipping notification '{title}' - another notification is already showing");
                    return;
                }

                LogDebug($"Creating overlay window for: {title}");

                NotificationOverlayWindow overlayWindow = null;
                try
                {
                    if (!string.IsNullOrEmpty(imageUrl) || !string.IsNullOrEmpty(customColor))
                    {
                        overlayWindow = new NotificationOverlayWindow(title, message, level, imageUrl, customColor);
                    }
                    else
                    {
                        overlayWindow = new NotificationOverlayWindow(title, message, level);
                    }
                }
                catch (Exception createEx)
                {
                    LogError($"Failed to create notification window: {createEx.Message}");
                    LogInfo($"NOTIFICATION (fallback): {title} - {message}");
                    return;
                }

                overlayWindow.Closed += (s, e) =>
                {
                    try
                    {
                        _activeOverlayWindows.Remove(overlayWindow);
                        LogDebug($"Notification window closed: {title}");
                    }
                    catch (Exception removeEx)
                    {
                        LogDebug($"Error removing closed window: {removeEx.Message}");
                    }
                };

                _activeOverlayWindows.Add(overlayWindow);

                try
                {
                    overlayWindow.Show();
                    LogDebug($"✓ Notification window shown successfully: {title}");
                }
                catch (Exception showEx)
                {
                    LogError($"Failed to show notification window: {showEx.Message}");
                    _activeOverlayWindows.Remove(overlayWindow);
                    LogInfo($"NOTIFICATION (fallback): {title} - {message}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Complete failure in CreateOverlayWindow: {ex.Message}");
                LogInfo($"NOTIFICATION (emergency fallback): {title} - {message}");
            }
        }

        private async Task DismissCurrentNotificationAsync()
        {
            CurrentNotification = null;
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
                StatusMessage = logEntry.FormattedMessage;

                // Keep only last 100 entries to prevent memory issues
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
                    StatusMessage = logEntry.FormattedMessage;

                    while (LogEntries.Count > 100)
                    {
                        LogEntries.RemoveAt(0);
                    }
                });
            }
        }

        private async Task ClearLogsAsync()
        {
            LogEntries.Clear();
            LogInfo("Logger tømt");
        }

        private async Task ToggleDetailedLogsAsync()
        {
            ShowDetailedLogs = !ShowDetailedLogs;
            LogInfo($"Detaljert logging {(ShowDetailedLogs ? "aktivert" : "deaktivert")}");
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning) return;

            IsAutomationRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                LogInfo("Starter automatisering...");
                ShowNotification("Automation Started", "STU tidsregistrering automatisering kjører nå", "SUCCESS");

                // Step 1: Check if existing cookies work
                LogDebug("Laster eksisterende cookies fra fil...");
                var cookies = await LoadCookiesAsync();
                bool cookiesValid = false;

                if (cookies != null)
                {
                    LogInfo($"Fant {cookies.Count} eksisterende cookies, tester gyldighet...");
                    cookiesValid = await TestCookiesAsync(cookies);

                    if (cookiesValid)
                    {
                        LogSuccess("Eksisterende cookies er gyldige!");
                    }
                    else
                    {
                        LogInfo("Eksisterende cookies er ugyldige eller utløpt");
                    }
                }
                else
                {
                    LogInfo("Ingen eksisterende cookies funnet");
                }

                // Step 2: If cookies don't work, get new ones via browser login
                if (!cookiesValid)
                {
                    LogInfo("Åpner nettleser for ny innlogging...");

                    cookies = await GetCookiesViaBrowserAsync();

                    if (cookies == null)
                    {
                        LogError("Kunne ikke få cookies fra nettleser innlogging");
                        return;
                    }

                    LogSuccess($"Fikk {cookies.Count} nye cookies");
                }

                LogSuccess("Autentisering fullført - starter overvåkingssløyfe...");

                // Step 3: Start the monitoring loop with cached data
                await RunMonitoringLoopAsync(_cancellationTokenSource.Token, cookies);
            }
            catch (OperationCanceledException)
            {
                LogInfo("Automatisering stoppet av bruker");
                ShowNotification("Automation Stopped", "Overvåking har blitt stoppet", "INFO");
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

        private async Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                LogInfo("Stopp forespurt - stopper automatisering...");
                ShowNotification("Automation Stopped", "Automatisering har blitt stoppet av bruker", "INFO");
            }
        }

        private async Task OpenSettingsAsync()
        {
            try
            {
                // Create and show the settings window
                var settingsWindow = new SettingsWindow();
                var settingsViewModel = new SettingsViewModel();

                // Pass the log entries and current settings
                settingsViewModel.SetLogEntries(this.LogEntries);
                settingsViewModel.ShowDetailedLogs = this.ShowDetailedLogs;

                settingsWindow.DataContext = settingsViewModel;

                // Handle events from settings view model
                settingsViewModel.CloseRequested += (s, e) => settingsWindow.Close();

                // Sync the ShowDetailedLogs setting back to the main view model
                settingsViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SettingsViewModel.ShowDetailedLogs))
                    {
                        this.ShowDetailedLogs = settingsViewModel.ShowDetailedLogs;
                    }
                };

                // Show as dialog
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    await settingsWindow.ShowDialog(desktop.MainWindow);
                }
                else
                {
                    settingsWindow.Show();
                }

                LogSuccess("Innstillinger vindu åpnet");
            }
            catch (Exception ex)
            {
                LogError($"Kunne ikke åpne innstillinger vindu: {ex.Message}");
            }
        }

        private string GetCookiesFilePath()
        {
            // Get the user's Application Support directory (best practice for macOS)
            string appSupportPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = Path.Combine(appSupportPath, "AkademiTrack");
            
            // Create directory if it doesn't exist
            Directory.CreateDirectory(appDataDir);
            
            // Return full path to cookies file
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
                    return null;
                }

                var json = await File.ReadAllTextAsync(cookiesPath);
                var cookieArray = JsonSerializer.Deserialize<Cookie[]>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug($"Loaded {cookieArray?.Length ?? 0} cookies from file: {cookiesPath}");
                return cookieArray?.ToDictionary(c => c.Name, c => c.Value);
            }
            catch (Exception ex)
            {
                LogError($"Failed to load cookies: {ex.Message}");
                return null;
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
                    LogDebug($"Cookie test successful - found {scheduleData.Items.Count} schedule items");
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
            IWebDriver localWebDriver = null;

            try
            {
                // Setup Chrome options
                var options = new ChromeOptions();
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--start-maximized");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    options.AddArgument("--disable-gpu");
                }

                LogInfo("Initialiserer Chrome nettleser...");
                localWebDriver = new ChromeDriver(options);
                _webDriver = localWebDriver; // Set the class field for disposal

                // Navigate to login page
                LogInfo("Navigerer til innloggingsside: https://iskole.net/elev/?ojr=login");
                _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                LogInfo("Vennligst fullfør innloggingsprosessen i nettleseren");
                LogInfo("Venter på navigering til: https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");

                // Wait for user to reach the target URL
                var targetReached = await WaitForTargetUrlAsync();

                if (!targetReached)
                {
                    LogError("Tidsavbrudd - innlogging ble ikke fullført innen 10 minutter");
                    return null;
                }

                LogSuccess("Innlogging fullført!");
                LogInfo("Ekstraherer cookies fra nettleser økten...");

                // Extract cookies - check if driver is still valid
                if (!IsWebDriverValid(_webDriver))
                {
                    LogError("Nettleser ble lukket under innlogging - kan ikke ekstraktere cookies");
                    return null;
                }

                var seleniumCookies = _webDriver.Manage().Cookies.AllCookies;
                var cookieDict = seleniumCookies.ToDictionary(c => c.Name, c => c.Value);

                LogDebug($"Ekstraherte cookies: {string.Join(", ", cookieDict.Keys)}");

                // Save cookies
                await SaveCookiesAsync(seleniumCookies.Select(c => new Cookie { Name = c.Name, Value = c.Value }).ToArray());

                LogSuccess($"Ekstraherte og lagret {cookieDict.Count} cookies");
                return cookieDict;
            }
            catch (WebDriverException webEx) when (webEx.Message.Contains("no such window") ||
                                                  webEx.Message.Contains("target window already closed") ||
                                                  webEx.Message.Contains("Session info: chrome") ||
                                                  webEx.Message.Contains("disconnected"))
            {
                LogError("Nettleser vindu ble lukket under innlogging - stopper automatisering");
                LogInfo("Automatisering vil bli stoppet - start på nytt for å prøve igjen");

                // Force stop the automation cleanly
                await ForceStopAutomationAsync();
                return null;
            }
            catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                         invEx.Message.Contains("no such session"))
            {
                LogError("Nettleser sesjon ble avbrutt under innlogging");
                LogInfo("Automatisering vil bli stoppet - start på nytt for å prøve igjen");

                // Force stop the automation cleanly
                await ForceStopAutomationAsync();
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Nettleser innlogging feilet: {ex.Message}");
                LogDebug($"Exception type: {ex.GetType().Name}");

                // Force stop automation for any other browser-related errors
                await ForceStopAutomationAsync();
                return null;
            }
            finally
            {
                // Always clean up the web driver, regardless of what happened
                await CleanupWebDriverAsync(localWebDriver);
            }
        }



        private async Task<bool> WaitForTargetUrlAsync()
        {
            var timeout = DateTime.Now.AddMinutes(10);
            var targetUrl = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan";
            int checkCount = 0;

            while (DateTime.Now < timeout && !_cancellationTokenSource?.Token.IsCancellationRequested == true)
            {
                try
                {
                    // Check if automation was cancelled
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        LogInfo("Automatisering ble stoppet under venting på innlogging");
                        return false;
                    }

                    // Check if web driver is still valid
                    if (!IsWebDriverValid(_webDriver))
                    {
                        LogError("Nettleser vindu ble lukket under venting på innlogging");
                        return false;
                    }

                    checkCount++;
                    var currentUrl = _webDriver.Url;

                    if (checkCount % 15 == 0) // Log every 30 seconds (15 * 2 second delay)
                    {
                        LogDebug($"Current URL: {currentUrl}");
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
                    LogError("Nettleser vindu ble lukket under venting");
                    return false;
                }
                catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                             invEx.Message.Contains("no such session"))
                {
                    LogError("Nettleser sesjon ble avbrutt under venting");
                    return false;
                }
                catch (Exception ex)
                {
                    LogDebug($"Error checking URL: {ex.Message}");

                    // If we can't check the URL, the browser might be closed
                    if (!IsWebDriverValid(_webDriver))
                    {
                        LogError("Nettleser ikke lenger tilgjengelig");
                        return false;
                    }
                }

                await Task.Delay(2000);
            }

            LogError("Tidsavbrudd nådd under venting på innlogging");
            return false;
        }

        private bool IsWebDriverValid(IWebDriver driver)
        {
            if (driver == null) return false;

            try
            {
                // Try to access a simple property to test if the driver is still connected
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

                // Clear the class field
                _webDriver = null;
                LogDebug("Nettleser opprydding fullført");
            }
            catch (Exception ex)
            {
                LogDebug($"Feil under opprydding av nettleser: {ex.Message}");
                // Always ensure the field is cleared
                _webDriver = null;
            }

            // Small delay to let cleanup complete
            await Task.Delay(500);
        }

        private async Task ForceStopAutomationAsync()
        {
            try
            {
                LogInfo("Stopper automatisering på grunn av nettleser problem...");

                // Cancel the automation token if it exists
                if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }

                // Update the UI state
                IsAutomationRunning = false;
                StatusMessage = "Automatisering stoppet - nettleser ble lukket";

                LogInfo("Automatisering stoppet - klar for ny start");
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved tvungen stopp av automatisering: {ex.Message}");
            }
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

        // OPTIMIZED MONITORING LOOP - SINGLE API CALL, TIME-BASED CHECKING
        private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken, Dictionary<string, string> cookies)
        {
            int cycleCount = 0;
            
            // Initial fetch of schedule data at startup - ONLY API CALL FOR THE DAY
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

            // Find all STU sessions for today using cached data
            var today = DateTime.Now.ToString("yyyyMMdd");
            var todaysStuSessions = _cachedScheduleData
                .Where(item => item.Dato == today && item.KNavn == "STU")
                .ToList();

            LogInfo($"Fant {todaysStuSessions.Count} STU-økter for i dag ({DateTime.Now:yyyy-MM-dd})");
            
            if (todaysStuSessions.Any())
            {
                foreach (var stuTime in todaysStuSessions)
                {
                    LogInfo($"STU økt: {stuTime.StartKl}-{stuTime.SluttKl}, Registreringsvindu: {stuTime.TidsromTilstedevaerelse}");
                }
            }
            else
            {
                LogInfo("Ingen STUDIE-økter funnet for i dag - stopper automatisering");
                ShowNotification("Ingen Flere Økter", "Ingen STUDIE-økter funnet for i dag", "INFO");
                return;
            }

            // Track which sessions have been successfully registered
            var registeredSessions = new HashSet<string>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    cycleCount++;
                    LogInfo($"Overvåkingssyklus #{cycleCount} - sjekker registreringsvinduer...");

                    bool allSessionsComplete = true;
                    int openWindows = 0;
                    int closedWindows = 0;
                    int notYetOpenWindows = 0;

                    // Check each STU session using cached data and current time - NO API CALLS
                    foreach (var stuSession in todaysStuSessions)
                    {
                        var sessionKey = $"{stuSession.StartKl}-{stuSession.SluttKl}";
                        
                        // Skip if already registered
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
                                    // ONLY API CALL DURING MONITORING - POST request for registration
                                    await RegisterAttendanceAsync(stuSession, cookies);
                                    LogSuccess($"Registrerte oppmøte for {stuSession.StartKl}-{stuSession.SluttKl}!");
                                    ShowNotification("Registration Success",
                                        $"Registrert for STU {stuSession.StartKl}-{stuSession.SluttKl}", "SUCCESS");
                                    
                                    // Mark as registered
                                    registeredSessions.Add(sessionKey);
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
                                var currentTime = DateTime.Now.ToString("HH:mm");
                                LogDebug($"Registreringsvindu lukket for {stuSession.StartKl}-{stuSession.SluttKl} (nåværende tid: {currentTime}, vindu: {stuSession.TidsromTilstedevaerelse})");
                                break;
                        }
                    }

                    // Check if all sessions are complete (either registered or windows closed)
                    if (allSessionsComplete || registeredSessions.Count == todaysStuSessions.Count)
                    {
                        LogSuccess($"Alle {todaysStuSessions.Count} STU-økter er håndtert for i dag!");
                        LogInfo($"Registrerte økter: {registeredSessions.Count}, Totalt: {todaysStuSessions.Count}");
                        ShowNotification("Alle Studietimer Registrert",
                            $"Alle {todaysStuSessions.Count} STU-økter er fullført.", "SUCCESS");
                        break;
                    }

                    // Log summary of current state
                    LogInfo($"Øktstatus: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} lukkede/registrerte");

                    // Wait 2 minutes before next time check (NO API CALL NEEDED)
                    LogInfo("Venter 2 minutter før neste tidssjekk...");
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Overvåkingsfeil: {ex.Message}");
                    LogInfo("Venter 1 minutt før nytt forsøk...");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }
        }

        // New method to get full day schedule data (called only once)
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
                return null;
            }
        }

        // Optional: Method to refresh schedule data if needed (for edge cases)
        private async Task RefreshScheduleDataIfNeeded(Dictionary<string, string> cookies)
        {
            // Only refresh if data is very old (e.g., more than 6 hours) or if it's a new day
            var dataAge = DateTime.Now - _scheduleDataFetchTime;
            var isNewDay = _scheduleDataFetchTime.Date != DateTime.Now.Date;
            
            if (dataAge.TotalHours > 6 || isNewDay)
            {
                LogInfo("Oppdaterer timeplandata...");
                var newData = await GetFullDayScheduleDataAsync(cookies);
                if (newData != null)
                {
                    _cachedScheduleData = newData;
                    _scheduleDataFetchTime = DateTime.Now;
                    LogSuccess("Timeplandata oppdatert");
                }
            }
        }

        // Add this enum for registration window status
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

            // Parse the registration time window (e.g., "08:15 - 08:30")
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

        // REMOVED: ShouldRegisterNow method (no longer needed as it's replaced by GetRegistrationWindowStatus)

        private async Task<ScheduleResponse> GetScheduleDataAsync(Dictionary<string, string> cookies)
        {
            try
            {
                // Ensure we have user parameters
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
                            SkoleId = "312" // Your original value as fallback
                        };
                    }
                }
                
                var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&offset=0&totalResults=true";

                LogDebug($"Making request with parameters: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");
                LogDebug($"Request URL: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add headers
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                // Add cookies
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
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                LogDebug($"Response received ({json.Length} characters)");

                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                LogDebug("JSON deserialized successfully");
                return scheduleResponse;
            }
            catch (Exception ex)
            {
                LogError($"Error getting schedule data: {ex.Message}");
                return null;
            }
        }

        


        private async Task<string> GetUserEmailFromActivationAsync()
        {
            try
            {
                string appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                string activationPath = System.IO.Path.Combine(appDataDir, "activation.json");

                if (!System.IO.File.Exists(activationPath))
                {
                    LogError("Activation file not found. Please log in first.");
                    return null;
                }

                string json = await System.IO.File.ReadAllTextAsync(activationPath);
                var activationData = JsonSerializer.Deserialize<JsonElement>(json);

                if (activationData.TryGetProperty("Email", out JsonElement emailElement))
                {
                    return emailElement.GetString();
                }

                LogError("Email not found in activation file.");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Failed to load email from activation file: {ex.Message}");
                return null;
            }
        }

       public class UserParameters
        {
            public string FylkeId { get; set; }
            public string PlanPeri { get; set; }
            public string SkoleId { get; set; }
            
            public bool IsComplete => !string.IsNullOrEmpty(FylkeId) && 
                                    !string.IsNullOrEmpty(PlanPeri) && 
                                    !string.IsNullOrEmpty(SkoleId);
        }

        // Add these fields to your MainWindowViewModel class
        private UserParameters _userParameters;

        // Add this method to extract parameters dynamically
        private async Task<UserParameters> ExtractUserParametersAsync(Dictionary<string, string> cookies)
        {
            try
            {
                LogInfo("Ekstraherer brukerparametere...");
                
                // First, try to extract parameters from browser if available
                if (IsWebDriverValid(_webDriver))
                {
                    var browserParams = await ExtractParametersFromBrowserAsync();
                    if (browserParams != null && browserParams.IsComplete)
                    {
                        LogSuccess("Fant parametere fra nettleser!");
                        await SaveUserParametersAsync(browserParams);
                        return browserParams;
                    }
                }
                
                // If that fails, check if we have saved parameters from before
                var savedParams = await LoadSavedParametersAsync();
                if (savedParams != null && savedParams.IsComplete)
                {
                    LogInfo("Bruker lagrede parametere fra tidligere økt");
                    return savedParams;
                }
                
                // If all else fails, try to make educated guesses
                LogInfo("Estimerer parametere basert på vanlige mønstre");
                return EstimateUserParameters();
            }
            catch (Exception ex)
            {
                LogError($"Kunne ikke ekstraktere brukerparametere: {ex.Message}");
                return null;
            }
        }

        private async Task<UserParameters> ExtractParametersFromBrowserAsync()
        {
            try
            {
                if (!IsWebDriverValid(_webDriver))
                {
                    return null;
                }
                
                LogInfo("VIKTIG: Gå til 'Fravær' siden i nettleseren for å la programmet finne dine skoleparametere");
                LogInfo("Nettleseren vil lukke automatisk når du er på Fravær siden og parametere er funnet");
                LogInfo("Venter på at du navigerer til Fravær siden...");
                
                var timeout = DateTime.Now.AddMinutes(5); // Give user more time
                
                while (DateTime.Now < timeout && IsWebDriverValid(_webDriver))
                {
                    try
                    {
                        var currentUrl = _webDriver.Url;
                        
                        // Only proceed when user is specifically on the Fravær page
                        if (currentUrl.Contains("ojr=fravar"))
                        {
                            LogInfo("Bruker er på Fravær siden - ekstraherer parametere...");
                            
                            // Wait for the page to fully load and make its API calls
                            await Task.Delay(4000);
                            
                            // Try multiple extraction methods
                            var parameters = await ExtractParametersFromPage();
                            
                            if (parameters != null && parameters.IsComplete)
                            {
                                LogSuccess($"Fant parametere: fylkeid={parameters.FylkeId}, planperi={parameters.PlanPeri}, skoleid={parameters.SkoleId}");
                                LogInfo("Lukker nettleser automatisk...");
                                
                                // Auto-close browser since we found the parameters
                                await Task.Delay(1000); // Brief pause to let user see the success message
                                return parameters;
                            }
                            else
                            {
                                LogInfo("Kunne ikke finne parametere på denne siden - oppdater siden eller prøv igjen");
                                await Task.Delay(3000);
                            }
                        }
                        else if (currentUrl.Contains("ojr=timeplan"))
                        {
                            LogInfo("Du er på Timeplan siden - gå til Fravær siden i stedet for best resultat");
                            await Task.Delay(3000);
                        }
                        else
                        {
                            // User is somewhere else, just wait
                            await Task.Delay(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Feil ved søking i nettleser: {ex.Message}");
                        await Task.Delay(2000);
                    }
                }
                
                LogInfo("Tidsavbrudd - kunne ikke finne parametere automatisk");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Feil ved parametersøking: {ex.Message}");
                return null;
            }
        }

        private async Task<UserParameters> ExtractParametersFromPage()
        {
            try
            {
                var jsExecutor = (IJavaScriptExecutor)_webDriver;
                
                // Enhanced JavaScript to search for parameters in multiple ways
                var script = @"
                    try {
                        var parameters = {};
                        
                        // Method 1: Search in page HTML content
                        var pageHtml = document.documentElement.outerHTML;
                        var fylkeMatch = pageHtml.match(/fylkeid[=:]['\""\s]*([0-9]+)/i);
                        var planMatch = pageHtml.match(/planperi[=:]['\""\s]*([0-9-]+)/i);
                        var skoleMatch = pageHtml.match(/skoleid[=:]['\""\s]*([0-9]+)/i);
                        
                        if (fylkeMatch && planMatch && skoleMatch) {
                            parameters.fylkeid = fylkeMatch[1];
                            parameters.planperi = planMatch[1];
                            parameters.skoleid = skoleMatch[1];
                            return parameters;
                        }
                        
                        // Method 2: Look for RESTFilter patterns
                        var restFilterMatch = pageHtml.match(/RESTFilter[^;]*;fylkeid=([0-9]+)[^,]*,planperi=([0-9-]+)[^,]*,skoleid=([0-9]+)/i);
                        if (restFilterMatch && restFilterMatch.length >= 4) {
                            parameters.fylkeid = restFilterMatch[1];
                            parameters.planperi = restFilterMatch[2];
                            parameters.skoleid = restFilterMatch[3];
                            return parameters;
                        }
                        
                        // Method 3: Check all script elements for these values
                        var scripts = document.querySelectorAll('script');
                        for (var i = 0; i < scripts.length; i++) {
                            var scriptContent = scripts[i].textContent || scripts[i].innerHTML;
                            var match = scriptContent.match(/fylkeid[=:]['\""\s]*([0-9]+).*?planperi[=:]['\""\s]*([0-9-]+).*?skoleid[=:]['\""\s]*([0-9]+)/i);
                            if (match && match.length >= 4) {
                                parameters.fylkeid = match[1];
                                parameters.planperi = match[2];
                                parameters.skoleid = match[3];
                                return parameters;
                            }
                        }
                        
                        // Method 4: Check for individual parameters separately
                        var fylkeFound = pageHtml.match(/fylkeid[=:]['\""\s]*([0-9]+)/i);
                        var planFound = pageHtml.match(/planperi[=:]['\""\s]*([0-9-]+)/i);
                        var skoleFound = pageHtml.match(/skoleid[=:]['\""\s]*([0-9]+)/i);
                        
                        if (fylkeFound) parameters.fylkeid = fylkeFound[1];
                        if (planFound) parameters.planperi = planFound[1];
                        if (skoleFound) parameters.skoleid = skoleFound[1];
                        
                        return Object.keys(parameters).length > 0 ? parameters : null;
                        
                    } catch (e) {
                        console.error('Parameter extraction error:', e);
                        return null;
                    }
                ";
                
                var result = jsExecutor.ExecuteScript(script);
                
                if (result is Dictionary<string, object> resultDict && resultDict.Count > 0)
                {
                    LogDebug($"Fant data i siden: {string.Join(", ", resultDict.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    
                    var parameters = new UserParameters();
                    
                    if (resultDict.ContainsKey("fylkeid"))
                        parameters.FylkeId = resultDict["fylkeid"].ToString();
                    if (resultDict.ContainsKey("planperi"))
                        parameters.PlanPeri = resultDict["planperi"].ToString();
                    if (resultDict.ContainsKey("skoleid"))
                        parameters.SkoleId = resultDict["skoleid"].ToString();
                    
                    // Return even if not complete - we'll use fallbacks for missing values
                    if (!string.IsNullOrEmpty(parameters.SkoleId) || !string.IsNullOrEmpty(parameters.PlanPeri))
                    {
                        // Fill in missing values with educated guesses
                        if (string.IsNullOrEmpty(parameters.FylkeId))
                            parameters.FylkeId = "00";
                        
                        if (string.IsNullOrEmpty(parameters.PlanPeri))
                        {
                            var currentYear = DateTime.Now.Year;
                            var schoolYearStart = DateTime.Now.Month >= 8 ? currentYear : currentYear - 1;
                            parameters.PlanPeri = $"{schoolYearStart}-{(schoolYearStart + 1).ToString().Substring(2)}";
                        }
                        
                        return parameters;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved utvinning av parametere: {ex.Message}");
                return null;
            }
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

        private async Task SaveUserParametersAsync(UserParameters parameters)
        {
            try
            {
                var json = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(GetUserParametersFilePath(), json);
                LogDebug("Brukerparametere lagret for fremtidig bruk");
            }
            catch (Exception ex)
            {
                LogDebug($"Kunne ikke lagre brukerparametere: {ex.Message}");
            }
        }

        private async Task<UserParameters> LoadSavedParametersAsync()
        {
            try
            {
                var filePath = GetUserParametersFilePath();
                if (!File.Exists(filePath))
                {
                    return null;
                }
                
                var json = await File.ReadAllTextAsync(filePath);
                var parameters = JsonSerializer.Deserialize<UserParameters>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (parameters != null && parameters.IsComplete)
                {
                    LogDebug($"Lastet lagrede parametere: fylkeid={parameters.FylkeId}, planperi={parameters.PlanPeri}, skoleid={parameters.SkoleId}");
                    return parameters;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Kunne ikke laste lagrede parametere: {ex.Message}");
            }
            
            return null;
        }

        private UserParameters EstimateUserParameters()
        {
            // Estimate current school year
            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;
            
            // School year typically starts in August/September
            var schoolYearStart = currentMonth >= 8 ? currentYear : currentYear - 1;
            var schoolYearEnd = schoolYearStart + 1;
            
            var parameters = new UserParameters
            {
                FylkeId = "00", // Most common for Norwegian schools
                PlanPeri = $"{schoolYearStart}-{schoolYearEnd.ToString().Substring(2)}", // e.g., "2024-25"
                SkoleId = "312" // Use your original value as fallback
            };
            
            LogInfo($"Estimerte parametere: fylkeid={parameters.FylkeId}, planperi={parameters.PlanPeri}, skoleid={parameters.SkoleId}");
            LogInfo("Bruker estimerte verdier. Hvis programmet ikke fungerer, kan du finne dine parametere manuelt:");
            LogInfo("1. Gå til timeplan siden i nettleseren");  
            LogInfo("2. Åpne Developer Tools (F12)");
            LogInfo("3. Gå til Network tab");
            LogInfo("4. Oppdater siden");
            LogInfo("5. Se etter en request som inneholder 'fylkeid=XX&planperi=XXXX-XX&skoleid=XXX'");
            
            return parameters;
        }

        private async Task SendStuRegistrationToSupabaseAsync(ScheduleItem stuSession, string registrationTime, string userEmail = null)
        {
            try
            {
                LogInfo("Sending STU registration to Supabase...");

                // Get email from activation file if not provided
                if (string.IsNullOrEmpty(userEmail))
                {
                    userEmail = await GetUserEmailFromActivationAsync();
                    if (string.IsNullOrEmpty(userEmail))
                    {
                        LogError("Cannot send STU registration - no email found");
                        return;
                    }
                }

                var payload = new
                {
                    user_email = userEmail,
                    session_date = DateTime.ParseExact(stuSession.Dato, "yyyyMMdd", null).ToString("yyyy-MM-dd"),
                    session_start = stuSession.StartKl,
                    session_end = stuSession.SluttKl,
                    course_name = stuSession.KNavn,
                    registration_time = registrationTime,
                    registration_window = stuSession.TidsromTilstedevaerelse,
                    created_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/rest/v1/stu_registrations")
                {
                    Content = content
                };

                // Add Supabase headers
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
                request.Headers.Add("Prefer", "return=minimal");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    LogSuccess($"STU registration sent to Supabase successfully for {userEmail}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogError($"Failed to send to Supabase: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Supabase request failed: {ex.Message}");
            }
        }

       private async Task RegisterAttendanceAsync(ScheduleItem stuTime, Dictionary<string, string> cookies)
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

                // Add headers
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
                request.Headers.Add("Origin", "https://iskole.net");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                // Add cookies
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                LogDebug("Sending registration request...");
                var response = await _httpClient.SendAsync(request);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    LogDebug($"Registration response: {responseContent}");

                    // Send to Supabase after successful registration
                    var registrationTime = DateTime.Now.ToString("HH:mm:ss");
                    var userEmail = await GetUserEmailFromActivationAsync();
                    
                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        LogDebug($"Sending Supabase registration for email: {userEmail}");
                        await SendStuRegistrationToSupabaseAsync(stuTime, registrationTime, userEmail);
                    }
                    else
                    {
                        LogError("Could not send to Supabase - no user email found");
                    }
                }
                else
                {
                    LogError($"Registration failed with status {response.StatusCode}");
                    LogDebug($"Error response: {responseContent}");
                    throw new Exception($"Registration failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Registration error: {ex.Message}");
                throw;
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

        public void Dispose()
        {
            LogInfo("Disposing resources...");
            _cancellationTokenSource?.Cancel();
            _webDriver?.Quit();
            _webDriver?.Dispose();
            _httpClient?.Dispose();

            // Close all active overlay windows
            foreach (var window in _activeOverlayWindows.ToList())
            {
                try
                {
                    window.Close();
                }
                catch { }
            }
            _activeOverlayWindows.Clear();
        }
    }

    public class Cookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem> Items { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string Fag { get; set; }
        public string Stkode { get; set; }
        public string KlTrinn { get; set; }
        public string KlId { get; set; }
        public string KNavn { get; set; }
        public string GruppeNr { get; set; }
        public string Dato { get; set; }
        public string StartKl { get; set; }
        public string SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string Typefravaer { get; set; }
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string TidsromTilstedevaerelse { get; set; }
        public int Timenr { get; set; }
    }
}