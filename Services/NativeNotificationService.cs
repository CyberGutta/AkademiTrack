using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;

namespace AkademiTrack.Services
{
    public static class NativeNotificationService
    {
        public static async Task ShowAsync(string title, string message = "", string level = "INFO")
        {
            try
            {
                // Check if notifications are enabled
                if (!await AreNotificationsEnabledAsync())
                {
                    Debug.WriteLine($"[NOTIFICATION SKIPPED] {title}: {message}");
                    return;
                }

                string icon = level switch
                {
                    "SUCCESS" => "",
                    "ERROR" => "",
                    "WARNING" => "",
                    _ => ""

                    //"SUCCESS" => "✅",
                    //"ERROR" => "❌",
                    //"WARNING" => "⚠️",
                    //_ => "ℹ️"
                };

                string fullTitle = $"{icon} {title}";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await ShowMacNotificationAsync(fullTitle, message);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ShowWindowsNotificationAsync(fullTitle, message);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await ShowLinuxNotificationAsync(fullTitle, message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        public static void Show(string title, string message = "", string level = "INFO")
        {
            _ = ShowAsync(title, message, level);
        }

        private static async Task<bool> AreNotificationsEnabledAsync()
        {
            try
            {
                var settings = await AkademiTrack.ViewModels.SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
                return settings.EnableNotifications;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking notification settings: {ex.Message}");
                return true; // Default to enabled if we can't read settings
            }
        }

        private static async Task ShowMacNotificationAsync(string title, string message)
        {
            try
            {
                string helperPath;

                var bundledPath = Path.Combine(AppContext.BaseDirectory, "AkademiTrackHelper.app", "Contents", "MacOS", "AkademiTrackHelper");
                var devPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Helpers", "AkademiTrack.app", "Contents", "MacOS", "AkademiTrackHelper");

                helperPath = File.Exists(bundledPath) ? bundledPath : devPath;

                if (!File.Exists(helperPath))
                {
                    Console.WriteLine("❌ AkademiTrackHelper not found");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                startInfo.ArgumentList.Add(title);
                startInfo.ArgumentList.Add(message);

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await process.WaitForExitAsync();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to launch AkademiTrackHelper: {ex.Message}");
            }
        }

        private static async Task ShowWindowsNotificationAsync(string title, string message)
        {
            try
            {
                OsNotifications.Notifications.ShowNotification(title, message);
                await Task.Delay(1000);
                Console.WriteLine($"✓ Windows notification shown: {title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows notification failed, trying PowerShell fallback: {ex.Message}");

                try
                {
                    var escapedTitle = title.Replace("'", "''");
                    var escapedMessage = message.Replace("'", "''");

                    var psScript = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

$APP_ID = 'AkademiTrack'
$template = @""
<toast>
  <visual>
    <binding template='ToastGeneric'>
      <image placement='appLogoOverride' src='file:///C:/Program Files/AkademiTrack/Assets/AT-1024.png'/>
      <text>{{0}}</text>
      <text>{{1}}</text>
    </binding>
  </visual>
</toast>
""@

$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml(($template -f '{escapedTitle}', '{escapedMessage}'))
$toast = New-Object Windows.UI.Notifications.ToastNotification $xml
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($APP_ID).Show($toast)
";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception psEx)
                {
                    Console.WriteLine($"PowerShell fallback also failed: {psEx.Message}");
                }
            }
        }

        private static async Task ShowLinuxNotificationAsync(string title, string message)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/notify-send",
                    Arguments = $"\"{title}\" \"{message}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    Console.WriteLine($"✓ Linux notification shown: {title}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Linux notification failed: {ex.Message}");

                try
                {
                    OsNotifications.Notifications.ShowNotification(title, message);
                }
                catch (Exception osEx)
                {
                    Console.WriteLine($"OsNotifications fallback failed: {osEx.Message}");
                }
            }
        }
    }
}