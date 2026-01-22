using AkademiTrack.ViewModels;
using AkademiTrack.Services.Interfaces;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Update checker that checks for updates periodically and logs to the app's activity log
    /// </summary>
    public class UpdateCheckerService : IDisposable
    {
        private readonly SettingsViewModel _settingsViewModel;
        private readonly ILoggingService _loggingService;
        private Timer? _updateCheckTimer;

        // FOR PRODUCTION: Set to 10 minutes for reasonable update checks
        // 🧪 FOR TESTING: Set to 30 seconds to see notifications quickly
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

        private bool _disposed;
        private int _notificationCount = 0;
        private bool _updateAvailable = false;
        private DateTime _lastUpdateNotification = DateTime.MinValue;

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
            _loggingService = ServiceLocator.Instance.GetService<ILoggingService>();
        }

        private void Log(string message, string level = "INFO")
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logMessage = $"[{timestamp}] [UpdateChecker] {message}";
            
            // Write to Debug console (for development)
            Debug.WriteLine(logMessage);
            
            // Also write to app's activity log using the logging service
            switch (level.ToUpper())
            {
                case "ERROR":
                    _loggingService?.LogError($"[UpdateChecker] {message}");
                    break;
                case "SUCCESS":
                    _loggingService?.LogSuccess($"[UpdateChecker] {message}");
                    break;
                default:
                    _loggingService?.LogInfo($"[UpdateChecker] {message}");
                    break;
            }
        }

        public void StartPeriodicChecks()
        {
            if (_updateCheckTimer != null)
            {
                Log("Already running - timer already exists", "INFO");
                return;
            }

            Log("STARTING UPDATE CHECKER SERVICE", "INFO");
            Log($"Check interval: {_checkInterval.TotalMinutes:F1} minutes", "INFO");
            Log("Test mode: OFF (Production)", "INFO");
            Log($"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "INFO");
            Log($"Next check at: {DateTime.Now.Add(_checkInterval):yyyy-MM-dd HH:mm:ss}", "INFO");

            // Run first check immediately
            Log("Running initial update check", "INFO");
            _ = CheckForUpdatesAsync();

            // Set up periodic checks
            _updateCheckTimer = new Timer(
                async _ => await CheckForUpdatesAsync(),
                null,
                _checkInterval,
                _checkInterval
            );

        }

        public void StopPeriodicChecks()
        {
            Log("Stopping periodic checks", "INFO");
            _updateCheckTimer?.Dispose();
            _updateCheckTimer = null;
            _notificationCount = 0;
            _updateAvailable = false;
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                Log($"=== Update Check #{_notificationCount + 1} Starting ===", "INFO");
                Log($"Check time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "INFO");

                bool updateDetected = false;
                string versionNumber = "unknown";

                // 🚀 PRODUCTION MODE: Real update check
                Log("Checking for real updates", "INFO");
                await _settingsViewModel.CheckForUpdatesAsync();

                // Read the results from ViewModel
                updateDetected = _settingsViewModel.UpdateAvailable;
                versionNumber = _settingsViewModel.AvailableVersion ?? "unknown";

                Log($"Real check result: Update Available={updateDetected}, Version={versionNumber}", "INFO");

                // If update is available, notify user reasonably
                if (updateDetected)
                {
                    _updateAvailable = true;
                    Log($"Update detected! Version {versionNumber} is available", "SUCCESS");

                    var timeSinceLastNotification = DateTime.Now - _lastUpdateNotification;
                    Log($"Time since last notification: {timeSinceLastNotification.TotalHours:F1} hours", "INFO");

                    if (timeSinceLastNotification.TotalHours >= 24 || _notificationCount == 0)
                    {
                        string message = GetReasonableMessage();

                        Log($"→ Notifying user about update (notification #{_notificationCount + 1})", "INFO");
                        Log($"→ Update version: v{versionNumber}", "INFO");
                        Log($"→ Message: {message}", "INFO");

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
                        Log($"Notification sent successfully. Total notifications: {_notificationCount}", "SUCCESS");
                    }
                    else
                    {
                        Log($"⊗ Update available but not notifying yet (last notification: {_lastUpdateNotification:HH:mm:ss})", "INFO");
                        Log($"  Next notification will be sent after: {_lastUpdateNotification.AddHours(24):yyyy-MM-dd HH:mm:ss}", "INFO");
                    }
                }
                else
                {
                    Log("✗ No updates available - software is up to date", "INFO");

                    // Reset counter if no update is available
                    if (_updateAvailable)
                    {
                        Log("Update was installed! Resetting notification counter.", "SUCCESS");
                        
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
                        Log("Notification state reset", "INFO");
                    }
                }

                Log($"=== Update Check #{_notificationCount + 1} Complete ===", "INFO");
                Log($"Next check scheduled for: {DateTime.Now.Add(_checkInterval):yyyy-MM-dd HH:mm:ss}", "INFO");
                Log("", "INFO"); // Empty line for readability
            }
            catch (Exception ex)
            {
                Log($"ERROR during scheduled check: {ex.Message}", "ERROR");
                Log($"Stack trace: {ex.StackTrace}", "ERROR");
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

            Log("Disposing UpdateCheckerService", "INFO");
            StopPeriodicChecks();
            _disposed = true;
            Log("UpdateCheckerService disposed", "INFO");
        }
    }
}