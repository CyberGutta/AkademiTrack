using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Thread-safe service locator for dependency injection
    /// NOTE: Consider migrating to Microsoft.Extensions.DependencyInjection for better DI support
    /// </summary>
    public class ServiceLocator
    {
        private static readonly Lazy<ServiceLocator> _instance = 
            new Lazy<ServiceLocator>(() => new ServiceLocator(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<Type, Func<object>> _factories = new();
        private readonly object _lock = new object();

        public static ServiceLocator Instance => _instance.Value;

        private ServiceLocator() { }

        /// <summary>
        /// Register a singleton service instance
        /// </summary>
        public void RegisterSingleton<TInterface, TImplementation>(TImplementation instance)
            where TImplementation : class, TInterface
        {
            lock (_lock)
            {
                _services[typeof(TInterface)] = instance;
            }
        }

        /// <summary>
        /// Register a factory for creating service instances
        /// </summary>
        public void RegisterFactory<TInterface>(Func<TInterface> factory)
        {
            lock (_lock)
            {
                _factories[typeof(TInterface)] = () => factory();
            }
        }

        /// <summary>
        /// Get a service instance (thread-safe)
        /// </summary>
        public T GetService<T>()
        {
            var type = typeof(T);
            
            lock (_lock)
            {
                // Check for existing singleton
                if (_services.TryGetValue(type, out var service))
                {
                    return (T)service;
                }
                
                // Check for factory
                if (_factories.TryGetValue(type, out var factory))
                {
                    var instance = factory();
                    _services[type] = instance; // Cache as singleton
                    return (T)instance;
                }
            }
            
            throw new InvalidOperationException($"Service of type {type.Name} is not registered. Please register it in InitializeServices().");
        }

        /// <summary>
        /// Check if a service is registered
        /// </summary>
        public bool IsRegistered<T>()
        {
            var type = typeof(T);
            lock (_lock)
            {
                return _services.ContainsKey(type) || _factories.ContainsKey(type);
            }
        }

        /// <summary>
        /// Initialize all core services
        /// </summary>
        public static void InitializeServices()
        {
            var locator = Instance;
            
            // Register core services
            locator.RegisterFactory<ILoggingService>(() => new LoggingService());
            locator.RegisterFactory<INotificationService>(() => new NotificationService());
            locator.RegisterFactory<ISettingsService>(() => new SettingsService());
            locator.RegisterFactory<AnalyticsService>(() => new AnalyticsService());
            locator.RegisterFactory<IAutomationService>(() => 
            {
                var loggingService = locator.GetService<ILoggingService>();
                var notificationService = locator.GetService<INotificationService>();
                return new AutomationService(loggingService, notificationService);
            });
            
            // Initialize settings service immediately
            var settingsService = locator.GetService<ISettingsService>();
            _ = settingsService.LoadSettingsAsync();
            
            // Initialize analytics service and start session
            var analyticsService = locator.GetService<AnalyticsService>();
            _ = analyticsService.StartSessionAsync();
            
            var loggingService = locator.GetService<ILoggingService>();
            var retentionManager = new LogRetentionManager(loggingService);
            retentionManager.Start();
        }

        /// <summary>
        /// Clear all registered services (useful for testing)
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                // Dispose any IDisposable services
                foreach (var service in _services.Values)
                {
                    if (service is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error disposing service: {ex.Message}");
                        }
                    }
                }
                
                _services.Clear();
                _factories.Clear();
            }
        }
    }
}