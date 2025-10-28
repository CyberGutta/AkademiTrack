using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public static class NativeNotificationService
    {
        /// <summary>
        /// Show a native OS notification using the most reliable method for each platform
        /// </summary>
        public static async Task ShowAsync(string title, string message = "", string level = "INFO")
        {
            try
            {
                // Add emoji based on level
                string icon = level switch
                {
                    "SUCCESS" => "✓",
                    "ERROR" => "✕",
                    "WARNING" => "⚠",
                    _ => "ℹ️"
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

        private static async Task ShowMacNotificationAsync(string title, string message)
        {
            try
            {
                // Clean the strings - remove problematic characters
                title = title.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");
                message = message.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");

                // Use osascript with proper argument passing (not string interpolation)
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Pass script as separate arguments to avoid quote issues
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add($"display notification \"{message}\" with title \"AkademiTrack\" subtitle \"{title}\"");

                Console.WriteLine($"Running osascript with title: {title}, message: {message}");

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        Console.WriteLine($"osascript error: {error}");

                        // Try simpler version without subtitle if it fails
                        await ShowSimpleMacNotificationAsync(title + ": " + message);
                    }
                    else
                    {
                        Console.WriteLine($"✓ macOS notification shown: {title}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"macOS notification failed: {ex.Message}");
            }
        }

        private static async Task ShowSimpleMacNotificationAsync(string text)
        {
            try
            {
                text = text.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add($"display notification \"{text}\" with title \"AkademiTrack\"");

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine($"✓ Simple macOS notification shown");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Simple notification also failed: {ex.Message}");
            }
        }

        private static async Task ShowWindowsNotificationAsync(string title, string message)
        {
            try
            {
                // Use OsNotifications for Windows since it works well there
                OsNotifications.Notifications.ShowNotification(title, message);
                await Task.Delay(1000); // Windows needs delay
                Console.WriteLine($"✓ Windows notification shown: {title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows notification failed, trying PowerShell fallback: {ex.Message}");

                // Fallback: PowerShell toast notification
                try
                {
                    var escapedTitle = title.Replace("'", "''");
                    var escapedMessage = message.Replace("'", "''");

                    var psScript = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

$APP_ID = 'AkademiTrack'
$template = @""<toast><visual><binding template='ToastText02'><text id='1'>{0}</text><text id='2'>{1}</text></binding></visual></toast>""@

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
                // Use notify-send (standard on most Linux distros)
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

                // Fallback: Try OsNotifications
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