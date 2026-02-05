using System;
using Avalonia;

namespace AkademiTrack
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Start Avalonia app
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}