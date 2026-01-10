using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;

namespace AkademiTrack.Services
{
    public static class MacOSDockHandler
    {
        private static Window? _mainWindow;
        private static DockClickDelegate? _dockClickDelegate;
        private static bool _isFirstCall = true;
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool DockClickDelegate(IntPtr self, IntPtr selector, IntPtr sender, bool hasVisibleWindows);
        
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
                Debug.WriteLine("[MacOSDockHandler] Setting up dock click handler...");
                
                // Get NSApplication
                var nsApp = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                Debug.WriteLine($"[MacOSDockHandler] NSApplication: {nsApp}");
                
                // Get the delegate
                var delegateObj = objc_msgSend(nsApp, sel_registerName("delegate"));
                Debug.WriteLine($"[MacOSDockHandler] Delegate: {delegateObj}");
                
                if (delegateObj != IntPtr.Zero)
                {
                    // Get the delegate's class
                    var delegateClass = objc_msgSend(delegateObj, sel_registerName("class"));
                    
                    // Create our callback
                    _dockClickDelegate = OnDockIconClicked;
                    var methodPtr = Marshal.GetFunctionPointerForDelegate(_dockClickDelegate);
                    
                    // Replace the applicationShouldHandleReopen:hasVisibleWindows: method
                    var selector = sel_registerName("applicationShouldHandleReopen:hasVisibleWindows:");
                    class_replaceMethod(delegateClass, selector, methodPtr, "c@:@c");
                    
                    Debug.WriteLine("[MacOSDockHandler] Successfully replaced dock click handler");
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
            
            // Ignore the first call (app startup)
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
    }
}