using System;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Enhanced notification manager that coordinates all notification services
    /// and provides additional smart notifications for user experience
    /// </summary>
    public class EnhancedNotificationManager : IDisposable
    {
        private readonly INotificationService _notificationService;
        private readonly SystemHealthNotificationService _healthNotificationService;
        private readonly UpdateCheckerService _updateCheckerService;
        private bool _disposed;

        public EnhancedNotificationManager(
            INotificationService notificationService,
            SettingsViewModel settingsViewModel)
        {
            _notificationService = notificationService;
            _healthNotificationService = new SystemHealthNotificationService(notificationService);
            _updateCheckerService = new UpdateCheckerService(settingsViewModel);
        }

        /// <summary>
        /// Start all notification monitoring services
        /// </summary>
        public void StartAllServices()
        {
            _healthNotificationService.StartMonitoring();
            _updateCheckerService.StartPeriodicChecks();
            
            // Send welcome notification
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait 2 seconds after startup
                    await _notificationService.ShowNotificationAsync(
                        "AkademiTrack Startet",
                        "Alle systemer er klare. Automatisering vil starte når du er innenfor skoletid.",
                        NotificationLevel.Success
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EnhancedNotificationManager] Welcome notification failed: {ex.Message}");
                }
            }).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[EnhancedNotificationManager] Welcome notification task failed: {t.Exception.GetBaseException().Message}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Stop all notification monitoring services
        /// </summary>
        public void StopAllServices()
        {
            _healthNotificationService.StopMonitoring();
            _updateCheckerService.StopPeriodicChecks();
        }

        /// <summary>
        /// Send a notification when the app is about to close
        /// </summary>
        public async Task NotifyAppClosingAsync()
        {
            await _notificationService.ShowNotificationAsync(
                "AkademiTrack Lukkes",
                "Automatisering er stoppet. Start appen igjen for å fortsette overvåking.",
                NotificationLevel.Info
            );
        }

        /// <summary>
        /// Send a notification when the app detects wake from sleep
        /// </summary>
        public async Task NotifyWakeFromSleepAsync()
        {
            await _notificationService.ShowNotificationAsync(
                "Våknet fra Dvale",
                "AkademiTrack har oppdaget at datamaskinen våknet fra dvale. Sjekker automatiseringsstatus...",
                NotificationLevel.Info
            );
        }

        /// <summary>
        /// Send a notification when credentials expire
        /// </summary>
        public async Task NotifyCredentialsExpiredAsync()
        {
            await _notificationService.ShowNotificationAsync(
                "Innlogging Utløpt",
                "Dine Feide-cookies har utløpt. Automatisering vil prøve å logge inn på nytt.",
                NotificationLevel.Warning,
                isHighPriority: true
            );
        }

        /// <summary>
        /// Send a notification when no STU sessions are found for the day
        /// </summary>
        public async Task NotifyNoStuSessionsAsync()
        {
            await _notificationService.ShowNotificationAsync(
                "Ingen STU-økter i Dag",
                "Ingen STUDIE-økter funnet i timeplanen for i dag. Automatisering er ikke nødvendig.",
                NotificationLevel.Info
            );
        }

        /// <summary>
        /// Send a notification when registration window is about to open
        /// </summary>
        public async Task NotifyRegistrationWindowSoonAsync(string sessionTime, int minutesUntil)
        {
            await _notificationService.ShowNotificationAsync(
                "Registrering Snart",
                $"STU-økt {sessionTime} kan registreres om {minutesUntil} minutter.",
                NotificationLevel.Info
            );
        }

        /// <summary>
        /// Send a notification when all sessions for the day are registered
        /// </summary>
        public async Task NotifyAllSessionsCompletedAsync(int totalSessions)
        {
            await _notificationService.ShowNotificationAsync(
                "Alle Økter Registrert",
                $"Alle {totalSessions} STU-økter er registrert for i dag. Automatisering fullført!",
                NotificationLevel.Success,
                isHighPriority: true
            );
        }

        /// <summary>
        /// Send a notification when automation fails due to network issues
        /// </summary>
        public async Task NotifyNetworkIssueAsync(string details)
        {
            await _notificationService.ShowNotificationAsync(
                "Nettverksproblem",
                $"Automatisering kan ikke fortsette på grunn av nettverksproblemer: {details}",
                NotificationLevel.Error,
                isHighPriority: true
            );
        }

        /// <summary>
        /// Send a notification when user needs to connect to school network
        /// </summary>
        public async Task NotifyNeedSchoolNetworkAsync()
        {
            await _notificationService.ShowNotificationAsync(
                "Koble til Skolens Nettverk",
                "Du må være tilkoblet skolens WiFi-nettverk for å registrere STU-økter.",
                NotificationLevel.Warning,
                isHighPriority: true
            );
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopAllServices();
            _healthNotificationService?.Dispose();
            _updateCheckerService?.Dispose();
            _disposed = true;
        }
    }
}