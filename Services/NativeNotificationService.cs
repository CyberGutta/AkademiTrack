using OsNotifications;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public static class NativeNotificationService
    {
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize the notification system. Call this once at app startup.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // For macOS: Set bundle identifier (optional for non-bundled apps)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // You can use Finder's identifier if not bundled
                    Notifications.BundleIdentifier = "com.apple.finder";
                    // Set to true if this is a GUI app (not console)
                    Notifications.SetGuiApplication(true);
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize notifications: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a native OS notification
        /// </summary>
        public static async Task ShowAsync(string title, string message = "", string level = "INFO")
        {
            try
            {
                // Ensure initialization
                if (!_isInitialized)
                {
                    Initialize();
                }

                // Add emoji/icon based on level
                string prefixedTitle = level switch
                {
                    "SUCCESS" => $"✓ {title}",
                    "ERROR" => $"✕ {title}",
                    "WARNING" => $"⚠ {title}",
                    _ => title
                };

                // Show the notification
                Notifications.ShowNotification(prefixedTitle, message);

                // On Windows, add small delay to ensure notification shows before app might exit
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await Task.Delay(100);
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback: log to console if platform doesn't support notifications
                Console.WriteLine($"[{level}] {title}: {message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show notification synchronously (for simple cases)
        /// </summary>
        public static void Show(string title, string message = "", string level = "INFO")
        {
            _ = ShowAsync(title, message, level);
        }
    }
}