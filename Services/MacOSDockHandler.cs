using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Diagnostics;

namespace AkademiTrack.Services
{
    public static class MacOSDockHandler
    {
        private static Window? _mainWindow;
        private static DockClickDelegate? _dockClickDelegate;
        private static ApplicationShouldTerminateDelegate? _shouldTerminateDelegate;
        private static bool _isFirstCall = true;
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool DockClickDelegate(IntPtr self, IntPtr selector, IntPtr sender, bool hasVisibleWindows);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ApplicationShouldTerminateDelegate(IntPtr self, IntPtr selector, IntPtr sender);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr sel_registerName(string name);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

        public static void Initialize(Window mainWindow)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;

            _mainWindow = mainWindow;
            
            try
            {
                Debug.WriteLine("[MacOSDockHandler] Setting up dock handlers");
                
                var nsApp = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                Debug.WriteLine($"[MacOSDockHandler] NSApplication: {nsApp}");
                

                var delegateObj = objc_msgSend(nsApp, sel_registerName("delegate"));
                Debug.WriteLine($"[MacOSDockHandler] Delegate: {delegateObj}");
                
                if (delegateObj != IntPtr.Zero)
                {
                    var delegateClass = objc_msgSend(delegateObj, sel_registerName("class"));
                    
                    _dockClickDelegate = OnDockIconClicked;
                    var clickMethodPtr = Marshal.GetFunctionPointerForDelegate(_dockClickDelegate);
                    var clickSelector = sel_registerName("applicationShouldHandleReopen:hasVisibleWindows:");
                    class_replaceMethod(delegateClass, clickSelector, clickMethodPtr, "c@:@c");
                    Debug.WriteLine("[MacOSDockHandler] ✓ Dock click handler installed");
                    
                    _shouldTerminateDelegate = OnApplicationShouldTerminate;
                    var terminateMethodPtr = Marshal.GetFunctionPointerForDelegate(_shouldTerminateDelegate);
                    var terminateSelector = sel_registerName("applicationShouldTerminate:");
                    class_replaceMethod(delegateClass, terminateSelector, terminateMethodPtr, "i@:@");
                    Debug.WriteLine("[MacOSDockHandler] ✓ Termination handler installed");
                }
                else
                {
                    Debug.WriteLine("[MacOSDockHandler] WARNING: No delegate found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacOSDockHandler] Failed to initialize: {ex.Message}");
                Debug.WriteLine($"[MacOSDockHandler] Stack: {ex.StackTrace}");
            }
        }

        private static bool OnDockIconClicked(IntPtr self, IntPtr selector, IntPtr sender, bool hasVisibleWindows)
        {
            Debug.WriteLine("[MacOSDockHandler] DOCK ICON CLICKED!");
            Debug.WriteLine($"[MacOSDockHandler] hasVisibleWindows: {hasVisibleWindows}");
            Debug.WriteLine($"[MacOSDockHandler] isFirstCall: {_isFirstCall}");
            
            if (_isFirstCall)
            {
                Debug.WriteLine("[MacOSDockHandler] Ignoring first call (app startup)");
                _isFirstCall = false;
                return true;
            }
            
            Dispatcher.UIThread.Post(() =>
            {
                if (_mainWindow != null)
                {
                    Debug.WriteLine($"[MacOSDockHandler] Window visible: {_mainWindow.IsVisible}");
                    Debug.WriteLine($"[MacOSDockHandler] Window state: {_mainWindow.WindowState}");
                    
                    _mainWindow.ShowInTaskbar = true;
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                    _mainWindow.BringIntoView();
                    
                    Debug.WriteLine("[MacOSDockHandler] Window shown!");
                }
            });
            
            return true;
        }

        private static int OnApplicationShouldTerminate(IntPtr self, IntPtr selector, IntPtr sender)
        {
            Debug.WriteLine("[MacOSDockHandler] APPLICATION SHOULD TERMINATE - Quit requested from dock menu");
            
            // NSTerminateNow = 1 (allow immediate termination)
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        Debug.WriteLine("[MacOSDockHandler] Initiating app shutdown");
                        
                        if (Avalonia.Application.Current is App app)
                        {
                            var field = app.GetType().GetField("_isShuttingDown", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            field?.SetValue(app, true);
                            Debug.WriteLine("[MacOSDockHandler] ✓ Set _isShuttingDown flag");
                        }
                        
                        // Dispose tray icon
                        TrayIconManager.Dispose();
                        Debug.WriteLine("[MacOSDockHandler] ✓ Disposed tray icon");
                        
                        // Shutdown desktop lifetime
                        desktop.Shutdown();
                        Debug.WriteLine("[MacOSDockHandler] ✓ Shutdown called");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MacOSDockHandler] Error during termination: {ex.Message}");
                }
            });
            
            return 1; // NSTerminateNow
        }
    }
}