using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Service for managing macOS caffeinate to keep the system awake during automation
    /// </summary>
    public class MacOSCaffeinateService : IDisposable
    {
        private Process? _caffeinateProcess;
        private bool _isActive = false;
        private bool _disposed = false;

        /// <summary>
        /// Start caffeinate to prevent system sleep
        /// </summary>
        public Task StartCaffeinateAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Debug.WriteLine("[Caffeinate] Not on macOS - skipping caffeinate");
                return Task.CompletedTask;
            }

            if (_isActive)
            {
                Debug.WriteLine("[Caffeinate] Already active - skipping");
                return Task.CompletedTask;
            }

            try
            {
                Debug.WriteLine("[Caffeinate] Starting caffeinate to keep system awake");

                // Start caffeinate with options:
                // -d: prevent display sleep
                // -i: prevent idle sleep
                // -s: prevent system sleep
                var startInfo = new ProcessStartInfo
                {
                    FileName = "caffeinate",
                    Arguments = "-dis",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _caffeinateProcess = Process.Start(startInfo);

                if (_caffeinateProcess != null)
                {
                    _isActive = true;
                    Debug.WriteLine($"[Caffeinate] ✓ Started successfully (PID: {_caffeinateProcess.Id})");
                }
                else
                {
                    Debug.WriteLine("[Caffeinate] ❌ Failed to start process");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffeinate] ❌ Error starting caffeinate: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop caffeinate and allow system sleep
        /// </summary>
        public async Task StopCaffeinateAsync()
        {
            if (!_isActive || _caffeinateProcess == null)
            {
                Debug.WriteLine("[Caffeinate] Not active - nothing to stop");
                return;
            }

            try
            {
                Debug.WriteLine("[Caffeinate] Stopping caffeinate");

                if (!_caffeinateProcess.HasExited)
                {
                    _caffeinateProcess.Kill();
                    await Task.Run(() => _caffeinateProcess.WaitForExit(5000)); // Wait max 5 seconds
                }

                _caffeinateProcess.Dispose();
                _caffeinateProcess = null;
                _isActive = false;

                Debug.WriteLine("[Caffeinate] ✓ Stopped successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffeinate] ❌ Error stopping caffeinate: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if caffeinate is currently active
        /// </summary>
        public bool IsActive => _isActive && _caffeinateProcess != null && !_caffeinateProcess.HasExited;

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_isActive)
                {
                    // Synchronous stop for disposal
                    StopCaffeinateAsync().Wait(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffeinate] Error during disposal: {ex.Message}");
            }

            _disposed = true;
        }
    }
}