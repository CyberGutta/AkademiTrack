using System;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Maintains a heartbeat to the widget by updating the timestamp every 6 seconds.
    /// This allows the widget to quickly detect when the main app is closed.
    /// Uses a more resilient approach that works even when the app is minimized.
    /// </summary>
    public class WidgetHeartbeatService : IDisposable
    {
        private readonly WidgetDataService _widgetDataService;
        private readonly ILoggingService? _loggingService;
        private Timer? _heartbeatTimer;
        private bool _isRunning;
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly object _lockObject = new object();

        public WidgetHeartbeatService(WidgetDataService widgetDataService, ILoggingService? loggingService = null)
        {
            _widgetDataService = widgetDataService;
            _loggingService = loggingService;
        }

        /// <summary>
        /// Starts the heartbeat timer that updates widget data every 6 seconds
        /// </summary>
        public void Start()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                {
                    _loggingService?.LogDebug("[WIDGET HEARTBEAT] Already running");
                    return;
                }

                _isRunning = true;
                _loggingService?.LogInfo("[WIDGET HEARTBEAT] Starting heartbeat service");

                // Update immediately on start
                _ = UpdateHeartbeatAsync();

                // Use a more aggressive timer that's less likely to be suspended
                // Timer with shorter intervals are less likely to be throttled by macOS
                _heartbeatTimer = new Timer(
                    async _ => await UpdateHeartbeatAsync(),
                    null,
                    TimeSpan.FromSeconds(5),  // Start after 5 seconds
                    TimeSpan.FromSeconds(5)   // Repeat every 5 seconds (more frequent)
                );
            }
        }

        /// <summary>
        /// Stops the heartbeat timer
        /// </summary>
        public void Stop()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                {
                    return;
                }

                _isRunning = false;
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
                _loggingService?.LogInfo("[WIDGET HEARTBEAT] Stopped heartbeat service");
            }
        }

        private async Task UpdateHeartbeatAsync()
        {
            try
            {
                // Only update if we haven't updated in the last 3 seconds
                // This prevents excessive updates if multiple timers fire
                if ((DateTime.Now - _lastUpdate).TotalSeconds < 3)
                {
                    return;
                }

                _lastUpdate = DateTime.Now;

                // Trigger a widget data refresh with current cached data
                // This updates the LastUpdated timestamp so the widget knows the app is alive
                await _widgetDataService.RefreshHeartbeatAsync();
                
                _loggingService?.LogDebug($"[WIDGET HEARTBEAT] Updated at {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                _loggingService?.LogWarning($"[WIDGET HEARTBEAT] Failed to update: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
