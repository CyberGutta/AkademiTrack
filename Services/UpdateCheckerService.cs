using AkademiTrack.ViewModels;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Aggressive update checker that BOMBARDS users with notifications every 10 minutes
    /// </summary>
    public class UpdateCheckerService : IDisposable
    {
        private readonly SettingsViewModel _settingsViewModel;
        private Timer? _updateCheckTimer;

        // 🚀 FOR PRODUCTION: Set to 10 minutes for reasonable update checks
        // 🧪 FOR TESTING: Set to 30 seconds to see notifications quickly
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

        private bool _disposed;
        private int _notificationCount = 0;
        private bool _updateAvailable = false;
        private DateTime _lastUpdateNotification = DateTime.MinValue;

        // 🧪 FOR TESTING: Set to true to simulate an available update
        // 🚀 FOR PRODUCTION: Set to false for real update checks
        private const bool SIMULATE_UPDATE_AVAILABLE = false;

        // More reasonable notification messages
        private readonly string[] _notificationMessages = new[]
        {
            "En ny versjon er tilgjengelig for nedlasting.",
            "Vennligst oppdater AkademiTrack når det passer deg.",
            "Ny versjon inneholder viktige forbedringer.",
            "Anbefaler å oppdatere til nyeste versjon.",
            "Oppdatering tilgjengelig - inneholder feilrettinger."
        };

        public UpdateCheckerService(SettingsViewModel settingsViewModel)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        }

    
        public void StartPeriodicChecks()
        {
            if (_updateCheckTimer != null)
            {
                Debug.WriteLine("[UpdateChecker] Already running");
                return;
            }

            Debug.WriteLine($"[UpdateChecker] Starting update checks every {_checkInterval.TotalMinutes:F1} minutes");
            Debug.WriteLine($"[UpdateChecker] Test mode: {(SIMULATE_UPDATE_AVAILABLE ? "ON" : "OFF")}");

            _ = CheckForUpdatesAsync();

            _updateCheckTimer = new Timer(
                async _ => await CheckForUpdatesAsync(),
                null,
                _checkInterval,
                _checkInterval
            );
        }

        public void StopPeriodicChecks()
        {
            Debug.WriteLine("[UpdateChecker] Stopping periodic checks");
            _updateCheckTimer?.Dispose();
            _updateCheckTimer = null;
            _notificationCount = 0;
            _updateAvailable = false;
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                Debug.WriteLine($"[UpdateChecker] === Check #{_notificationCount + 1} at {DateTime.Now:HH:mm:ss} ===");

                bool updateDetected = false;
                string versionNumber = "unknown";

                if (SIMULATE_UPDATE_AVAILABLE)
                {
                    Debug.WriteLine("[UpdateChecker] 🧪 TEST MODE: Simulating available update");
                    updateDetected = true;
                    versionNumber = "99.99.99";

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _settingsViewModel.UpdateAvailable = true;
                        _settingsViewModel.AvailableVersion = versionNumber;
                    });
                }
                else
                {
                    // 🚀 PRODUCTION MODE: Real update check
                    Debug.WriteLine("[UpdateChecker] PRODUCTION: Checking for real updates...");
                    await _settingsViewModel.CheckForUpdatesAsync();

                    // Read the results from ViewModel
                    updateDetected = _settingsViewModel.UpdateAvailable;
                    versionNumber = _settingsViewModel.AvailableVersion ?? "unknown";

                    Debug.WriteLine($"[UpdateChecker] Real check result: Update={updateDetected}, Version={versionNumber}");
                }

                // If update is available, notify user reasonably
                if (updateDetected)
                {
                    _updateAvailable = true;

                    // Only notify once per day to avoid spam
                    var timeSinceLastNotification = DateTime.Now - _lastUpdateNotification;
                    if (timeSinceLastNotification.TotalHours >= 24 || _notificationCount == 0)
                    {
                        string message = GetReasonableMessage();

                        Debug.WriteLine($"[UpdateChecker] Notifying user about update (notification #{_notificationCount + 1})");
                        Debug.WriteLine($"[UpdateChecker] Update version: v{versionNumber}");

                        // Show notification on UI thread
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            NativeNotificationService.Show(
                                "Oppdatering Tilgjengelig",
                                $"{message}\n\nVersjon {versionNumber} er klar for nedlasting.",
                                "INFO"
                            );
                        });

                        _notificationCount++;
                        _lastUpdateNotification = DateTime.Now;
                    }
                    else
                    {
                        Debug.WriteLine($"[UpdateChecker] Update available but not notifying (last notification: {_lastUpdateNotification:HH:mm:ss})");
                    }
                }
                else
                {
                    Debug.WriteLine("[UpdateChecker] No updates available");

                    // Reset counter if no update is available
                    if (_updateAvailable)
                    {
                        Debug.WriteLine("[UpdateChecker] Update was installed! Resetting notification counter.");
                        
                        // Notify user that update was successful
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            NativeNotificationService.Show(
                                "Oppdatering Fullført",
                                "AkademiTrack er nå oppdatert til nyeste versjon!",
                                "SUCCESS"
                            );
                        });
                        
                        _notificationCount = 0;
                        _updateAvailable = false;
                        _lastUpdateNotification = DateTime.MinValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Error during scheduled check: {ex.Message}");
                Debug.WriteLine($"[UpdateChecker] Stack trace: {ex.StackTrace}");
            }
        }

        private string GetReasonableMessage()
        {
            // Return reasonable messages, cycling through them
            int index = Math.Min(_notificationCount, _notificationMessages.Length - 1);
            return _notificationMessages[index];
        }

        public void Dispose()
        {
            if (_disposed) return;

            Debug.WriteLine("[UpdateChecker] Disposing...");
            StopPeriodicChecks();
            _disposed = true;
        }
    }
}