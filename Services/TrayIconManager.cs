using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AkademiTrack.Services
{
    public static class TrayIconManager
    {
        private static TrayIcon? _trayIcon;
        private static Window? _mainWindow;

        public static void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;

            try
            {
                Avalonia.Controls.WindowIcon? windowIcon = mainWindow.Icon;

                if (windowIcon == null)
                {
                    var possiblePaths = new[]
                    {
                        "avares://AkademiTrack/Assets/AT-1024.ico",
                        "/Assets/AT-1024.ico",
                        "avares://AkademiTrack/Assets/AT-Transparrent.png",
                        "/Assets/AT-Transparrent.png",
                        "Assets/AT-Transparrent.png"
                    };

                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            windowIcon = new Avalonia.Controls.WindowIcon(path);
                            Debug.WriteLine($"✓ Successfully loaded tray icon from: {path}");
                            break;
                        }
                        catch (Exception)
                        {
                            Debug.WriteLine($"⚠️ Could not load icon from: {path}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("✓ Using window icon for tray");
                }

                _trayIcon = new TrayIcon
                {
                    Icon = windowIcon,
                    ToolTipText = "AkademiTrack",
                    IsVisible = false 
                };

                var menu = new NativeMenu();

                var showItem = new NativeMenuItem("Vis vindu");
                showItem.Click += (s, e) => ShowMainWindow();
                menu.Add(showItem);

                menu.Add(new NativeMenuItemSeparator());

                var exitItem = new NativeMenuItem("Avslutt");
                exitItem.Click += (s, e) => ExitApplication();
                menu.Add(exitItem);

                _trayIcon.Menu = menu;

                _trayIcon.Clicked += (s, e) => ShowMainWindow();

                Debug.WriteLine("✓ System tray icon initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Could not initialize tray icon: {ex.Message}");
            }
        }

        public static void ShowTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = true;
                Debug.WriteLine("Tray icon shown");
            }
        }

        public static void HideTrayIcon()
        {
            if (_trayIcon != null)
            {
               
                Debug.WriteLine("Tray icon kept visible");
            }
        }

        public static void MinimizeToTray()
        {
            if (_mainWindow != null)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    _mainWindow.WindowState = WindowState.Minimized;
                }
                else
                {
                    _mainWindow.Hide();
                }

                ShowTrayIcon();
                Debug.WriteLine("Application minimized to tray");
            }
        }

        private static void ShowMainWindow()
        {
            if (_mainWindow != null)
            {
                Debug.WriteLine("Restoring window from tray...");
                
                try
                {
                    _mainWindow.ShowInTaskbar = true;
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                    
                    Debug.WriteLine("✓ Main window restored from tray");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error restoring window: {ex.Message}");
                }
            }
        }

        private static void ExitApplication()
        {
            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exiting application: {ex.Message}");
            }
        }

        public static void Dispose()
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}