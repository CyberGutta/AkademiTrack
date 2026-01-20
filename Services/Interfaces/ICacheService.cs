using System;
using System.Threading.Tasks;
using AkademiTrack.Services.Caching;

namespace AkademiTrack.Services.Interfaces
{
    /// <summary>
    /// Interface for caching service
    /// </summary>
    public interface ICacheService : IDisposable
    {
        /// <summary>
        /// Get value from cache
        /// </summary>
        T? Get<T>(string key) where T : class;

        /// <summary>
        /// Set value in cache with default TTL
        /// </summary>
        void Set<T>(string key, T value) where T : class;

        /// <summary>
        /// Set value in cache with custom TTL
        /// </summary>
        void Set<T>(string key, T value, TimeSpan ttl) where T : class;

        /// <summary>
        /// Get or set value using a factory function
        /// </summary>
        Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null) where T : class;

        /// <summary>
        /// Remove specific key from cache
        /// </summary>
        bool Remove(string key);

        /// <summary>
        /// Remove all keys matching a pattern
        /// </summary>
        int RemoveByPattern(string pattern);

        /// <summary>
        /// Clear all cache entries
        /// </summary>
        void Clear();

        /// <summary>
        /// Check if key exists and is not expired
        /// </summary>
        bool ContainsKey(string key);

        /// <summary>
        /// Refresh TTL for existing entry
        /// </summary>
        bool RefreshTtl(string key, TimeSpan? newTtl = null);

        /// <summary>
        /// Get cache statistics
        /// </summary>
        CacheStats GetStats();
    }
}