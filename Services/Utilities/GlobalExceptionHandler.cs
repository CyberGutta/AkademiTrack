using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Global exception handler for unhandled exceptions and critical errors
    /// </summary>
    public static class GlobalExceptionHandler
    {
        private static ILoggingService? _loggingService;
        private static INotificationService? _notificationService;
        private static AnalyticsService? _analyticsService;
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize the global exception handler with required services
        /// </summary>
        public static void Initialize(ILoggingService loggingService, 
                                    INotificationService notificationService, 
                                    AnalyticsService analyticsService)
        {
            _loggingService = loggingService;
            _notificationService = notificationService;
            _analyticsService = analyticsService;
            _isInitialized = true;

            // Register for unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Debug.WriteLine("[GlobalExceptionHandler] Initialized and registered for unhandled exceptions");
        }

        /// <summary>
        /// Handle unhandled exceptions from the main application domain
        /// </summary>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                var isTerminating = e.IsTerminating;

                Debug.WriteLine($"[GlobalExceptionHandler] Unhandled exception caught (terminating: {isTerminating})");
                Debug.WriteLine($"[GlobalExceptionHandler] Exception: {exception?.Message}");
                Debug.WriteLine($"[GlobalExceptionHandler] Stack trace: {exception?.StackTrace}");

                HandleException(exception, "UnhandledException", isTerminating);
            }
            catch (Exception handlerEx)
            {
                // Last resort - write to debug output
                Debug.WriteLine($"[GlobalExceptionHandler] Error in exception handler: {handlerEx.Message}");
            }
        }

        /// <summary>
        /// Handle unobserved task exceptions
        /// </summary>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                // Mark as observed to prevent app termination
                e.SetObserved();

                // Ignore specific non-critical exceptions from browser automation
                if (e.Exception.Message.Contains("Response body is unavailable for redirect responses") ||
                    e.Exception.InnerException?.Message.Contains("Response body is unavailable for redirect responses") == true)
                {
                    return;
                }

                Debug.WriteLine($"[GlobalExceptionHandler] Unobserved task exception caught");
                Debug.WriteLine($"[GlobalExceptionHandler] Exception: {e.Exception.Message}");

                HandleException(e.Exception, "UnobservedTaskException", false);
            }
            catch (Exception handlerEx)
            {
                Debug.WriteLine($"[GlobalExceptionHandler] Error in task exception handler: {handlerEx.Message}");
            }
        }

        /// <summary>
        /// Handle a specific exception with logging, analytics, and user notification
        /// </summary>
        public static void HandleException(Exception? exception, string context = "Unknown", bool isTerminating = false)
        {
            if (exception == null) return;

            try
            {
                var errorMessage = $"[{context}] {exception.Message}";
                var fullError = $"{errorMessage}\nStack trace: {exception.StackTrace}";

                // Log the error
                if (_isInitialized && _loggingService != null)
                {
                    _loggingService.LogError($"🚨 CRITICAL ERROR: {errorMessage}");
                    _loggingService.LogDebug(fullError);
                }
                else
                {
                    Debug.WriteLine($"[GlobalExceptionHandler] CRITICAL ERROR: {fullError}");
                }

                // Send to analytics (fire and forget)
                if (_isInitialized && _analyticsService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _analyticsService.LogErrorAsync(
                                $"critical_error_{context.ToLowerInvariant()}",
                                errorMessage,
                                exception
                            );
                        }
                        catch (Exception analyticsEx)
                        {
                            Debug.WriteLine($"[GlobalExceptionHandler] Failed to log to analytics: {analyticsEx.Message}");
                        }
                    }).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            Debug.WriteLine($"[GlobalExceptionHandler] Analytics logging task failed: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }

                // Show user notification (if not terminating) - DISABLED
                // Removed annoying "Kritisk feil" popup that appears for non-critical errors
                /*
                if (_isInitialized && _notificationService != null && !isTerminating)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _notificationService.ShowNotificationAsync(
                                "Kritisk feil",
                                "En uventet feil oppstod. Applikasjonen kan være ustabil. Vurder å starte på nytt.",
                                NotificationLevel.Error,
                                isHighPriority: true
                            );
                        }
                        catch (Exception notificationEx)
                        {
                            Debug.WriteLine($"[GlobalExceptionHandler] Failed to show notification: {notificationEx.Message}");
                        }
                    }).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            Debug.WriteLine($"[GlobalExceptionHandler] Notification task failed: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
                */
                // If terminating, try to save critical state
                if (isTerminating)
                {
                    try
                    {
                        SaveCriticalState(exception);
                    }
                    catch (Exception saveEx)
                    {
                        Debug.WriteLine($"[GlobalExceptionHandler] Failed to save critical state: {saveEx.Message}");
                    }
                }
            }
            catch (Exception handlerEx)
            {
                // Absolute last resort
                Debug.WriteLine($"[GlobalExceptionHandler] FATAL: Exception handler failed: {handlerEx.Message}");
                Debug.WriteLine($"[GlobalExceptionHandler] Original exception: {exception.Message}");
            }
        }

        /// <summary>
        /// Save critical application state before termination
        /// </summary>
        private static void SaveCriticalState(Exception exception)
        {
            try
            {
                var crashReport = new
                {
                    Timestamp = DateTime.UtcNow,
                    Exception = exception.ToString(),
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    InnerException = exception.InnerException?.ToString(),
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    OSVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    WorkingSet = Environment.WorkingSet,
                    Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                };

                var crashReportJson = System.Text.Json.JsonSerializer.Serialize(crashReport, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                var appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                System.IO.Directory.CreateDirectory(appDataDir);

                var crashReportPath = System.IO.Path.Combine(appDataDir, 
                    $"crash_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

                System.IO.File.WriteAllText(crashReportPath, crashReportJson);

                Debug.WriteLine($"[GlobalExceptionHandler] Crash report saved to: {crashReportPath}");
            }
            catch (Exception saveEx)
            {
                Debug.WriteLine($"[GlobalExceptionHandler] Failed to save crash report: {saveEx.Message}");
            }
        }

        /// <summary>
        /// Cleanup and unregister event handlers
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

                _loggingService = null;
                _notificationService = null;
                _analyticsService = null;
                _isInitialized = false;

                Debug.WriteLine("[GlobalExceptionHandler] Cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalExceptionHandler] Error during cleanup: {ex.Message}");
            }
        }
    }
}