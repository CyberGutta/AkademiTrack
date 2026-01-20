using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services.Caching
{
    /// <summary>
    /// Advanced caching service with TTL, memory management, and cache invalidation
    /// </summary>
    public class CacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly Timer _cleanupTimer;
        private readonly object _statsLock = new object();
        private bool _disposed = false;

        // Cache statistics
        private long _hits = 0;
        private long _misses = 0;
        private long _evictions = 0;

        // Configuration
        private readonly TimeSpan _defaultTtl;
        private readonly int _maxEntries;
        private readonly TimeSpan _cleanupInterval;

        public CacheService(
            TimeSpan? defaultTtl = null,
            int maxEntries = 1000,
            TimeSpan? cleanupInterval = null)
        {
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(15);
            _maxEntries = maxEntries;
            _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(5);

            // Start cleanup timer
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, _cleanupInterval, _cleanupInterval);

            Debug.WriteLine($"[CacheService] Initialized with TTL={_defaultTtl}, MaxEntries={_maxEntries}");
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetStats()
        {
            lock (_statsLock)
            {
                var total = _hits + _misses;
                var hitRate = total > 0 ? (double)_hits / total * 100 : 0;

                return new CacheStats
                {
                    Hits = _hits,
                    Misses = _misses,
                    Evictions = _evictions,
                    HitRate = hitRate,
                    EntryCount = _cache.Count
                };
            }
        }

        /// <summary>
        /// Get value from cache
        /// </summary>
        public T? Get<T>(string key) where T : class
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    RecordMiss();
                    return null;
                }

                entry.UpdateLastAccessed();
                RecordHit();
                return entry.Value as T;
            }

            RecordMiss();
            return null;
        }

        /// <summary>
        /// Set value in cache with default TTL
        /// </summary>
        public void Set<T>(string key, T value) where T : class
        {
            Set(key, value, _defaultTtl);
        }

        /// <summary>
        /// Set value in cache with custom TTL
        /// </summary>
        public void Set<T>(string key, T value, TimeSpan ttl) where T : class
        {
            if (string.IsNullOrEmpty(key) || value == null)
                return;

            // Check if we need to evict entries
            if (_cache.Count >= _maxEntries)
            {
                EvictLeastRecentlyUsed();
            }

            var entry = new CacheEntry(value, ttl);
            _cache.AddOrUpdate(key, entry, (k, existing) => entry);

            Debug.WriteLine($"[CacheService] Cached '{key}' with TTL {ttl}");
        }

        /// <summary>
        /// Get or set value using a factory function
        /// </summary>
        public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null) where T : class
        {
            var cached = Get<T>(key);
            if (cached != null)
            {
                return cached;
            }

            try
            {
                var value = await factory();
                if (value != null)
                {
                    Set(key, value, ttl ?? _defaultTtl);
                }
                return value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CacheService] Factory function failed for key '{key}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Remove specific key from cache
        /// </summary>
        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            var removed = _cache.TryRemove(key, out _);
            if (removed)
            {
                Debug.WriteLine($"[CacheService] Removed '{key}' from cache");
            }
            return removed;
        }

        /// <summary>
        /// Remove all keys matching a pattern
        /// </summary>
        public int RemoveByPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return 0;

            var removed = 0;
            var keysToRemove = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                Debug.WriteLine($"[CacheService] Removed {removed} entries matching pattern '{pattern}'");
            }

            return removed;
        }

        /// <summary>
        /// Clear all cache entries
        /// </summary>
        public void Clear()
        {
            var count = _cache.Count;
            _cache.Clear();
            Debug.WriteLine($"[CacheService] Cleared {count} cache entries");
        }

        /// <summary>
        /// Check if key exists and is not expired
        /// </summary>
        public bool ContainsKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Refresh TTL for existing entry
        /// </summary>
        public bool RefreshTtl(string key, TimeSpan? newTtl = null)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefreshTtl(newTtl ?? _defaultTtl);
                Debug.WriteLine($"[CacheService] Refreshed TTL for '{key}'");
                return true;
            }
            return false;
        }

        private void CleanupExpiredEntries(object? state)
        {
            try
            {
                var expiredKeys = new List<string>();

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.IsExpired)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                var removed = 0;
                foreach (var key in expiredKeys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        removed++;
                        RecordEviction();
                    }
                }

                if (removed > 0)
                {
                    Debug.WriteLine($"[CacheService] Cleanup removed {removed} expired entries");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CacheService] Cleanup error: {ex.Message}");
            }
        }

        private void EvictLeastRecentlyUsed()
        {
            try
            {
                var oldestEntry = DateTime.MaxValue;
                string? oldestKey = null;

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.LastAccessed < oldestEntry)
                    {
                        oldestEntry = kvp.Value.LastAccessed;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey != null && _cache.TryRemove(oldestKey, out _))
                {
                    RecordEviction();
                    Debug.WriteLine($"[CacheService] Evicted LRU entry: '{oldestKey}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CacheService] LRU eviction error: {ex.Message}");
            }
        }

        private void RecordHit()
        {
            lock (_statsLock)
            {
                _hits++;
            }
        }

        private void RecordMiss()
        {
            lock (_statsLock)
            {
                _misses++;
            }
        }

        private void RecordEviction()
        {
            lock (_statsLock)
            {
                _evictions++;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _cleanupTimer?.Dispose();
                _cache.Clear();
                Debug.WriteLine("[CacheService] Disposed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CacheService] Dispose error: {ex.Message}");
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Cache entry with TTL and access tracking
    /// </summary>
    internal class CacheEntry
    {
        public object Value { get; }
        public DateTime CreatedAt { get; }
        public DateTime ExpiresAt { get; private set; }
        public DateTime LastAccessed { get; private set; }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public CacheEntry(object value, TimeSpan ttl)
        {
            Value = value;
            CreatedAt = DateTime.UtcNow;
            LastAccessed = CreatedAt;
            ExpiresAt = CreatedAt.Add(ttl);
        }

        public void UpdateLastAccessed()
        {
            LastAccessed = DateTime.UtcNow;
        }

        public void RefreshTtl(TimeSpan newTtl)
        {
            ExpiresAt = DateTime.UtcNow.Add(newTtl);
            UpdateLastAccessed();
        }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long Evictions { get; set; }
        public double HitRate { get; set; }
        public int EntryCount { get; set; }

        public override string ToString()
        {
            return $"Hits: {Hits}, Misses: {Misses}, Hit Rate: {HitRate:F1}%, Entries: {EntryCount}, Evictions: {Evictions}";
        }
    }
}