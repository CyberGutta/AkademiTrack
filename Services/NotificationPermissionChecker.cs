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
                // First check: Has user dismissed the dialog before?
                var hasDismissed = await HasDismissedDialog();
                if (hasDismissed)
                {
                    Debug.WriteLine("[PermissionChecker] User has dismissed dialog before, not showing again");
                    return PermissionStatus.Authorized; // Don't bother them again
                }

                // Second check: Try to detect if notifications are actually enabled
                var isEnabled = await CheckIfNotificationsEnabled();
                if (isEnabled)
                {
                    Debug.WriteLine("[PermissionChecker] Notifications are enabled in system");
                    return PermissionStatus.Authorized;
                }

                Debug.WriteLine("[PermissionChecker] Notifications not enabled, should show dialog");
                return PermissionStatus.NotDetermined;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error checking notification permission: {ex.Message}");
                return PermissionStatus.Unknown;
            }
        }

        private static async Task<bool> CheckIfNotificationsEnabled()
        {
            try
            {
                // Check macOS notification database to see if app is registered and enabled
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -A 5 'AkademiTrack' | grep -q 'flags = 16' && echo 'enabled' || echo 'disabled'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();

                    if (output == "enabled")
                    {
                        Debug.WriteLine("[PermissionChecker] Found enabled notification settings");
                        return true;
                    }
                }

                // Fallback: Check if app exists in notification center at all
                var checkExistsInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -q 'AkademiTrack' && echo 'found' || echo 'notfound'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var existsProcess = Process.Start(checkExistsInfo);
                if (existsProcess != null)
                {
                    await existsProcess.WaitForExitAsync();
                    var existsOutput = (await existsProcess.StandardOutput.ReadToEndAsync()).Trim();

                    if (existsOutput == "found")
                    {
                        Debug.WriteLine("[PermissionChecker] App found in notification center (assuming enabled)");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error checking if notifications enabled: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> HasDismissedDialog()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string settingsPath = Path.Combine(appDataDir, "settings.json");

                if (File.Exists(settingsPath))
                {
                    string json = await File.ReadAllTextAsync(settingsPath);
                    return json.Contains("\"NotificationDialogDismissed\"") && json.Contains("true");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error checking if dismissed: {ex.Message}");
            }
            return false;
        }

        public static async Task MarkDialogDismissedAsync()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                
                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                string settingsPath = Path.Combine(appDataDir, "settings.json");
                
                var settings = new System.Collections.Generic.Dictionary<string, object>();
                
                if (File.Exists(settingsPath))
                {
                    string json = await File.ReadAllTextAsync(settingsPath);
                    try
                    {
                        settings = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json) 
                            ?? new System.Collections.Generic.Dictionary<string, object>();
                    }
                    catch
                    {
                        // If deserialization fails, start fresh
                        settings = new System.Collections.Generic.Dictionary<string, object>();
                    }
                }

                settings["NotificationDialogDismissed"] = true;
                
                string updatedJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(settingsPath, updatedJson);
                Debug.WriteLine("[PermissionChecker] Marked dialog as dismissed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error marking dialog dismissed: {ex.Message}");
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
                Debug.WriteLine($"[PermissionChecker] Failed to open notification settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines if the permission dialog should be shown to the user
        /// </summary>
        public static async Task<bool> ShouldShowPermissionDialogAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Debug.WriteLine("[PermissionChecker] Not macOS, skipping dialog");
                return false; // Only show on macOS
            }

            try
            {
                // First check: Has user dismissed the dialog before?
                var hasDismissed = await HasDismissedDialog();
                if (hasDismissed)
                {
                    Debug.WriteLine("[PermissionChecker] User has dismissed dialog before, not showing again");
                    return false;
                }

                // Second check: Are notifications already enabled?
                var isEnabled = await CheckIfNotificationsEnabled();
                if (isEnabled)
                {
                    Debug.WriteLine("[PermissionChecker] Notifications already enabled, no need to show dialog");
                    return false;
                }

                Debug.WriteLine("[PermissionChecker] Should show permission dialog");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error checking if should show dialog: {ex.Message}");
                return false; // Don't show dialog if we can't determine
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
                
                Debug.WriteLine("[PermissionChecker] Test notification sent");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Failed to request notification permission: {ex.Message}");
            }
        }
    }
}