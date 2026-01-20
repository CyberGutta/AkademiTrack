using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services.DependencyInjection
{
    /// <summary>
    /// Modern dependency injection container using Microsoft.Extensions.DependencyInjection
    /// Replaces the old ServiceLocator pattern with proper DI
    /// </summary>
    public static class ServiceContainer
    {
        private static IServiceProvider? _serviceProvider;
        private static IHost? _host;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the current service provider
        /// </summary>
        public static IServiceProvider ServiceProvider
        {
            get
            {
                if (_serviceProvider == null)
                {
                    throw new InvalidOperationException("ServiceContainer has not been initialized. Call Initialize() first.");
                }
                return _serviceProvider;
            }
        }

        /// <summary>
        /// Initialize the dependency injection container
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_serviceProvider != null)
                {
                    Debug.WriteLine("[ServiceContainer] Already initialized - skipping");
                    return;
                }

                Debug.WriteLine("[ServiceContainer] Initializing dependency injection container...");

                try
                {
                    var builder = Host.CreateDefaultBuilder()
                        .ConfigureServices((context, services) =>
                        {
                            ConfigureServices(services);
                        })
                        .ConfigureLogging(logging =>
                        {
                            logging.ClearProviders();
                            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                        });

                    _host = builder.Build();
                    _serviceProvider = _host.Services;

                    Debug.WriteLine("[ServiceContainer] ✓ Dependency injection container initialized successfully");

                    // Start background services
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await InitializeBackgroundServicesAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ServiceContainer] Background service initialization failed: {ex.Message}");
                        }
                    }).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            Debug.WriteLine($"[ServiceContainer] Background service task failed: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ServiceContainer] CRITICAL: Failed to initialize DI container: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Configure all services for dependency injection
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            Debug.WriteLine("[ServiceContainer] Configuring services...");

            // Core interfaces and implementations
            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ICacheService>(provider => 
            {
                return new Caching.CacheService(
                    defaultTtl: TimeSpan.FromMinutes(15),
                    maxEntries: 1000,
                    cleanupInterval: TimeSpan.FromMinutes(5)
                );
            });
            services.AddSingleton<AnalyticsService>();

            // Automation service with dependencies
            services.AddSingleton<IAutomationService>(provider =>
            {
                var loggingService = provider.GetRequiredService<ILoggingService>();
                var notificationService = provider.GetRequiredService<INotificationService>();
                return new AutomationService(loggingService, notificationService);
            });

            // Additional services
            services.AddSingleton<AttendanceDataService>(provider =>
            {
                var cacheService = provider.GetService<ICacheService>();
                return new AttendanceDataService(cacheService);
            });
            services.AddSingleton<AuthenticationService>();
            services.AddSingleton<UpdateCheckerService>();
            services.AddSingleton<SystemHealthCheck>();
            services.AddSingleton<EnhancedNotificationManager>();
            services.AddSingleton<OfflineService>();
            services.AddSingleton<TelemetryService>();

            // Utility services
            services.AddSingleton<LogRetentionManager>();
            services.AddTransient<Services.Utilities.CircuitBreaker>();
            services.AddTransient<Services.Utilities.RateLimiter>();

            // Platform-specific services
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                services.AddSingleton<MacOSAuthService>();
                // MacOSDockHandler is static - no DI registration needed
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                services.AddSingleton<WindowsHelloAuthService>();
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                services.AddSingleton<LinuxPinAuthService>();
            }

            Debug.WriteLine("[ServiceContainer] ✓ All services configured");
        }

        /// <summary>
        /// </summary>
        private static async Task InitializeBackgroundServicesAsync()
        {
            Debug.WriteLine("[ServiceContainer] Initializing background services...");

            try
            {
                // Initialize settings service
                var settingsService = GetService<ISettingsService>();
                await settingsService.LoadSettingsAsync();
                Debug.WriteLine("[ServiceContainer] ✓ Settings service initialized");

                // Initialize analytics service
                var analyticsService = GetService<AnalyticsService>();
                await analyticsService.StartSessionAsync();
                Debug.WriteLine("[ServiceContainer] ✓ Analytics service initialized");

                // Start log retention manager
                var loggingService = GetService<ILoggingService>();
                var retentionManager = new LogRetentionManager(loggingService);
                retentionManager.Start();
                Debug.WriteLine("[ServiceContainer] ✓ Log retention manager started");

                Debug.WriteLine("[ServiceContainer] ✓ All background services initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Background service initialization error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get a service from the DI container
        /// </summary>
        public static T GetService<T>() where T : notnull
        {
            try
            {
                return ServiceProvider.GetRequiredService<T>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Failed to resolve service {typeof(T).Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get a service from the DI container (nullable)
        /// </summary>
        public static T? GetOptionalService<T>() where T : class
        {
            try
            {
                return ServiceProvider.GetService<T>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Failed to resolve optional service {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if a service is registered
        /// </summary>
        public static bool IsServiceRegistered<T>()
        {
            try
            {
                var service = ServiceProvider.GetService<T>();
                return service != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// </summary>
        public static IServiceScope CreateScope()
        {
            return ServiceProvider.CreateScope();
        }

        /// <summary>
        /// Shutdown the service container and dispose resources
        /// </summary>
        public static async Task ShutdownAsync()
        {
            Debug.WriteLine("[ServiceContainer] Shutting down...");

            try
            {
                if (_host != null)
                {
                    await _host.StopAsync();
                    _host.Dispose();
                    _host = null;
                }

                _serviceProvider = null;
                Debug.WriteLine("[ServiceContainer] ✓ Shutdown completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Shutdown error: {ex.Message}");
            }
        }

        /// <summary>
        /// Legacy compatibility method for ServiceLocator migration
        /// </summary>
        [Obsolete("Use ServiceContainer.GetService<T>() instead. This method is for migration compatibility only.")]
        public static T GetLegacyService<T>() where T : notnull
        {
            return GetService<T>();
        }

        /// <summary>
        /// Legacy ServiceLocator compatibility - Instance property
        /// </summary>
        [Obsolete("Use ServiceContainer directly instead of Instance property")]
        public static ServiceLocatorCompat Instance => new ServiceLocatorCompat();
    }

    /// <summary>
    /// Compatibility wrapper for legacy ServiceLocator usage
    /// </summary>
    [Obsolete("Use ServiceContainer directly instead")]
    public class ServiceLocatorCompat
    {
        public T GetService<T>() where T : notnull
        {
            return ServiceContainer.GetService<T>();
        }
    }
}