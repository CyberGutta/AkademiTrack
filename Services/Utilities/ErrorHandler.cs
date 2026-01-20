using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AkademiTrack.Common;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Centralized error handling with logging and analytics
    /// </summary>
    public class ErrorHandler
    {
        private readonly ILoggingService? _loggingService;
        private readonly AnalyticsService? _analyticsService;

        public ErrorHandler(ILoggingService? loggingService = null, AnalyticsService? analyticsService = null)
        {
            _loggingService = loggingService;
            _analyticsService = analyticsService;
        }

        /// <summary>
        /// </summary>
        public async Task<Result<T>> ExecuteAsync<T>(Func<Task<T>> action, string context)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrEmpty(context)) throw new ArgumentNullException(nameof(context));

            try
            {
                var result = await action();
                return Result<T>.Successful(result);
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync<T>(ex, context);
            }
        }

        /// <summary>
        /// </summary>
        public async Task<Result> ExecuteAsync(Func<Task> action, string context)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrEmpty(context)) throw new ArgumentNullException(nameof(context));

            try
            {
                await action();
                return Result.Successful();
            }
            catch (Exception ex)
            {
                return await HandleExceptionAsync(ex, context);
            }
        }

        /// <summary>
        /// Execute a synchronous action with comprehensive error handling
        /// </summary>
        public Result<T> Execute<T>(Func<T> action, string context)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrEmpty(context)) throw new ArgumentNullException(nameof(context));

            try
            {
                var result = action();
                return Result<T>.Successful(result);
            }
            catch (Exception ex)
            {
                return HandleException<T>(ex, context);
            }
        }

        private async Task<Result<T>> HandleExceptionAsync<T>(Exception ex, string context)
        {
            var errorMessage = $"{context}: {ex.Message}";
            
            // Log to console
            Debug.WriteLine($"[ERROR] {errorMessage}");
            Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            
            // Log to logging service
            _loggingService?.LogError(errorMessage);
            
            // Log to analytics
            if (_analyticsService != null)
            {
                try
                {
                    await _analyticsService.LogErrorAsync(
                        SanitizeContextForAnalytics(context),
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[ERROR] Failed to log to analytics: {analyticsEx.Message}");
                }
            }
            
            return Result<T>.Failed(errorMessage, ex);
        }

        private async Task<Result> HandleExceptionAsync(Exception ex, string context)
        {
            var errorMessage = $"{context}: {ex.Message}";
            
            Debug.WriteLine($"[ERROR] {errorMessage}");
            Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            
            _loggingService?.LogError(errorMessage);
            
            if (_analyticsService != null)
            {
                try
                {
                    await _analyticsService.LogErrorAsync(
                        SanitizeContextForAnalytics(context),
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[ERROR] Failed to log to analytics: {analyticsEx.Message}");
                }
            }
            
            return Result.Failed(errorMessage, ex);
        }

        private Result<T> HandleException<T>(Exception ex, string context)
        {
            var errorMessage = $"{context}: {ex.Message}";
            
            Debug.WriteLine($"[ERROR] {errorMessage}");
            Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            
            _loggingService?.LogError(errorMessage);
            
            if (_analyticsService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _analyticsService.LogErrorAsync(
                            SanitizeContextForAnalytics(context),
                            ex.Message,
                            ex
                        );
                    }
                    catch (Exception analyticsEx)
                    {
                        Debug.WriteLine($"[ErrorHandler] Analytics logging failed: {analyticsEx.Message}");
                    }
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        Debug.WriteLine($"[ErrorHandler] Analytics task failed: {t.Exception.GetBaseException().Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            
            return Result<T>.Failed(errorMessage, ex);
        }

        private static string SanitizeContextForAnalytics(string context)
        {
            // Remove any potentially sensitive information
            return context
                .Replace("password", "***")
                .Replace("token", "***")
                .Replace("secret", "***")
                .Replace("key", "***");
        }
    }
}
