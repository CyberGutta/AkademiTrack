using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public static class NotificationPermissionChecker
    {
        // Use a SEPARATE file for notification dialog state so it persists across app updates
        private static string NotificationStatePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AkademiTrack",
            "notification_state.json"
        );

        public enum PermissionStatus
        {
            NotDetermined,
            Denied,
            Authorized,
            Unknown
        }


        public static async Task<PermissionStatus> CheckMacNotificationPermissionAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return PermissionStatus.Authorized;
            }

            try
            {
                Debug.WriteLine("[PermissionChecker] ========== CheckMacNotificationPermissionAsync ==========");
                
                var hasDismissed = await HasDismissedDialog();
                Debug.WriteLine($"[PermissionChecker] HasDismissedDialog result: {hasDismissed}");
                
                if (hasDismissed)
                {
                    Debug.WriteLine("[PermissionChecker] User has dismissed dialog before, not showing again");
                    return PermissionStatus.Authorized;
                }

                var isEnabled = await CheckIfNotificationsEnabled();
                Debug.WriteLine($"[PermissionChecker] CheckIfNotificationsEnabled result: {isEnabled}");
                
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
            Debug.WriteLine("[PermissionChecker] ========== CheckIfNotificationsEnabled ==========");
            
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"defaults read ~/Library/Preferences/com.apple.ncprefs.plist 2>/dev/null | grep -A 5 'AkademiTrack' | grep -q 'flags = 16' && echo 'enabled' || echo 'disabled'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Debug.WriteLine("[PermissionChecker] Running check: flags = 16");
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();

                    Debug.WriteLine($"[PermissionChecker] Check output: '{output}'");

                    if (output == "enabled")
                    {
                        Debug.WriteLine("[PermissionChecker] Found enabled notification settings (flags = 16)");
                        await MarkDialogDismissedAsync();
                        return true;
                    }
                }

                Debug.WriteLine("[PermissionChecker] ✗ Notifications NOT enabled");
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
            Debug.WriteLine("[PermissionChecker] ========== HasDismissedDialog ==========");
            
            try
            {
                Debug.WriteLine($"[PermissionChecker] Checking state file: {NotificationStatePath}");
                Debug.WriteLine($"[PermissionChecker] File exists: {File.Exists(NotificationStatePath)}");

                if (File.Exists(NotificationStatePath))
                {
                    string json = await File.ReadAllTextAsync(NotificationStatePath);
                    Debug.WriteLine($"[PermissionChecker] State content: {json}");
                    
                    bool hasDismissed = json.Contains("\"dismissed\"") && json.Contains("true");
                    Debug.WriteLine($"[PermissionChecker] HasDismissed result: {hasDismissed}");
                    
                    return hasDismissed;
                }
                
                Debug.WriteLine("[PermissionChecker] State file does not exist - returning false");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error checking if dismissed: {ex.Message}");
            }
            return false;
        }

        public static async Task MarkDialogDismissedAsync()
        {
            Debug.WriteLine("[PermissionChecker] ========== MarkDialogDismissedAsync ==========");
            
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                
                if (!Directory.Exists(appDataDir))
                {
                    Debug.WriteLine($"[PermissionChecker] Creating directory: {appDataDir}");
                    Directory.CreateDirectory(appDataDir);
                }

                Debug.WriteLine($"[PermissionChecker] State file path: {NotificationStatePath}");
                
                var state = new
                {
                    dismissed = true,
                    timestamp = DateTime.UtcNow.ToString("O")
                };
                
                string json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                Debug.WriteLine($"[PermissionChecker] Writing state: {json}");
                await File.WriteAllTextAsync(NotificationStatePath, json);
                Debug.WriteLine("[PermissionChecker] Marked dialog as permanently dismissed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error marking dialog dismissed: {ex.Message}");
            }
        }

 
        public static void OpenMacNotificationSettings()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return;

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

        public static async Task<bool> ShouldShowPermissionDialogAsync()
        {
            Debug.WriteLine("[PermissionChecker] ========== ShouldShowPermissionDialogAsync ==========");
            
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Debug.WriteLine("[PermissionChecker] Not macOS, skipping dialog");
                return false;
            }

            try
            {
                var hasDismissed = await HasDismissedDialog();
                Debug.WriteLine($"[PermissionChecker] HasDismissed (user permanently dismissed): {hasDismissed}");
                
                if (hasDismissed)
                {
                    Debug.WriteLine("[PermissionChecker] User permanently dismissed dialog");
                    return false;
                }

                var isEnabled = await CheckIfNotificationsEnabled();
                Debug.WriteLine($"[PermissionChecker] IsEnabled (system has notifications on): {isEnabled}");
                
                if (isEnabled)
                {
                    Debug.WriteLine("[PermissionChecker] Notifications already enabled in system");
                    return false;
                }

                Debug.WriteLine("[PermissionChecker] ✅ SHOULD SHOW DIALOG - user needs to enable notifications");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PermissionChecker] Error: {ex.Message}");
                return false;
            }
        }

        public static async Task RequestPermissionAsync()
        {
            Debug.WriteLine("[PermissionChecker] ========== RequestPermissionAsync ==========");
            
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Debug.WriteLine("[PermissionChecker] Not macOS, skipping");
                return;
            }

            try
            {
                Debug.WriteLine("[PermissionChecker] Sending test notification");
                
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