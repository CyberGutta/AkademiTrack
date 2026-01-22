using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Provides retry logic with exponential backoff for transient failures
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Action to execute</param>
        /// <param name="maxRetries">Maximum number of retries (default: 3)</param>
        /// <param name="initialDelayMs">Initial delay in milliseconds (default: 1000)</param>
        /// <param name="maxDelayMs">Maximum delay in milliseconds (default: 30000)</param>
        /// <param name="onRetry">Optional callback on retry</param>
        /// <returns>Result of the action</returns>
        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>> action,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000,
            Action<int, Exception>? onRetry = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));

            Exception? lastException = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (IsTransientException(ex) && attempt < maxRetries)
                {
                    lastException = ex;
                    
                    // Calculate exponential backoff delay
                    var delay = Math.Min(
                        initialDelayMs * (int)Math.Pow(2, attempt),
                        maxDelayMs
                    );

                    Debug.WriteLine($"[RetryPolicy] Attempt {attempt + 1}/{maxRetries + 1} failed: {ex.Message}. Retrying in {delay}ms");
                    
                    onRetry?.Invoke(attempt + 1, ex);
                    
                    await Task.Delay(delay);
                }
            }

            // All retries exhausted
            throw new Exception($"Operation failed after {maxRetries + 1} attempts", lastException);
        }

        /// <summary>
        /// </summary>
        public static async Task ExecuteAsync(
            Func<Task> action,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000,
            Action<int, Exception>? onRetry = null)
        {
            await ExecuteAsync(async () =>
            {
                await action();
                return true;
            }, maxRetries, initialDelayMs, maxDelayMs, onRetry);
        }

        /// <summary>
        /// Determine if an exception is transient and worth retrying
        /// </summary>
        private static bool IsTransientException(Exception ex)
        {
            return ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is TimeoutException
                || (ex.InnerException != null && IsTransientException(ex.InnerException));
        }
    }
}
