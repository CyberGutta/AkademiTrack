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

        private readonly Queue<NotificationQueueItem> _notificationQueue = new Queue<NotificationQueueItem>();
        private bool _isShowingNotification = false;
        private readonly object _notificationLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co"; // Replace with your actual URL
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k"; // Replace with your actual anon key
        private string _userEmail = "TESTGMAIL"; // Replace with actual user email
        private string _userPassword = "TESTPASSWORD";

        public class NotificationQueueItem
        {
            public string Title { get; set; }
            public string Message { get; set; }
            public string Level { get; set; }
            public string ImageUrl { get; set; }
            public string CustomColor { get; set; }
            public bool IsHighPriority { get; set; }
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
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

                // Create request with shorter timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 second timeout instead of 100

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");

                LogDebug($"Fetching admin notifications for: {userEmail}");

                HttpResponseMessage response = null;
                try
                {
                    response = await _httpClient.SendAsync(request, cts.Token);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || cts.Token.IsCancellationRequested)
                {
                    LogDebug("Admin notification request timed out after 30 seconds - skipping this check");
                    return;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("nodename") || ex.Message.Contains("servname") || ex.Message.Contains("not known"))
                {
                    LogDebug($"DNS resolution failed for Supabase: {ex.Message} - checking network connectivity");

                    // Try a simple connectivity test
                    try
                    {
                        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _httpClient.GetAsync("https://www.google.com", testCts.Token);
                        LogDebug("Internet connectivity OK - Supabase DNS issue");
                    }
                    catch
                    {
                        LogDebug("No internet connectivity detected");
                    }
                    return;
                }
                catch (HttpRequestException ex)
                {
                    LogDebug($"Network error fetching admin notifications: {ex.Message}");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogDebug($"Failed to fetch admin notifications: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"Error response: {errorContent}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json) || json == "[]")
                {
                    LogDebug("No admin notifications returned");
                    return;
                }

                EnhancedAdminNotification[] notifications = null;
                try
                {
                    notifications = JsonSerializer.Deserialize<EnhancedAdminNotification[]>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    LogError($"Failed to parse admin notifications JSON: {ex.Message}");
                    LogDebug($"JSON content: {json.Substring(0, Math.Min(200, json.Length))}...");
                    return;
                }

                if (notifications == null || notifications.Length == 0)
                {
                    LogDebug("No valid admin notifications found");
                    return;
                }

                bool hasNewNotifications = false;

                foreach (var notification in notifications)
                {
                    if (string.IsNullOrEmpty(notification.Id))
                    {
                        LogDebug("Skipping notification with empty ID");
                        continue;
                    }

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

                        var adminTitle = $"[ADMIN]{notification.Title}";

                        try
                        {
                            ShowSystemOverlayNotification(adminTitle, notification.Message, notificationLevel,
                                notification.Image_Url, notification.Custom_Color);

                            LogInfo($"Enhanced admin notification displayed: {notification.Title}");
                        }
                        catch (Exception showEx)
                        {
                            LogError($"Failed to show admin notification: {showEx.Message}");
                            // Continue processing other notifications even if one fails
                        }
                    }
                }

                if (hasNewNotifications)
                {
                    try
                    {
                        await SaveProcessedNotificationIdsAsync();
                    }
                    catch (Exception saveEx)
                    {
                        LogError($"Failed to save processed notification IDs: {saveEx.Message}");
                        // Not critical - notifications will just be shown again next time
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in CheckForNewAdminNotificationsAsync: {ex.Message}");
                if (ShowDetailedLogs)
                {
                    LogDebug($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private async Task SendAdminNotificationAsync(string title, string message, string targetEmail = "all", string priority = "INFO")
        {
            try
            {
                LogInfo($"Sending admin notification via secure function: {title}");

                var payload = new
                {
                    p_title = title,
                    p_message = message,
                    p_target_email = targetEmail,
                    p_priority = priority
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/rest/v1/rpc/send_admin_notification")
                {
                    Content = content
                };

                // Add Supabase headers
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
                request.Headers.Add("Prefer", "return=representation");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    LogSuccess($"Admin notification sent successfully: {title}");
                    LogDebug($"Response: {responseContent}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogError($"Failed to send admin notification: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Admin notification request failed: {ex.Message}");
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



        // Replace your existing ShowNotification method with this enhanced version
        private void ShowNotification(string title, string message, string level = "INFO")
        {
            // Allow these specific system notifications OR any notification marked as admin
            var allowedNotifications = new[]
            {
        "Automation Started",
        "Automation Stopped",
        "Registration Success",
        "Alle Studietimer Registrert",
        "Ingen STUDIE-økter funnet for i dag",
        "Ingen Flere Økter"
    };

            // Check if this is an admin notification
            bool isAdminNotification = title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN[");

            if (allowedNotifications.Contains(title) || isAdminNotification)
            {
                LogDebug($"Queueing enhanced system overlay notification: {title} - {message}");

                // Determine if this is a high priority notification
                bool isHighPriority = title == "Registration Success" ||
                                     title == "Ingen Flere Økter" ||
                                     title.Contains("Ingen STUDIE-økter") ||
                                     isAdminNotification;

                QueueNotification(title, message, level, null, null, isHighPriority);
            }
            else
            {
                LogDebug($"Notification filtered out: {title}");
            }
        }

        private void ShowSystemOverlayNotification(string title, string message, string level, string imageUrl = null, string customColor = null)
        {
            // Determine priority
            bool isRegistrationSuccess = title == "Registration Success";
            bool isPriorityNotification = title == "Ingen Flere Økter" || title.Contains("Ingen STUDIE-økter");
            bool isAdminNotification = title.StartsWith("[ADMIN]") || title.StartsWith("[ADMIN[");
            bool isHighPriority = isRegistrationSuccess || isPriorityNotification || isAdminNotification;

            QueueNotification(title, message, level, imageUrl, customColor, isHighPriority);
        }

        private void QueueNotification(string title, string message, string level, string imageUrl = null, string customColor = null, bool isHighPriority = false)
        {
            var queueItem = new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Level = level,
                ImageUrl = imageUrl,
                CustomColor = customColor,
                IsHighPriority = isHighPriority
            };

            lock (_notificationLock)
            {
                if (isHighPriority)
                {
                    // High priority notifications go to the front of the queue
                    var tempQueue = new Queue<NotificationQueueItem>();
                    tempQueue.Enqueue(queueItem);

                    while (_notificationQueue.Count > 0)
                    {
                        tempQueue.Enqueue(_notificationQueue.Dequeue());
                    }

                    while (tempQueue.Count > 0)
                    {
                        _notificationQueue.Enqueue(tempQueue.Dequeue());
                    }

                    LogDebug($"High priority notification queued at front: {title}");
                }
                else
                {
                    _notificationQueue.Enqueue(queueItem);
                    LogDebug($"Notification queued: {title} (Queue size: {_notificationQueue.Count})");
                }

                // Start processing queue if not already showing a notification
                if (!_isShowingNotification)
                {
                    ProcessNotificationQueue();
                }
            }
        }

        private async void ProcessNotificationQueue()
        {
            while (true)
            {
                NotificationQueueItem nextNotification = null;

                lock (_notificationLock)
                {
                    if (_notificationQueue.Count == 0)
                    {
                        _isShowingNotification = false;
                        LogDebug("Notification queue empty - stopping processing");
                        return;
                    }

                    nextNotification = _notificationQueue.Dequeue();
                    _isShowingNotification = true;
                }

                LogDebug($"Processing queued notification: {nextNotification.Title} (Remaining in queue: {_notificationQueue.Count})");

                try
                {
                    // Show the notification and wait for it to complete
                    await ShowNotificationAndWaitAsync(nextNotification);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to show queued notification: {ex.Message}");
                }

                // Add a small delay between notifications for better UX
                await Task.Delay(500);
            }
        }

        // New method to show notification and wait for completion
        private async Task ShowNotificationAndWaitAsync(NotificationQueueItem item)
        {
            TaskCompletionSource<bool> notificationComplete = new TaskCompletionSource<bool>();

            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    await CreateOverlayWindowAndWaitAsync(item, notificationComplete);
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await CreateOverlayWindowAndWaitAsync(item, notificationComplete);
                    });
                }

                // Wait for the notification to complete
                await notificationComplete.Task;
            }
            catch (Exception ex)
            {
                LogError($"Failed to show notification window: {ex.Message}");
                LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
                notificationComplete.SetResult(true); // Complete even on failure
            }
        }

        private async Task CreateOverlayWindowAndWaitAsync(NotificationQueueItem item, TaskCompletionSource<bool> completionSource)
        {
            try
            {
                // Clean up old windows
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

                LogDebug($"Creating overlay window for: {item.Title}");

                NotificationOverlayWindow overlayWindow = null;
                try
                {
                    if (!string.IsNullOrEmpty(item.ImageUrl) || !string.IsNullOrEmpty(item.CustomColor))
                    {
                        overlayWindow = new NotificationOverlayWindow(item.Title, item.Message, item.Level, item.ImageUrl, item.CustomColor);
                    }
                    else
                    {
                        overlayWindow = new NotificationOverlayWindow(item.Title, item.Message, item.Level);
                    }
                }
                catch (Exception createEx)
                {
                    LogError($"Failed to create notification window: {createEx.Message}");
                    LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
                    completionSource.SetResult(true);
                    return;
                }

                bool windowClosed = false;
                overlayWindow.Closed += (s, e) =>
                {
                    try
                    {
                        if (!windowClosed)
                        {
                            windowClosed = true;
                            _activeOverlayWindows.Remove(overlayWindow);
                            LogDebug($"Notification window closed: {item.Title}");
                            completionSource.SetResult(true);
                        }
                    }
                    catch (Exception removeEx)
                    {
                        LogDebug($"Error removing closed window: {removeEx.Message}");
                        completionSource.SetResult(true);
                    }
                };

                _activeOverlayWindows.Add(overlayWindow);

                try
                {
                    overlayWindow.Show();
                    LogDebug($"✓ Notification window shown successfully: {item.Title}");

                    // Set a backup timeout in case the window doesn't close properly
                    _ = Task.Delay(15000).ContinueWith(_ =>
                    {
                        if (!windowClosed)
                        {
                            LogDebug($"Notification timeout reached for: {item.Title}");
                            windowClosed = true;
                            completionSource.TrySetResult(true);
                        }
                    });
                }
                catch (Exception showEx)
                {
                    LogError($"Failed to show notification window: {showEx.Message}");
                    _activeOverlayWindows.Remove(overlayWindow);
                    LogInfo($"NOTIFICATION (fallback): {item.Title} - {item.Message}");
                    completionSource.SetResult(true);
                }
            }
            catch (Exception ex)
            {
                LogError($"Complete failure in CreateOverlayWindowAndWaitAsync: {ex.Message}");
                LogInfo($"NOTIFICATION (emergency fallback): {item.Title} - {item.Message}");
                completionSource.SetResult(true);
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

                // Only update status message for important messages
                if (ShouldShowInStatus(message, level))
                {
                    StatusMessage = logEntry.FormattedMessage;
                }

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

                    // Only update status message for important messages
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
            // Always show ERROR and SUCCESS messages
            if (level == "ERROR" || level == "SUCCESS")
            {
                return true;
            }

            // Show specific important INFO messages only
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
                "Venter 2 minutter før neste tidssjekk..."  // ADD THIS LINE
            };

            // Check if message starts with any important message pattern
            foreach (var important in importantMessages)
            {
                if (message.StartsWith(important))
                {
                    return true;
                }
            }

            // Show messages about found STU sessions
            if (message.Contains("STU-økter for i dag") && message.Contains("Fant"))
            {
                return true;
            }

            // Show cycle count messages (less frequently)
            if (message.Contains("Overvåkingssyklus") && message.Contains("#"))
            {
                return true;
            }

            // Show session status summaries
            if (message.Contains("Øktstatus:") && (message.Contains("åpne") || message.Contains("registrerte")))
            {
                return true;
            }

            // Don't show DEBUG messages in status (regardless of ShowDetailedLogs setting)
            if (level == "DEBUG")
            {
                return false;
            }

            // Don't show routine/verbose INFO messages or UI interactions
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
        "Venter 2 minutter før neste tidssjekk...",
        "Venter 1 minutt før nytt forsøk...",
        "Rydder opp nettleser ressurser...",
        "Nettleser opprydding fullført",
        "Disposing resources...",
        "vindu åpnet",
        "window opened"
    };

            foreach (var skip in skipMessages)
            {
                if (message.StartsWith(skip) || message.Contains(skip))
                {
                    return false;
                }
            }

            // For remaining INFO messages, only show if they seem important
            if (level == "INFO")
            {
                // Show WARNING level messages
                if (level == "WARNING")
                {
                    return true;
                }

                return false; // Skip most other INFO messages from status
            }

            return true; // Show anything else not explicitly filtered
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

                Dictionary<string, string> cookies = null;
                bool needsFreshLogin = false;

                // Step 1: Check if existing cookies work and try to get parameters
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
                            // Try to get parameters for valid cookies
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
                            cookies = null;
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

                // Step 2: Fresh login if needed (either no cookies or no valid parameters)
                if (needsFreshLogin || cookies == null)
                {
                    LogInfo("Åpner nettleser for ny innlogging og parameterinnhenting...");

                    cookies = await GetCookiesViaBrowserAsync();

                    if (cookies == null)
                    {
                        LogError("Kunne ikke få cookies fra nettleser innlogging");
                        return;
                    }

                    LogSuccess($"Fikk {cookies.Count} nye cookies og oppdaterte parametere");
                }

                // Ensure we have valid parameters
                if (_userParameters == null || !_userParameters.IsComplete)
                {
                    LogError("KRITISK: Mangler gyldige parametere etter innlogging");
                    LogError("Dette bør ikke skje - sjekk parameter-capture logikk");
                    return;
                }

                LogSuccess("Autentisering og parametere fullført - starter overvåkingssløyfe...");
                LogInfo($"Bruker parametere: fylkeid={_userParameters.FylkeId}, planperi={_userParameters.PlanPeri}, skoleid={_userParameters.SkoleId}");

                // Step 3: Start monitoring with confirmed valid parameters
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
                // Setup Chrome options - EXACTLY THE SAME
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

                // Navigate to login page - EXACTLY THE SAME
                LogInfo("Navigerer til innloggingsside: https://iskole.net/elev/?ojr=login");
                _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");

                LogInfo("Vennligst fullfør innloggingsprosessen i nettleseren");
                LogInfo("Venter på navigering til: https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");

                // Wait for user to reach the target URL - EXACTLY THE SAME
                var targetReached = await WaitForTargetUrlAsync();

                if (!targetReached)
                {
                    LogError("Tidsavbrudd - innlogging ble ikke fullført innen 10 minutter");
                    return null;
                }

                LogSuccess("Innlogging fullført!");

                // NEW: Quick parameter capture (only addition - 2 seconds max)
                await QuickParameterCapture();

                LogInfo("Ekstraherer cookies fra nettleser økten...");

                // Extract cookies - check if driver is still valid - EXACTLY THE SAME
                if (!IsWebDriverValid(_webDriver))
                {
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet etter innlogging");
                    ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return null;
                }

                var seleniumCookies = _webDriver.Manage().Cookies.AllCookies;
                var cookieDict = seleniumCookies.ToDictionary(c => c.Name, c => c.Value);

                LogDebug($"Ekstraherte cookies: {string.Join(", ", cookieDict.Keys)}");

                // Save cookies - EXACTLY THE SAME
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
                ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                await ForceStopAutomationAsync();
                return null;
            }
            catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                        invEx.Message.Contains("no such session"))
            {
                LogError("Automatisering stoppet - bruker lukket innloggingsvinduet under prosessen");
                ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                await ForceStopAutomationAsync();
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Nettleser innlogging feilet: {ex.Message}");
                LogDebug($"Exception type: {ex.GetType().Name}");
                ShowNotification("Automation Stopped", "Innlogging feilet - automatisering stoppet", "ERROR");
                await ForceStopAutomationAsync();
                return null;
            }
            finally
            {
                // Always clean up the web driver, regardless of what happened - EXACTLY THE SAME
                await CleanupWebDriverAsync(localWebDriver);
            }
        }

        private async Task<UserParameters> QuickParameterCapture()
        {
            try
            {
                if (!IsWebDriverValid(_webDriver)) return null;

                LogDebug("Fanger parametere fra nettverkstrafikk...");

                var jsExecutor = (IJavaScriptExecutor)_webDriver;

                // Check performance entries for actual network requests
                var result = jsExecutor.ExecuteScript(@"
            try {
                // Get all network requests from performance API
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
                
                // If no network requests found yet, wait and check again
                return { waiting: true };
                
            } catch (e) {
                console.error('Error capturing parameters:', e);
                return null;
            }
        ");

                // If we need to wait for network requests, try a few times
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

                    // Save the captured parameters
                    await SaveParametersAsync(parameters);

                    // Set the class field
                    _userParameters = parameters;

                    return parameters;
                }
                else
                {
                    LogDebug("Ingen nettverkstrafikk funnet med parametere");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogError($"Parameter capture feilet: {ex.Message}");
                return null;
            }
        }

        // Helper to get current school year dynamically
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
                        LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                        ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
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
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                    ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return false;
                }
                catch (InvalidOperationException invEx) when (invEx.Message.Contains("disconnected") ||
                                                            invEx.Message.Contains("no such session"))
                {
                    LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                    ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                    return false;
                }
                catch (Exception ex)
                {
                    LogDebug($"Error checking URL: {ex.Message}");

                    // If we can't check the URL, the browser might be closed
                    if (!IsWebDriverValid(_webDriver))
                    {
                        LogError("Automatisering stoppet - bruker lukket innloggingsvinduet");
                        ShowNotification("Automation Stopped", "Innlogging avbrutt av bruker - automatisering stoppet", "WARNING");
                        return false;
                    }
                }

                await Task.Delay(2000);
            }

            LogError("Automatisering stoppet - innlogging tidsavbrudd");
            ShowNotification("Automation Stopped", "Innlogging tidsavbrudd - automatisering stoppet", "ERROR");
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

        private bool HasConflictingClass(ScheduleItem stuSession, List<ScheduleItem> allScheduleItems)
        {
            try
            {
                // Parse STU session times
                if (!TimeSpan.TryParse(stuSession.StartKl, out var stuStartTime) ||
                    !TimeSpan.TryParse(stuSession.SluttKl, out var stuEndTime))
                {
                    LogDebug($"Could not parse STU session times: {stuSession.StartKl}-{stuSession.SluttKl}");
                    return false; // If we can't parse times, allow registration (safer default)
                }

                // Check ALL other classes on the same date (any class type)
                var conflictingClasses = allScheduleItems
                    .Where(item => item.Dato == stuSession.Dato && // Same date
                                  item.KNavn != "STU" && // Not another STU session
                                  item.Id != stuSession.Id) // Not the same session
                    .ToList();

                foreach (var otherClass in conflictingClasses)
                {
                    // Parse other class times
                    if (!TimeSpan.TryParse(otherClass.StartKl, out var otherStartTime) ||
                        !TimeSpan.TryParse(otherClass.SluttKl, out var otherEndTime))
                    {
                        LogDebug($"Could not parse class times for {otherClass.KNavn}: {otherClass.StartKl}-{otherClass.SluttKl}");
                        continue; // Skip this class if we can't parse its times
                    }

                    // Check for time overlap
                    // Two time periods overlap if: start1 < end2 AND start2 < end1
                    bool hasOverlap = stuStartTime < otherEndTime && otherStartTime < stuEndTime;

                    if (hasOverlap)
                    {
                        LogInfo($"CONFLICT DETECTED: STU session {stuSession.StartKl}-{stuSession.SluttKl} overlaps with class {otherClass.KNavn} ({otherClass.StartKl}-{otherClass.SluttKl})");
                        LogInfo($"Skipping STU registration - student must attend regular class: {otherClass.KNavn}");
                        return true; // Conflict found with ANY class
                    }
                }

                LogDebug($"No conflicts found for STU session {stuSession.StartKl}-{stuSession.SluttKl}");
                return false; // No conflicts
            }
            catch (Exception ex)
            {
                LogError($"Error checking for class conflicts: {ex.Message}");
                return false; // On error, allow registration (safer default)
            }
        }


        // OPTIMIZED MONITORING LOOP - SINGLE API CALL, TIME-BASED CHECKING
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

        // CHECK FOR NO STU SESSIONS - SHOW NOTIFICATION IMMEDIATELY
        if (todaysStuSessions.Count == 0)
        {
            LogInfo("Ingen STUDIE-økter funnet for i dag - viser melding og stopper automatisering");
            ShowNotification("Ingen STUDIE-økter funnet for i dag",
                "Det er ingen STU-økter å registrere for i dag. Automatiseringen stopper.", "INFO");
            return;
        }

        // FILTER OUT STU SESSIONS WITH CONFLICTS
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

        // Log all valid STU sessions
        foreach (var stuTime in validStuSessions)
        {
            LogInfo($"Gyldig STU økt: {stuTime.StartKl}-{stuTime.SluttKl}, Registreringsvindu: {stuTime.TidsromTilstedevaerelse}");
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

                // Check each VALID STU session using cached data and current time - NO API CALLS
                foreach (var stuSession in validStuSessions) // Use validStuSessions instead of todaysStuSessions
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
                if (allSessionsComplete || registeredSessions.Count == validStuSessions.Count)
                {
                    LogSuccess($"Alle {validStuSessions.Count} gyldige STU-økter er håndtert for i dag!");
                    LogInfo($"Registrerte økter: {registeredSessions.Count}, Totalt gyldige: {validStuSessions.Count}");

                    // Show appropriate completion notification
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

                // Log summary of current state and show in status for 6 seconds
                LogInfo($"Øktstatus: {openWindows} åpne, {notYetOpenWindows} venter, {closedWindows} lukkede/registrerte");

                // Wait 6 seconds before showing the "waiting" message
                await Task.Delay(6000, cancellationToken);

                // Check if cancellation was requested during the 6-second delay
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Now show the "waiting 2 minutes" message
                LogInfo("Venter 2 minutter før neste tidssjekk...");

                // Wait the remaining time (2 minutes minus the 6 seconds we already waited)
                await Task.Delay(TimeSpan.FromMinutes(2).Subtract(TimeSpan.FromSeconds(6)), cancellationToken);
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
                LogDebug("Sjekker cached parametere...");

                // First, try to load valid cached parameters
                var cachedParams = await LoadValidParametersAsync();
                if (cachedParams != null && cachedParams.IsComplete)
                {
                    LogInfo($"Bruker gyldige cached parametere for skoleår {cachedParams.PlanPeri}");
                    return cachedParams;
                }

                // If no valid cache and we have browser, extract from network
                if (IsWebDriverValid(_webDriver))
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

                // CRITICAL: If we reach here, we would need fallbacks - instead force fresh login
                LogError("Kunne ikke finne gyldige parametere - fallback ikke tillatt");
                LogError("Sletter cookies for å tvinge ny innlogging med parameterinnhenting");

                // Delete cookies to force fresh browser login
                await DeleteCookiesAsync();

                throw new InvalidOperationException("Parameters required - forcing fresh login");
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw to trigger fresh login
            }
            catch (Exception ex)
            {
                LogError($"Kritisk feil ved parameterhåndtering: {ex.Message}");
                LogError("Tvinger ny innlogging for å sikre korrekte parametere");
                await DeleteCookiesAsync();
                throw new InvalidOperationException("Parameters required - forcing fresh login");
            }
        }

        private async Task DeleteCookiesAsync()
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

        private async Task<UserParameters> LoadValidParametersAsync()
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

                // Check if parameters are still valid for current school year
                if (!IsCurrentSchoolYear(savedData.Parameters.PlanPeri))
                {
                    LogInfo($"Lagrede parametere er for gammelt skoleår ({savedData.Parameters.PlanPeri}) - trenger oppdatering");
                    File.Delete(filePath); // Clean up old file
                    return null;
                }

                LogSuccess($"Lastet gyldige parametere fra cache (alder: {age.TotalDays:F0} dager, skoleår: {savedData.Parameters.PlanPeri})");
                LogDebug($"Parametere: fylkeid={savedData.Parameters.FylkeId}, planperi={savedData.Parameters.PlanPeri}, skoleid={savedData.Parameters.SkoleId}");
                return savedData.Parameters;
            }
            catch (Exception ex)
            {
                LogDebug($"Feil ved lasting av parametere: {ex.Message}");
                return null;
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

                // Validate that we're saving parameters for current school year
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
            public UserParameters Parameters { get; set; }
            public DateTime SavedAt { get; set; }
            public string SchoolYear { get; set; } // Track which school year these parameters are for
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
                LogDebug($"Registration response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    // Check for network requirement error in successful response
                    if (await CheckForNetworkErrorInResponse(responseContent, stuTime))
                    {
                        return; // Error was handled, don't proceed with success flow
                    }

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

                    // Check for network requirement error in error response too
                    await CheckForNetworkErrorInResponse(responseContent, stuTime);

                    throw new Exception($"Registration failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Registration error: {ex.Message}");
                throw;
            }
        }


        private async Task<bool> CheckForNetworkErrorInResponse(string responseContent, ScheduleItem stuTime)
        {
            try
            {
                // Check for the specific network requirement error
                if (responseContent.Contains("Du må være koblet på skolens nettverk for å kunne registrere fremmøte") ||
                    responseContent.Contains("retval\":50") ||
                    responseContent.Contains("må være koblet på skolens nettverk"))
                {
                    LogError($"NETTVERKSFEIL: Må være tilkoblet skolens nettverk for å registrere STU-økt {stuTime.StartKl}-{stuTime.SluttKl}");
                    LogError("Du er ikke tilkoblet skolens WiFi/nettverk som kreves for registrering");

                    // Show prominent notification to user about network requirement
                    ShowNotification("Feil Nettverk - Kan Ikke Registrere",
                        $"Du må være tilkoblet skolens nettverk (WiFi) for å registrere STU-tid {stuTime.StartKl}-{stuTime.SluttKl}. " +
                        $"Koble til skolens WiFi - programmet fortsetter å kjøre og prøver igjen automatisk.", "ERROR");

                    return true; // Error was handled
                }

                return false; // No network error found
            }
            catch (Exception ex)
            {
                LogDebug($"Error checking for network error in response: {ex.Message}");
                return false;
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