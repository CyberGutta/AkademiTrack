using System;
using System.Runtime.InteropServices;
using Avalonia;
using Velopack;
using OsNotifications;

namespace AkademiTrack
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // MUST be first - Velopack initialization
            VelopackApp.Build().Run();

            // Initialize notifications BEFORE Avalonia (critical for macOS)
            InitializeNotifications();

            // Then start Avalonia app
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static void InitializeNotifications()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Notifications.BundleIdentifier = "com.apple.finder";
                    Notifications.SetGuiApplication(true);
                    Console.WriteLine("✓ macOS notifications initialized");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows notifications work automatically
                    Console.WriteLine("✓ Windows notifications ready");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Console.WriteLine("✓ Linux notifications ready");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Notification initialization warning: {ex.Message}");
                // Don't crash the app if notifications fail to initialize
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}