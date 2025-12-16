using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;
using Avalonia.Threading;

namespace AkademiTrack.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ObservableCollection<NotificationEntry> _notifications;
        private readonly Queue<NotificationQueueItem> _notificationQueue;
        private readonly SemaphoreSlim _notificationSemaphore;
        private readonly HashSet<string> _processedNotificationIds;
        private readonly string _processedNotificationsFile;
        private readonly object _queueLock = new();
        private bool _isProcessingQueue;
        private int _nextNotificationId = 1;

        public event EventHandler<NotificationEventArgs>? NotificationAdded;
        public event EventHandler<NotificationEventArgs>? NotificationDismissed;

        public bool HasActiveNotifications => _notifications.Any(n => n.IsVisible);

        public NotificationService()
        {
            _notifications = new ObservableCollection<NotificationEntry>();
            _notificationQueue = new Queue<NotificationQueueItem>();
            _notificationSemaphore = new SemaphoreSlim(1, 1);
            _processedNotificationIds = new HashSet<string>();
            _processedNotificationsFile = GetProcessedNotificationsFilePath();
            
            LoadProcessedNotifications();
        }

        public async Task ShowNotificationAsync(string title, string message, NotificationLevel level = NotificationLevel.Info, 
            string? imageUrl = null, string? customColor = null, bool isHighPriority = false)
        {
            var queueItem = new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Level = level,
                ImageUrl = imageUrl,
                CustomColor = customColor,
                IsHighPriority = isHighPriority,
                UniqueId = Guid.NewGuid().ToString()
            };

            lock (_queueLock)
            {
                if (isHighPriority)
                {
                    // Add high priority items to the front
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
                }
                else
                {
                    _notificationQueue.Enqueue(queueItem);
                }
            }

            await ProcessNotificationQueueAsync();
        }

        public async Task DismissNotificationAsync(int notificationId)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
                if (notification != null)
                {
                    notification.IsVisible = false;
                    NotificationDismissed?.Invoke(this, new NotificationEventArgs(notification));
                }
            });
        }

        public async Task ClearAllNotificationsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var notification in _notifications)
                {
                    notification.IsVisible = false;
                }
            });
        }

        public IReadOnlyList<NotificationEntry> GetActiveNotifications()
        {
            return _notifications.Where(n => n.IsVisible).ToList();
        }

        private async Task ProcessNotificationQueueAsync()
        {
            if (!await _notificationSemaphore.WaitAsync(100))
                return;

            try
            {
                lock (_queueLock)
                {
                    if (_isProcessingQueue || _notificationQueue.Count == 0)
                        return;
                    _isProcessingQueue = true;
                }

                while (true)
                {
                    NotificationQueueItem? item = null;
                    
                    lock (_queueLock)
                    {
                        if (_notificationQueue.Count == 0)
                            break;
                        item = _notificationQueue.Dequeue();
                    }

                    if (item != null && !_processedNotificationIds.Contains(item.UniqueId))
                    {
                        await ShowNotificationInternalAsync(item);
                        _processedNotificationIds.Add(item.UniqueId);
                        await SaveProcessedNotificationsAsync();
                    }

                    await Task.Delay(500); // Brief delay between notifications
                }
            }
            finally
            {
                lock (_queueLock)
                {
                    _isProcessingQueue = false;
                }
                _notificationSemaphore.Release();
            }
        }

        private async Task ShowNotificationInternalAsync(NotificationQueueItem item)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var notification = new NotificationEntry
                {
                    Id = _nextNotificationId++,
                    Timestamp = DateTime.Now,
                    Title = item.Title,
                    Message = item.Message,
                    Level = item.Level.ToString(),
                    IsVisible = true
                };

                _notifications.Add(notification);
                NotificationAdded?.Invoke(this, new NotificationEventArgs(notification));

                // Show system notification
                try
                {
                    var levelString = item.Level.ToString().ToUpper();
                    NativeNotificationService.Show(item.Title ?? "Notification", item.Message ?? "", levelString);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to show system notification: {ex.Message}");
                }
            });
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

        private void LoadProcessedNotifications()
        {
            try
            {
                if (File.Exists(_processedNotificationsFile))
                {
                    var json = File.ReadAllText(_processedNotificationsFile);
                    var ids = JsonSerializer.Deserialize<HashSet<string>>(json);
                    if (ids != null)
                    {
                        foreach (var id in ids)
                        {
                            _processedNotificationIds.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load processed notifications: {ex.Message}");
            }
        }

        private async Task SaveProcessedNotificationsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_processedNotificationIds);
                await File.WriteAllTextAsync(_processedNotificationsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save processed notifications: {ex.Message}");
            }
        }

        private class NotificationQueueItem
        {
            public string? Title { get; set; }
            public string? Message { get; set; }
            public NotificationLevel Level { get; set; }
            public string? ImageUrl { get; set; }
            public string? CustomColor { get; set; }
            public bool IsHighPriority { get; set; }
            public string UniqueId { get; set; } = Guid.NewGuid().ToString();
        }
    }
}