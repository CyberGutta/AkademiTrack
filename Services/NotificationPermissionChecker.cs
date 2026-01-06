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
                // Check if notifications are actually enabled first
                var isEnabled = await CheckIfNotificationsEnabled();
                if (isEnabled)
                {
                    Debug.WriteLine("[PermissionChecker] Notifications are enabled in system");
                    return PermissionStatus.Authorized;
                }

                // If not enabled, check if user has dismissed the dialog before
                var hasDismissed = await HasDismissedDialog();
                if (hasDismissed)
                {
                    Debug.WriteLine("[PermissionChecker] User has dismissed dialog before, but notifications still not enabled - returning Denied");
                    return PermissionStatus.Denied; // User dismissed but notifications still not enabled
                }

                Debug.WriteLine("[PermissionChecker] Notifications not enabled and user hasn't dismissed - should show dialog");
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
                Debug.WriteLine("[PermissionChecker] Starting notification permission check...");
                
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
                    var error = (await process.StandardError.ReadToEndAsync()).Trim();

                    Debug.WriteLine($"[PermissionChecker] First check output: '{output}', error: '{error}'");

                    if (output == "enabled")
                    {
                        Debug.WriteLine("[PermissionChecker] Found enabled notification settings (flags = 16)");
                        return true;
                    }
                }

                // More specific check: Look for the app with enabled flags
                var detailedCheckInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -A 10 -B 2 'AkademiTrack' | grep -E 'flags.*=.*(16|48|80)' && echo 'enabled' || echo 'disabled'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var detailedProcess = Process.Start(detailedCheckInfo);
                if (detailedProcess != null)
                {
                    await detailedProcess.WaitForExitAsync();
                    var detailedOutput = (await detailedProcess.StandardOutput.ReadToEndAsync()).Trim();
                    var detailedError = (await detailedProcess.StandardError.ReadToEndAsync()).Trim();

                    Debug.WriteLine($"[PermissionChecker] Detailed check output: '{detailedOutput}', error: '{detailedError}'");

                    if (detailedOutput == "enabled")
                    {
                        Debug.WriteLine("[PermissionChecker] Found enabled notification settings (detailed check)");
                        return true;
                    }
                }

                // Debug: Let's see what's actually in the notification preferences
                var debugInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -A 10 -B 2 'AkademiTrack' || echo 'AkademiTrack not found in notification preferences'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var debugProcess = Process.Start(debugInfo);
                if (debugProcess != null)
                {
                    await debugProcess.WaitForExitAsync();
                    var debugOutput = (await debugProcess.StandardOutput.ReadToEndAsync()).Trim();
                    Debug.WriteLine($"[PermissionChecker] Debug - AkademiTrack in notification prefs: {debugOutput}");
                }

                Debug.WriteLine("[PermissionChecker] No enabled notification settings found");
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
        /// Resets the dismissed dialog flag so the user can see the permission dialog again
        /// </summary>
        public static async Task ResetDialogDismissedAsync()
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
                    try
                    {
                        var settings = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json) 
                            ?? new System.Collections.Generic.Dictionary<string, object>();
                        
                        settings.Remove("NotificationDialogDismissed");
                        
                        string updatedJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
                        { 
                            WriteIndented = true 
                        });
                        
                        await File.WriteAllTextAsync(settingsPath, updatedJson);
                        Debug.WriteLine("[PermissionChecker] Reset dialog dismissed flag");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PermissionChecker] Error parsing settings file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error resetting dialog dismissed: {ex.Message}");
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