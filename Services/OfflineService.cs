using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Common;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Service for handling offline functionality and data persistence
    /// </summary>
    public class OfflineService : IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly string _offlineDataPath;
        private readonly string _queuedActionsPath;
        private readonly List<QueuedAction> _queuedActions;
        private bool _isOnline = true;
        private bool _disposed = false;

        public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

        public bool IsOnline => _isOnline;

        public OfflineService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            
            _offlineDataPath = Path.Combine(appDataDir, "offline_data.json");
            _queuedActionsPath = Path.Combine(appDataDir, "queued_actions.json");
            _queuedActions = new List<QueuedAction>();
            
            LoadQueuedActions();
            
            Debug.WriteLine("[OfflineService] Initialized offline service");
        }

        /// <summary>
        /// Set the online/offline status
        /// </summary>
        public void SetConnectivityStatus(bool isOnline)
        {
            if (_isOnline != isOnline)
            {
                var previousStatus = _isOnline;
                _isOnline = isOnline;
                
                _loggingService.LogInfo($"🌐 Connectivity changed: {(isOnline ? "ONLINE" : "OFFLINE")}");
                
                ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs
                {
                    IsOnline = isOnline,
                    PreviousStatus = previousStatus,
                    Timestamp = DateTime.Now
                });

                if (isOnline)
                {
                    _ = Task.Run(ProcessQueuedActionsAsync);
                }
            }
        }

        /// <summary>
        /// Save data for offline access
        /// </summary>
        public async Task SaveOfflineDataAsync<T>(string key, T data) where T : class
        {
            try
            {
                var offlineData = await LoadOfflineDataAsync();
                offlineData[key] = new OfflineDataEntry
                {
                    Data = JsonSerializer.Serialize(data),
                    Timestamp = DateTime.Now,
                    Type = typeof(T).Name
                };

                var json = JsonSerializer.Serialize(offlineData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_offlineDataPath, json);
                
                Debug.WriteLine($"[OfflineService] Saved offline data for key: {key}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to save offline data for {key}: {ex.Message}");
            }
        }

        /// <summary>
        /// Load data from offline storage
        /// </summary>
        public async Task<T?> LoadOfflineDataAsync<T>(string key) where T : class
        {
            try
            {
                var offlineData = await LoadOfflineDataAsync();
                
                if (offlineData.TryGetValue(key, out var entry))
                {
                    // Check if data is not too old (max 24 hours for offline data)
                    if (DateTime.Now - entry.Timestamp < TimeSpan.FromHours(24))
                    {
                        var data = JsonSerializer.Deserialize<T>(entry.Data);
                        Debug.WriteLine($"[OfflineService] Loaded offline data for key: {key}");
                        return data;
                    }
                    else
                    {
                        Debug.WriteLine($"[OfflineService] Offline data for {key} is too old, ignoring");
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to load offline data for {key}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queue an action to be executed when online
        /// </summary>
        public async Task QueueActionAsync(string actionType, string actionData, int priority = 0)
        {
            try
            {
                var queuedAction = new QueuedAction
                {
                    Id = Guid.NewGuid().ToString(),
                    ActionType = actionType,
                    ActionData = actionData,
                    Priority = priority,
                    QueuedAt = DateTime.Now,
                    RetryCount = 0
                };

                _queuedActions.Add(queuedAction);
                await SaveQueuedActionsAsync();
                
                _loggingService.LogInfo($"📋 Queued action: {actionType} (Priority: {priority})");
                
                // If we're online, try to process immediately
                if (_isOnline)
                {
                    _ = Task.Run(ProcessQueuedActionsAsync);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to queue action {actionType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get cached data with fallback to offline storage
        /// </summary>
        public async Task<T?> GetDataWithOfflineFallbackAsync<T>(
            string key, 
            Func<Task<T?>> onlineDataFetcher,
            TimeSpan? maxAge = null) where T : class
        {
            if (_isOnline)
            {
                try
                {
                    // Try to get fresh data
                    var onlineData = await onlineDataFetcher();
                    if (onlineData != null)
                    {
                        // Save for offline use
                        await SaveOfflineDataAsync(key, onlineData);
                        return onlineData;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Online data fetch failed for {key}: {ex.Message}");
                    // Fall through to offline data
                }
            }

            // Use offline data as fallback
            var offlineData = await LoadOfflineDataAsync<T>(key);
            if (offlineData != null)
            {
                _loggingService.LogInfo($"📱 Using offline data for: {key}");
                return offlineData;
            }

            return null;
        }

        /// <summary>
        /// Check if we have offline data for a key
        /// </summary>
        public async Task<bool> HasOfflineDataAsync(string key)
        {
            try
            {
                var offlineData = await LoadOfflineDataAsync();
                return offlineData.ContainsKey(key);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clear old offline data
        /// </summary>
        public async Task CleanupOfflineDataAsync(TimeSpan? maxAge = null)
        {
            try
            {
                var cutoffTime = DateTime.Now - (maxAge ?? TimeSpan.FromDays(7));
                var offlineData = await LoadOfflineDataAsync();
                var keysToRemove = new List<string>();

                foreach (var kvp in offlineData)
                {
                    if (kvp.Value.Timestamp < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    offlineData.Remove(key);
                }

                if (keysToRemove.Count > 0)
                {
                    var json = JsonSerializer.Serialize(offlineData, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(_offlineDataPath, json);
                    
                    _loggingService.LogInfo($"🧹 Cleaned up {keysToRemove.Count} old offline data entries");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to cleanup offline data: {ex.Message}");
            }
        }

        private async Task<Dictionary<string, OfflineDataEntry>> LoadOfflineDataAsync()
        {
            try
            {
                if (File.Exists(_offlineDataPath))
                {
                    var json = await File.ReadAllTextAsync(_offlineDataPath);
                    return JsonSerializer.Deserialize<Dictionary<string, OfflineDataEntry>>(json) 
                           ?? new Dictionary<string, OfflineDataEntry>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfflineService] Failed to load offline data: {ex.Message}");
            }

            return new Dictionary<string, OfflineDataEntry>();
        }

        private void LoadQueuedActions()
        {
            try
            {
                if (File.Exists(_queuedActionsPath))
                {
                    var json = File.ReadAllText(_queuedActionsPath);
                    var actions = JsonSerializer.Deserialize<List<QueuedAction>>(json);
                    if (actions != null)
                    {
                        _queuedActions.AddRange(actions);
                        Debug.WriteLine($"[OfflineService] Loaded {actions.Count} queued actions");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfflineService] Failed to load queued actions: {ex.Message}");
            }
        }

        private async Task SaveQueuedActionsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_queuedActions, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_queuedActionsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfflineService] Failed to save queued actions: {ex.Message}");
            }
        }

        private async Task ProcessQueuedActionsAsync()
        {
            if (!_isOnline || _queuedActions.Count == 0)
                return;

            Debug.WriteLine($"[OfflineService] Processing {_queuedActions.Count} queued actions");

            // Sort by priority (higher priority first) then by queued time
            _queuedActions.Sort((a, b) =>
            {
                var priorityComparison = b.Priority.CompareTo(a.Priority);
                return priorityComparison != 0 ? priorityComparison : a.QueuedAt.CompareTo(b.QueuedAt);
            });

            var processedActions = new List<QueuedAction>();

            foreach (var action in _queuedActions.ToArray())
            {
                try
                {
                    var success = await ProcessQueuedActionAsync(action);
                    if (success)
                    {
                        processedActions.Add(action);
                        _loggingService.LogInfo($"Processed queued action: {action.ActionType}");
                    }
                    else
                    {
                        action.RetryCount++;
                        if (action.RetryCount >= Constants.Network.MAX_RETRY_ATTEMPTS)
                        {
                            processedActions.Add(action); // Remove failed actions after max retries
                            _loggingService.LogError($"Failed to process action after {action.RetryCount} retries: {action.ActionType}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Error processing queued action {action.ActionType}: {ex.Message}");
                    action.RetryCount++;
                    if (action.RetryCount >= Constants.Network.MAX_RETRY_ATTEMPTS)
                    {
                        processedActions.Add(action);
                    }
                }
            }

            // Remove processed actions
            foreach (var action in processedActions)
            {
                _queuedActions.Remove(action);
            }

            if (processedActions.Count > 0)
            {
                await SaveQueuedActionsAsync();
                Debug.WriteLine($"[OfflineService] Processed {processedActions.Count} actions, {_queuedActions.Count} remaining");
            }
        }

        private async Task<bool> ProcessQueuedActionAsync(QueuedAction action)
        {
            // This is a placeholder - in a real implementation, you would
            // dispatch to appropriate handlers based on action.ActionType
            
            switch (action.ActionType)
            {
                case "analytics_event":
                    // Process analytics event
                    return await ProcessAnalyticsEventAsync(action.ActionData);
                    
                case "attendance_registration":
                    // Process attendance registration
                    return await ProcessAttendanceRegistrationAsync(action.ActionData);
                    
                default:
                    Debug.WriteLine($"[OfflineService] Unknown action type: {action.ActionType}");
                    return false;
            }
        }

        private async Task<bool> ProcessAnalyticsEventAsync(string actionData)
        {
            // Placeholder for analytics event processing
            await Task.Delay(100); // Simulate processing
            return true;
        }

        private async Task<bool> ProcessAttendanceRegistrationAsync(string actionData)
        {
            // Placeholder for attendance registration processing
            await Task.Delay(100); // Simulate processing
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Save any pending queued actions
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveQueuedActionsAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[OfflineService] Error saving queued actions during dispose: {ex.Message}");
                    }
                });

                Debug.WriteLine("[OfflineService] Disposed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfflineService] Error during dispose: {ex.Message}");
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Represents offline data entry
    /// </summary>
    public class OfflineDataEntry
    {
        public string Data { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a queued action for offline processing
    /// </summary>
    public class QueuedAction
    {
        public string Id { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionData { get; set; } = string.Empty;
        public int Priority { get; set; }
        public DateTime QueuedAt { get; set; }
        public int RetryCount { get; set; }
    }

    /// <summary>
    /// Event args for connectivity changes
    /// </summary>
    public class ConnectivityChangedEventArgs : EventArgs
    {
        public bool IsOnline { get; set; }
        public bool PreviousStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }
}