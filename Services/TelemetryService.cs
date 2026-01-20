using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Common;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Enhanced telemetry service for performance monitoring and diagnostics
    /// </summary>
    public class TelemetryService : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly AnalyticsService? _analyticsService;
        private readonly Dictionary<string, PerformanceCounter> _performanceCounters;
        private readonly Dictionary<string, DateTime> _operationStartTimes;
        private readonly Timer _metricsTimer;
        private bool _disposed = false;

        // Performance metrics
        private long _totalOperations = 0;
        private long _successfulOperations = 0;
        private long _failedOperations = 0;
        private readonly Dictionary<string, long> _operationCounts = new();
        private readonly Dictionary<string, TimeSpan> _operationTotalTimes = new();

        public TelemetryService(ILoggingService loggingService, AnalyticsService? analyticsService = null)
        {
            _loggingService = loggingService;
            _analyticsService = analyticsService;
            _performanceCounters = new Dictionary<string, PerformanceCounter>();
            _operationStartTimes = new Dictionary<string, DateTime>();

            // Start metrics collection timer (every 5 minutes)
            _metricsTimer = new Timer(CollectMetrics, null, 
                TimeSpan.FromMinutes(Constants.Time.CACHE_SHORT_TTL_MINUTES), 
                TimeSpan.FromMinutes(Constants.Time.CACHE_SHORT_TTL_MINUTES));

            Debug.WriteLine("[TelemetryService] Initialized telemetry service");
        }

        /// <summary>
        /// Start tracking an operation
        /// </summary>
        public void StartOperation(string operationName)
        {
            try
            {
                var key = $"{operationName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.Now.Ticks}";
                _operationStartTimes[key] = DateTime.Now;
                
                Debug.WriteLine($"[Telemetry] Started operation: {operationName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error starting operation {operationName}: {ex.Message}");
            }
        }

        /// <summary>
        /// End tracking an operation
        /// </summary>
        public void EndOperation(string operationName, bool success = true, string? errorMessage = null)
        {
            try
            {
                var key = _operationStartTimes.Keys.FirstOrDefault(k => k.StartsWith(operationName));
                if (key != null && _operationStartTimes.TryGetValue(key, out var startTime))
                {
                    var duration = DateTime.Now - startTime;
                    _operationStartTimes.Remove(key);

                    // Update counters
                    Interlocked.Increment(ref _totalOperations);
                    if (success)
                    {
                        Interlocked.Increment(ref _successfulOperations);
                    }
                    else
                    {
                        Interlocked.Increment(ref _failedOperations);
                    }

                    // Update operation-specific metrics
                    lock (_operationCounts)
                    {
                        _operationCounts.TryGetValue(operationName, out var count);
                        _operationCounts[operationName] = count + 1;

                        _operationTotalTimes.TryGetValue(operationName, out var totalTime);
                        _operationTotalTimes[operationName] = totalTime + duration;
                    }

                    var status = success ? "SUCCESS" : "FAILED";
                    Debug.WriteLine($"[Telemetry] Ended operation: {operationName} ({status}) - Duration: {duration.TotalMilliseconds:F2}ms");

                    // Log slow operations
                    if (duration.TotalSeconds > Constants.Time.SHORT_TIMEOUT_SECONDS)
                    {
                        _loggingService.LogWarning($"⚠️ Slow operation detected: {operationName} took {duration.TotalSeconds:F2}s");
                    }

                    // Log failures
                    if (!success && !string.IsNullOrEmpty(errorMessage))
                    {
                        _loggingService.LogError($"❌ Operation failed: {operationName} - {errorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error ending operation {operationName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Track a custom metric
        /// </summary>
        public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null)
        {
            try
            {
                Debug.WriteLine($"[Telemetry] Metric: {metricName} = {value}");
                
                // Log significant metrics
                if (metricName.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                    metricName.Contains("fail", StringComparison.OrdinalIgnoreCase))
                {
                    _loggingService.LogWarning($"📊 Metric Alert: {metricName} = {value}");
                }

                // Send to analytics if available
                if (_analyticsService != null && properties != null)
                {
                    var analyticsProperties = new Dictionary<string, object>();
                    foreach (var prop in properties)
                    {
                        analyticsProperties[prop.Key] = prop.Value;
                    }
                    analyticsProperties["metric_value"] = value;
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _analyticsService.TrackEventAsync($"metric_{metricName}", analyticsProperties);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[TelemetryService] Failed to send metric to analytics: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error tracking metric {metricName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Track an exception
        /// </summary>
        public void TrackException(Exception exception, string? context = null, Dictionary<string, string>? properties = null)
        {
            try
            {
                var errorContext = context ?? "Unknown";
                Debug.WriteLine($"[Telemetry] Exception in {errorContext}: {exception.Message}");
                
                _loggingService.LogError($"🚨 Exception tracked: {errorContext} - {exception.Message}");

                // Send to analytics if available
                if (_analyticsService != null)
                {
                    var analyticsProperties = new Dictionary<string, object>
                    {
                        ["exception_type"] = exception.GetType().Name,
                        ["exception_message"] = exception.Message,
                        ["context"] = errorContext,
                        ["stack_trace"] = exception.StackTrace ?? "No stack trace"
                    };

                    if (properties != null)
                    {
                        foreach (var prop in properties)
                        {
                            analyticsProperties[prop.Key] = prop.Value;
                        }
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _analyticsService.TrackEventAsync("exception_tracked", analyticsProperties);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[TelemetryService] Failed to send exception to analytics: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error tracking exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Get performance statistics
        /// </summary>
        public TelemetryStats GetStats()
        {
            try
            {
                lock (_operationCounts)
                {
                    var operationStats = new Dictionary<string, OperationStats>();
                    
                    foreach (var kvp in _operationCounts)
                    {
                        var operationName = kvp.Key;
                        var count = kvp.Value;
                        var totalTime = _operationTotalTimes.GetValueOrDefault(operationName, TimeSpan.Zero);
                        var averageTime = count > 0 ? totalTime.TotalMilliseconds / count : 0;

                        operationStats[operationName] = new OperationStats
                        {
                            Count = count,
                            TotalTime = totalTime,
                            AverageTime = TimeSpan.FromMilliseconds(averageTime)
                        };
                    }

                    return new TelemetryStats
                    {
                        TotalOperations = _totalOperations,
                        SuccessfulOperations = _successfulOperations,
                        FailedOperations = _failedOperations,
                        SuccessRate = _totalOperations > 0 ? (double)_successfulOperations / _totalOperations * 100 : 0,
                        OperationStats = operationStats
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error getting stats: {ex.Message}");
                return new TelemetryStats();
            }
        }

        /// <summary>
        /// Reset all statistics
        /// </summary>
        public void ResetStats()
        {
            try
            {
                Interlocked.Exchange(ref _totalOperations, 0);
                Interlocked.Exchange(ref _successfulOperations, 0);
                Interlocked.Exchange(ref _failedOperations, 0);

                lock (_operationCounts)
                {
                    _operationCounts.Clear();
                    _operationTotalTimes.Clear();
                }

                Debug.WriteLine("[TelemetryService] Statistics reset");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error resetting stats: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute an operation with automatic telemetry tracking
        /// </summary>
        public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation)
        {
            StartOperation(operationName);
            try
            {
                var result = await operation();
                EndOperation(operationName, success: true);
                return result;
            }
            catch (Exception ex)
            {
                EndOperation(operationName, success: false, errorMessage: ex.Message);
                TrackException(ex, operationName);
                throw;
            }
        }

        /// <summary>
        /// </summary>
        public async Task TrackOperationAsync(string operationName, Func<Task> operation)
        {
            StartOperation(operationName);
            try
            {
                await operation();
                EndOperation(operationName, success: true);
            }
            catch (Exception ex)
            {
                EndOperation(operationName, success: false, errorMessage: ex.Message);
                TrackException(ex, operationName);
                throw;
            }
        }

        private void CollectMetrics(object? state)
        {
            try
            {
                var stats = GetStats();
                
                // Log periodic statistics
                _loggingService.LogInfo($"📊 Telemetry Stats - Total: {stats.TotalOperations}, Success Rate: {stats.SuccessRate:F1}%");
                
                // Track key metrics
                TrackMetric("total_operations", stats.TotalOperations);
                TrackMetric("success_rate", stats.SuccessRate);
                TrackMetric("failed_operations", stats.FailedOperations);

                // Track slow operations
                foreach (var kvp in stats.OperationStats)
                {
                    if (kvp.Value.AverageTime.TotalSeconds > Constants.Time.SHORT_TIMEOUT_SECONDS)
                    {
                        TrackMetric($"slow_operation_{kvp.Key}", kvp.Value.AverageTime.TotalSeconds);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error collecting metrics: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _metricsTimer?.Dispose();
                
                // Log final statistics
                var stats = GetStats();
                _loggingService.LogInfo($"📊 Final Telemetry Stats - Total: {stats.TotalOperations}, Success Rate: {stats.SuccessRate:F1}%");
                
                Debug.WriteLine("[TelemetryService] Disposed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error during dispose: {ex.Message}");
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Performance counter for tracking metrics
    /// </summary>
    public class PerformanceCounter
    {
        public string Name { get; set; } = string.Empty;
        public long Count { get; set; }
        public double TotalValue { get; set; }
        public double MinValue { get; set; } = double.MaxValue;
        public double MaxValue { get; set; } = double.MinValue;
        public DateTime LastUpdated { get; set; }

        public double AverageValue => Count > 0 ? TotalValue / Count : 0;
    }

    /// <summary>
    /// Statistics for a specific operation
    /// </summary>
    public class OperationStats
    {
        public long Count { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan AverageTime { get; set; }
    }

    /// <summary>
    /// Overall telemetry statistics
    /// </summary>
    public class TelemetryStats
    {
        public long TotalOperations { get; set; }
        public long SuccessfulOperations { get; set; }
        public long FailedOperations { get; set; }
        public double SuccessRate { get; set; }
        public Dictionary<string, OperationStats> OperationStats { get; set; } = new();
    }
}