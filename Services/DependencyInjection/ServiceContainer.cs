using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

                Debug.WriteLine("[ServiceContainer] Initializing dependency injection container");

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

                    Debug.WriteLine("[ServiceContainer] Dependency injection container initialized successfully");

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
            Debug.WriteLine("[ServiceContainer] Configuring services");

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
            services.AddSingleton<SystemHealthCheck>();

            // Utility services
            services.AddSingleton<LogRetentionManager>();

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

            Debug.WriteLine("[ServiceContainer] All services configured");
        }

        /// <summary>
        /// </summary>
        private static async Task InitializeBackgroundServicesAsync()
        {
            Debug.WriteLine("[ServiceContainer] Initializing background services");

            try
            {
                // Initialize settings service
                var settingsService = GetService<ISettingsService>();
                await settingsService.LoadSettingsAsync();
                Debug.WriteLine("[ServiceContainer] Settings service initialized");

                // Initialize analytics service
                var analyticsService = GetService<AnalyticsService>();
                await analyticsService.StartSessionAsync();
                Debug.WriteLine("[ServiceContainer] Analytics service initialized");

                // Start log retention manager
                var loggingService = GetService<ILoggingService>();
                var retentionManager = new LogRetentionManager(loggingService);
                retentionManager.Start();
                Debug.WriteLine("[ServiceContainer] Log retention manager started");

                Debug.WriteLine("[ServiceContainer] All background services initialized successfully");
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
            Debug.WriteLine("[ServiceContainer] Shutting down");

            try
            {
                // Create uninstall detector before shutting down (with timeout)
                var uninstallTask = CreateUninstallDetectorAsync();
                if (await Task.WhenAny(uninstallTask, Task.Delay(1000)) != uninstallTask)
                {
                    Debug.WriteLine("[ServiceContainer] Uninstall detector timed out");
                }

                if (_host != null)
                {
                    // Give host 1 second to stop gracefully, then force
                    var stopTask = _host.StopAsync();
                    if (await Task.WhenAny(stopTask, Task.Delay(1000)) == stopTask)
                    {
                        Debug.WriteLine("[ServiceContainer] Host stopped gracefully");
                    }
                    else
                    {
                        Debug.WriteLine("[ServiceContainer] Host stop timed out - forcing");
                    }
                    
                    _host.Dispose();
                    _host = null;
                }

                _serviceProvider = null;
                Debug.WriteLine("[ServiceContainer] Shutdown completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Shutdown error: {ex.Message}");
            }
        }

        private static async Task CreateUninstallDetectorAsync()
        {
            try
            {
                Debug.WriteLine("[ServiceContainer] Creating uninstall detector");

                // Get analytics service to get user ID and config
                var analyticsService = GetOptionalService<AnalyticsService>();
                if (analyticsService == null) return;

                var userId = analyticsService.GetPersistentUserId();
                var config = Services.Configuration.AppConfiguration.Instance;
                var supabaseUrl = config.SupabaseUrl;
                var apiKey = config.SupabaseAnonKey;

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(apiKey) || apiKey == "disabled")
                {
                    Debug.WriteLine("[ServiceContainer] Missing data for uninstall detection");
                    return;
                }

                // Get current app executable path
                var currentProcess = Process.GetCurrentProcess();
                var appPath = currentProcess.MainModule?.FileName ?? "";

                Debug.WriteLine($"[ServiceContainer] Creating detector for user: {userId}");
                Debug.WriteLine($"[ServiceContainer] Monitoring app: {appPath}");

                // Create JSON payload
                var jsonPayload = $"{{\"user_id\":\"{userId}\",\"event_name\":\"user_deleted_app\",\"properties\":\"{{\\\"deletion_time\\\":\\\"{DateTime.UtcNow:o}\\\",\\\"platform\\\":\\\"Windows\\\"}}\",\"session_id\":\"uninstall_detector\"}}";

                // Create platform-specific detector script
                var tempDir = Path.GetTempPath();
                string scriptPath;
                string scriptContent;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    scriptPath = Path.Combine(tempDir, $"uninstall_detector_{userId}.bat");
                    scriptContent = $@"@echo off
timeout /t 15 /nobreak >nul 2>nul
if not exist ""{appPath}"" (
    echo Sending uninstall notification for user {userId}
    curl -s -X POST ""{supabaseUrl}/rest/v1/events"" -H ""apikey: {apiKey}"" -H ""Authorization: Bearer {apiKey}"" -H ""Content-Type: application/json"" -d ""{jsonPayload}""
)
del ""%~f0"" >nul 2>nul";
                }
                else
                {
                    var unixJsonPayload = $"{{\\\"user_id\\\":\\\"{userId}\\\",\\\"event_name\\\":\\\"user_deleted_app\\\",\\\"properties\\\":\\\"{{\\\\\\\"deletion_time\\\\\\\":\\\\\\\"{DateTime.UtcNow:o}\\\\\\\",\\\\\\\"platform\\\\\\\":\\\\\\\"Unix\\\\\\\"}}\\\",\\\"session_id\\\":\\\"uninstall_detector\\\"}}";
                    scriptPath = Path.Combine(tempDir, $"uninstall_detector_{userId}.sh");
                    scriptContent = $@"#!/bin/bash
sleep 15
if [ ! -e ""{appPath}"" ]; then
    echo ""Sending uninstall notification for user {userId}""
    curl -s -X POST ""{supabaseUrl}/rest/v1/events"" \
         -H ""apikey: {apiKey}"" \
         -H ""Authorization: Bearer {apiKey}"" \
         -H ""Content-Type: application/json"" \
         -d '{unixJsonPayload}'
fi
rm ""$0"" 2>/dev/null";
                }

                await File.WriteAllTextAsync(scriptPath, scriptContent);

                // Make executable on Unix
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit();
                }

                // Launch the detector script in background
                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c start /b \"{scriptPath}\"";
                }
                else
                {
                    startInfo.FileName = "/bin/bash";
                    startInfo.Arguments = $"\"{scriptPath}\" &";
                }

                Process.Start(startInfo);
                Debug.WriteLine("[ServiceContainer] Uninstall detector created and launched");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceContainer] Error creating uninstall detector: {ex.Message}");
            }
        }
    }
}
