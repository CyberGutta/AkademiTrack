using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Service for managing macOS caffinate to keep the system awake during automation
    /// </summary>
    public class MacOSCaffinateService : IDisposable
    {
        private Process? _caffinateProcess;
        private bool _isActive = false;
        private bool _disposed = false;

        /// <summary>
        /// Start caffinate to prevent system sleep
        /// </summary>
        public Task StartCaffinateAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Debug.WriteLine("[Caffinate] Not on macOS - skipping caffinate");
                return Task.CompletedTask;
            }

            if (_isActive)
            {
                Debug.WriteLine("[Caffinate] Already active - skipping");
                return Task.CompletedTask;
            }

            try
            {
                Debug.WriteLine("[Caffinate] Starting caffinate to keep system awake");

                // Start caffinate with options:
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

                _caffinateProcess = Process.Start(startInfo);

                if (_caffinateProcess != null)
                {
                    _isActive = true;
                    Debug.WriteLine($"[Caffinate] ✓ Started successfully (PID: {_caffinateProcess.Id})");
                }
                else
                {
                    Debug.WriteLine("[Caffinate] ❌ Failed to start process");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffinate] ❌ Error starting caffinate: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop caffinate and allow system sleep
        /// </summary>
        public async Task StopCaffinateAsync()
        {
            if (!_isActive || _caffinateProcess == null)
            {
                Debug.WriteLine("[Caffinate] Not active - nothing to stop");
                return;
            }

            try
            {
                Debug.WriteLine("[Caffinate] Stopping caffinate");

                if (!_caffinateProcess.HasExited)
                {
                    _caffinateProcess.Kill();
                    await Task.Run(() => _caffinateProcess.WaitForExit(5000)); // Wait max 5 seconds
                }

                _caffinateProcess.Dispose();
                _caffinateProcess = null;
                _isActive = false;

                Debug.WriteLine("[Caffinate] ✓ Stopped successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffinate] ❌ Error stopping caffinate: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if caffinate is currently active
        /// </summary>
        public bool IsActive => _isActive && _caffinateProcess != null && !_caffinateProcess.HasExited;

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_isActive)
                {
                    // Synchronous stop for disposal
                    StopCaffinateAsync().Wait(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Caffinate] Error during disposal: {ex.Message}");
            }

            _disposed = true;
        }
    }
}