using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AkademiTrack.Services
{
    public class ResourceMonitor : INotifyPropertyChanged, IDisposable
    {
        private static ResourceMonitor? _instance;
        public static ResourceMonitor Instance => _instance ??= new ResourceMonitor();

        private readonly Process _currentProcess;
        private Timer? _updateTimer;
        private bool _isMonitoring;

        private DateTime _lastCpuCheck;
        private TimeSpan _lastTotalProcessorTime;

        private double _cpuUsage;
        private long _memoryUsageMB;
        private long _totalMemoryMB;
        private double _memoryPercentage;
        private TimeSpan _uptime;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double CpuUsage
        {
            get => _cpuUsage;
            private set
            {
                if (Math.Abs(_cpuUsage - value) > 0.01)
                {
                    _cpuUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public long MemoryUsageMB
        {
            get => _memoryUsageMB;
            private set
            {
                if (_memoryUsageMB != value)
                {
                    _memoryUsageMB = value;
                    OnPropertyChanged();
                }
            }
        }

        public long TotalMemoryMB
        {
            get => _totalMemoryMB;
            private set
            {
                if (_totalMemoryMB != value)
                {
                    _totalMemoryMB = value;
                    OnPropertyChanged();
                }
            }
        }

        public double MemoryPercentage
        {
            get => _memoryPercentage;
            private set
            {
                if (Math.Abs(_memoryPercentage - value) > 0.01)
                {
                    _memoryPercentage = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan Uptime
        {
            get => _uptime;
            private set
            {
                if (_uptime != value)
                {
                    _uptime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UptimeFormatted));
                }
            }
        }

        public string UptimeFormatted
        {
            get
            {
                if (Uptime.TotalHours >= 1)
                    return $"{(int)Uptime.TotalHours}t {Uptime.Minutes}m";
                else if (Uptime.TotalMinutes >= 1)
                    return $"{(int)Uptime.TotalMinutes}m {Uptime.Seconds}s";
                else
                    return $"{Uptime.Seconds}s";
            }
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set
            {
                if (_isMonitoring != value)
                {
                    _isMonitoring = value;
                    OnPropertyChanged();
                }
            }
        }

        private ResourceMonitor()
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastCpuCheck = DateTime.UtcNow;
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;

            // Get total system memory
            try
            {
                var memInfo = GC.GetGCMemoryInfo();
                TotalMemoryMB = memInfo.TotalAvailableMemoryBytes / 1024 / 1024;
            }
            catch
            {
                TotalMemoryMB = 8192; // Default to 8GB if we can't detect
            }
        }

        public void StartMonitoring()
        {
            if (IsMonitoring) return;

            IsMonitoring = true;
            _lastCpuCheck = DateTime.UtcNow;
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
            _updateTimer = new Timer(UpdateMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Debug.WriteLine("Resource monitoring started");
        }

        public void StopMonitoring()
        {
            if (!IsMonitoring) return;

            IsMonitoring = false;
            _updateTimer?.Dispose();
            _updateTimer = null;
            Debug.WriteLine("Resource monitoring stopped");
        }

        private void UpdateMetrics(object? state)
        {
            try
            {
                _currentProcess.Refresh();

                // Calculate memory usage
                var memoryBytes = _currentProcess.WorkingSet64;
                var memoryMB = memoryBytes / 1024 / 1024;

                // Calculate CPU usage (cross-platform method)
                var currentTime = DateTime.UtcNow;
                var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

                var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                var totalMsPassed = (currentTime - _lastCpuCheck).TotalMilliseconds;
                var cpuUsageTotal = 0.0;

                if (totalMsPassed > 0)
                {
                    cpuUsageTotal = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100.0;
                }

                _lastCpuCheck = currentTime;
                _lastTotalProcessorTime = currentTotalProcessorTime;

                // Calculate memory percentage
                double memoryPct = TotalMemoryMB > 0 ? (memoryMB * 100.0 / TotalMemoryMB) : 0;

                // Calculate uptime
                var uptime = DateTime.Now - _currentProcess.StartTime;

                // Update properties on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    MemoryUsageMB = memoryMB;
                    CpuUsage = Math.Round(Math.Min(100, Math.Max(0, cpuUsageTotal)), 1);
                    MemoryPercentage = Math.Round(memoryPct, 1);
                    Uptime = uptime;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating resource metrics: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            StopMonitoring();
            _currentProcess?.Dispose();
        }
    }
}