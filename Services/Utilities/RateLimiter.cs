using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Rate limiter to prevent too many requests in a short time
    /// </summary>
    public class RateLimiter : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _minIntervalMs;
        private DateTime _lastRequestTime;
        private readonly object _lock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Create a new rate limiter
        /// </summary>
        /// <param name="minIntervalMs">Minimum interval between requests in milliseconds</param>
        public RateLimiter(int minIntervalMs = 1000)
        {
            if (minIntervalMs < 0)
                throw new ArgumentOutOfRangeException(nameof(minIntervalMs));

            _minIntervalMs = minIntervalMs;
            _semaphore = new SemaphoreSlim(1, 1);
            _lastRequestTime = DateTime.MinValue;
        }

        /// <summary>
        /// Wait if necessary to respect the rate limit, then execute the action
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RateLimiter));

            await _semaphore.WaitAsync();
            try
            {
                await WaitIfNeededAsync();
                return await action();
            }
            finally
            {
                lock (_lock)
                {
                    _lastRequestTime = DateTime.Now;
                }
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Wait if necessary to respect the rate limit, then execute the action (no return value)
        /// </summary>
        public async Task ExecuteAsync(Func<Task> action)
        {
            await ExecuteAsync(async () =>
            {
                await action();
                return true;
            });
        }

        private async Task WaitIfNeededAsync()
        {
            DateTime lastRequest;
            lock (_lock)
            {
                lastRequest = _lastRequestTime;
            }

            var timeSinceLastRequest = DateTime.Now - lastRequest;
            var remainingWait = _minIntervalMs - (int)timeSinceLastRequest.TotalMilliseconds;

            if (remainingWait > 0)
            {
                Debug.WriteLine($"[RateLimiter] Waiting {remainingWait}ms to respect rate limit");
                await Task.Delay(remainingWait);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _semaphore?.Dispose();
            _disposed = true;
        }
    }
}
