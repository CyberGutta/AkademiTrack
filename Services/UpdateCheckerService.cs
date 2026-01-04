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

        // 🧪 FOR TESTING: Set to 30 seconds to see notifications quickly
        // 🚀 FOR PRODUCTION: Change to TimeSpan.FromMinutes(10)
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

        private bool _disposed;
        private int _notificationCount = 0;
        private bool _updateAvailable = false;

        // 🧪 FOR TESTING: Set to true to simulate an available update
        // 🚀 FOR PRODUCTION: Set to false for real update checks
        private const bool SIMULATE_UPDATE_AVAILABLE = false;

        // Escalating notification messages
        private readonly string[] _notificationMessages = new[]
        {
            "En ny versjon er tilgjengelig!",
            "Vennligst oppdater AkademiTrack nå.",
            "Din versjon er utdatert. Oppdater!",
            "VIKTIG: Vennligst oppdater til den nyeste versjonen!",
            "Kritisk: Du må oppdatere AkademiTrack!",
            "HASTER: Oppdater nå!",
            "SISTE ADVARSEL: Oppdater umiddelbart!",
            "KRITISK NIVÅ: Oppdater nå eller risiker feil!"
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

                // If update is available, BOMBARD the user
                if (updateDetected)
                {
                    _updateAvailable = true;

                    // Get escalating message based on how many times we've notified
                    string message = GetEscalatingMessage();

                    Debug.WriteLine($"[UpdateChecker] BOMBARDING USER with notification #{_notificationCount + 1}");
                    Debug.WriteLine($"[UpdateChecker] Update version: v{versionNumber}");

                    // Show notification on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        NativeNotificationService.Show(
                            $"Oppdatering #{_notificationCount + 1}",
                            $"{message}\n\nVersjon {versionNumber} venter!",
                            _notificationCount < 3 ? "INFO" :
                            _notificationCount < 6 ? "WARNING" : "ERROR"
                        );
                    });

                    _notificationCount++;

                    // After 10 notifications, show a special "YOU'RE IGNORING THIS" message
                    if (_notificationCount >= 10 && _notificationCount % 5 == 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            NativeNotificationService.Show(
                                "KRITISK MELDING",
                                $"Du har ignorert {_notificationCount} oppdateringsmeldinger!\n\n" +
                                $"Versjon {versionNumber} er tilgjengelig.\n\n" +
                                "Disse meldingene fortsetter til du oppdaterer!",
                                "ERROR"
                            );
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("[UpdateChecker] No updates available");

                    // Reset counter if no update is available
                    if (_updateAvailable)
                    {
                        Debug.WriteLine("[UpdateChecker] Update was installed! Resetting notification counter.");
                        _notificationCount = 0;
                        _updateAvailable = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Error during scheduled check: {ex.Message}");
                Debug.WriteLine($"[UpdateChecker] Stack trace: {ex.StackTrace}");
            }
        }

        private string GetEscalatingMessage()
        {
            // Return escalating messages, but cap at the most aggressive one
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