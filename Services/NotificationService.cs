using AkademiTrack.ViewModels;
using Avalonia.Threading;
using System;

namespace AkademiTrack.Services
{
    public static class NotificationService
    {
        /// <summary>
        /// Shows a notification using macOS native notifications if possible.
        /// Falls back to overlay window for other platforms.
        /// </summary>
        public static void Show(string title, string message, string level = "INFO")
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // macOS native notification via AppleScript
                    var escapedTitle = EscapeForAppleScript(title);
                    var escapedMessage = EscapeForAppleScript(message);

                    System.Diagnostics.Process.Start("osascript",
                        $"-e 'display notification \"{escapedMessage}\" with title \"{escapedTitle}\"'");
                    return; // stop here if successful
                }
            }
            catch
            {
                // If macOS native notification fails, fallback to overlay
            }

            // Fallback: overlay for Windows, Linux, or failed macOS
            Dispatcher.UIThread.Post(() =>
            {
                var overlay = new NotificationOverlayWindow(title, message, level);
                overlay.Show();
            });
        }

        private static string EscapeForAppleScript(string text)
        {
            // Escape quotes for osascript
            return text.Replace("\"", "\\\"");
        }
    }
}
