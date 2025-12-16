using System;
using System.Collections.Generic;
using AkademiTrack.Services.Interfaces;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Simple service locator for dependency injection
    /// </summary>
    public class ServiceLocator
    {
        private static ServiceLocator? _instance;
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<Type, Func<object>> _factories = new();

        public static ServiceLocator Instance => _instance ??= new ServiceLocator();

        private ServiceLocator() { }

        /// <summary>
        /// Register a singleton service instance
        /// </summary>
        public void RegisterSingleton<TInterface, TImplementation>(TImplementation instance)
            where TImplementation : class, TInterface
        {
            _services[typeof(TInterface)] = instance;
        }

        /// <summary>
        /// Register a factory for creating service instances
        /// </summary>
        public void RegisterFactory<TInterface>(Func<TInterface> factory)
        {
            _factories[typeof(TInterface)] = () => factory();
        }

        /// <summary>
        /// Get a service instance
        /// </summary>
        public T GetService<T>()
        {
            var type = typeof(T);
            
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
            
            throw new InvalidOperationException($"Service of type {type.Name} is not registered");
        }

        /// <summary>
        /// Check if a service is registered
        /// </summary>
        public bool IsRegistered<T>()
        {
            var type = typeof(T);
            return _services.ContainsKey(type) || _factories.ContainsKey(type);
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
            locator.RegisterFactory<IAutomationService>(() => 
            {
                var loggingService = locator.GetService<ILoggingService>();
                var notificationService = locator.GetService<INotificationService>();
                return new AutomationService(loggingService, notificationService);
            });
            
            // Initialize settings service immediately
            var settingsService = locator.GetService<ISettingsService>();
            _ = settingsService.LoadSettingsAsync();
        }

        /// <summary>
        /// Clear all registered services (useful for testing)
        /// </summary>
        public void Clear()
        {
            _services.Clear();
            _factories.Clear();
        }
    }
}