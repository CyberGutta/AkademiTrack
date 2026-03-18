using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services.Interfaces
{
    public interface INotificationService
    {
        event EventHandler<NotificationEventArgs>? NotificationAdded;
        event EventHandler<NotificationEventArgs>? NotificationDismissed;
        
        Task ShowNotificationAsync(string title, string message, NotificationLevel level = NotificationLevel.Info, 
            string? imageUrl = null, string? customColor = null, bool isHighPriority = false);
        
        Task DismissNotificationAsync(int notificationId);
        Task ClearAllNotificationsAsync();
        
        IReadOnlyList<NotificationEntry> GetActiveNotifications();
        bool HasActiveNotifications { get; }
    }

    public enum NotificationLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Admin
    }

    public class NotificationEventArgs : EventArgs
    {
        public NotificationEntry Notification { get; }
        
        public NotificationEventArgs(NotificationEntry notification)
        {
            Notification = notification;
        }
    }


}