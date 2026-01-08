using System;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Service that monitors system health and sends notifications for issues
    /// </summary>
    public class SystemHealthNotificationService : IDisposable
    {
        private readonly INotificationService _notificationService;
        private readonly SystemHealthCheck _healthCheck;
        private Timer? _healthCheckTimer;
        private bool _disposed;
        private bool _lastInternetStatus = true;
        private bool _lastFeideStatus = true;
        private bool _lastISkoleStatus = true;
        private bool _lastSeleniumStatus = true;

        public SystemHealthNotificationService(INotificationService notificationService)
        {
            _notificationService = notificationService;
            _healthCheck = new SystemHealthCheck();
        }

        public void StartMonitoring()
        {
            if (_healthCheckTimer != null)
                return;

            // Check health every 5 minutes
            _healthCheckTimer = new Timer(
                async _ => await CheckHealthAndNotifyAsync(),
                null,
                TimeSpan.Zero, // Start immediately
                TimeSpan.FromMinutes(5)
            );
        }

        public void StopMonitoring()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
        }

        private async Task CheckHealthAndNotifyAsync()
        {
            try
            {
                var results = await _healthCheck.RunFullHealthCheckAsync();

                foreach (var result in results)
                {
                    await ProcessHealthResultAsync(result);
                }
            }
            catch (Exception ex)
            {
                await _notificationService.ShowNotificationAsync(
                    "Systemsjekk Feilet",
                    $"Kunne ikke sjekke systemhelse: {ex.Message}",
                    NotificationLevel.Warning
                );
            }
        }

        private async Task ProcessHealthResultAsync(HealthCheckResult result)
        {
            bool currentStatus = result.Status == HealthStatus.Healthy;
            bool? lastStatus = result.ComponentName switch
            {
                "Internett" => _lastInternetStatus,
                "Feide" => _lastFeideStatus,
                "iSkole API" => _lastISkoleStatus,
                "Selenium Driver" => _lastSeleniumStatus,
                _ => null
            };

            // Only notify on status changes (from working to not working, or vice versa)
            if (lastStatus.HasValue && lastStatus.Value != currentStatus)
            {
                if (!currentStatus) // Component failed
                {
                    var level = result.Status == HealthStatus.Error ? NotificationLevel.Error : NotificationLevel.Warning;
                    await _notificationService.ShowNotificationAsync(
                        $"{result.ComponentName} Problem",
                        $"{result.Message}: {result.Details}",
                        level,
                        isHighPriority: result.Status == HealthStatus.Error
                    );
                }
                else // Component recovered
                {
                    await _notificationService.ShowNotificationAsync(
                        $"{result.ComponentName} Gjenopprettet",
                        $"{result.ComponentName} fungerer igjen normalt.",
                        NotificationLevel.Success
                    );
                }
            }

            // Update last known status
            switch (result.ComponentName)
            {
                case "Internett":
                    _lastInternetStatus = currentStatus;
                    break;
                case "Feide":
                    _lastFeideStatus = currentStatus;
                    break;
                case "iSkole API":
                    _lastISkoleStatus = currentStatus;
                    break;
                case "Selenium Driver":
                    _lastSeleniumStatus = currentStatus;
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopMonitoring();
            _disposed = true;
        }
    }
}