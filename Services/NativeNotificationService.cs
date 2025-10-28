using OsNotifications;
using System;
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
                

                string prefixedTitle = level switch
                {
                    "SUCCESS" => $"✓ {title}",
                    "ERROR" => $"✕ {title}",
                    "WARNING" => $"⚠ {title}",
                    _ => title
                };

                Notifications.ShowNotification(prefixedTitle, message);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await Task.Delay(100);
                }
            }
            catch (PlatformNotSupportedException)
            {
                Console.WriteLine($"[{level}] {title}: {message}");
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
    }
}