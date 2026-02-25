using System;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Maintains a heartbeat to the widget by updating the timestamp every 5 seconds.
    /// This allows the widget to quickly detect when the main app is closed.
    /// </summary>
    public class WidgetHeartbeatService : IDisposable
    {
        private readonly WidgetDataService _widgetDataService;
        private readonly ILoggingService? _loggingService;
        private Timer? _heartbeatTimer;
        private bool _isRunning;
        private DateTime _lastUpdate = DateTime.MinValue;

        public WidgetHeartbeatService(WidgetDataService widgetDataService, ILoggingService? loggingService = null)
        {
            _widgetDataService = widgetDataService;
            _loggingService = loggingService;
        }

        /// <summary>
        /// Starts the heartbeat timer that updates widget data every 10 seconds
        /// </summary>
        public void Start()
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

            // Then update every 10 seconds (widget checks every 15s, staleness is 20s)
            _heartbeatTimer = new Timer(
                async _ => await UpdateHeartbeatAsync(),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10)
            );
        }

        /// <summary>
        /// Stops the heartbeat timer
        /// </summary>
        public void Stop()
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

        private async Task UpdateHeartbeatAsync()
        {
            try
            {
                // Only update if we haven't updated in the last 8 seconds
                // This prevents excessive updates if multiple timers fire
                if ((DateTime.Now - _lastUpdate).TotalSeconds < 8)
                {
                    return;
                }

                _lastUpdate = DateTime.Now;

                // Trigger a widget data refresh with current cached data
                // This updates the LastUpdated timestamp so the widget knows the app is alive
                await _widgetDataService.RefreshHeartbeatAsync();
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
