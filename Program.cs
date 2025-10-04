using System;
using Avalonia;
using Velopack;

namespace AkademiTrack
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // MUST be first - Velopack initialization
            VelopackApp.Build().Run();

            // Then start Avalonia app
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}