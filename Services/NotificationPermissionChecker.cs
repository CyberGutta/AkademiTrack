using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public static class NotificationPermissionChecker
    {
        public enum PermissionStatus
        {
            NotDetermined,
            Denied,
            Authorized,
            Unknown
        }

        /// <summary>
        /// Checks if notification permissions are granted on macOS
        /// </summary>
        public static async Task<PermissionStatus> CheckMacNotificationPermissionAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return PermissionStatus.Authorized; // Not macOS, assume OK
            }

            try
            {
                // Simple approach: Try to send a test notification and check if helper exists
                // If the helper app doesn't exist or fails, assume permission needed
                var helperPath = Path.Combine(AppContext.BaseDirectory, "AkademiTrackHelper.app", "Contents", "MacOS", "AkademiTrackHelper");
                var devPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Helpers", "AkademiTrack.app", "Contents", "MacOS", "AkademiTrackHelper");

                var finalPath = File.Exists(helperPath) ? helperPath : devPath;

                if (!File.Exists(finalPath))
                {
                    // Helper not found - can't send notifications
                    return PermissionStatus.NotDetermined;
                }

                // Check if notification center settings exist for our app
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read com.apple.ncprefs.plist 2>/dev/null | grep -q 'AkademiTrackHelper' && echo 'found' || echo 'notfound'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return PermissionStatus.Unknown;
                }

                await process.WaitForExitAsync();
                var output = (await process.StandardOutput.ReadToEndAsync()).Trim();

                // If settings found, assume authorized (they've interacted with it)
                // If not found, it's likely first time
                return output == "found" ? PermissionStatus.Authorized : PermissionStatus.NotDetermined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking notification permission: {ex.Message}");
                return PermissionStatus.Unknown;
            }
        }

        /// <summary>
        /// Opens macOS System Settings to the Notifications page
        /// </summary>
        public static void OpenMacNotificationSettings()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return;

                // Open System Settings > Notifications
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "x-apple.systempreferences:com.apple.preference.notifications",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open notification settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Request notification permission by showing a test notification
        /// This will trigger the macOS permission prompt if not determined
        /// </summary>
        public static async Task RequestPermissionAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;

            try
            {
                // Send a test notification to trigger permission prompt
                await NativeNotificationService.ShowAsync(
                    "Varsler Aktivert",
                    "AkademiTrack kan nå sende deg viktige varsler",
                    "SUCCESS"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to request notification permission: {ex.Message}");
            }
        }
    }
}