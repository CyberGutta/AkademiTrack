using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using AkademiTrack.Services.Interfaces;
using AkademiTrack.Services.DependencyInjection;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Legacy Service Locator - now uses ServiceContainer as backend
    /// </summary>
    [Obsolete("Use ServiceContainer directly instead of ServiceLocator")]
    public class ServiceLocator
    {
        private static readonly Lazy<ServiceLocator> _instance = 
            new Lazy<ServiceLocator>(() => new ServiceLocator(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static ServiceLocator Instance => _instance.Value;

        private ServiceLocator() { }

        /// <summary>
        /// Get a service instance (delegates to ServiceContainer)
        /// </summary>
        [Obsolete("Use ServiceContainer.GetService<T>() instead")]
        public T GetService<T>() where T : notnull
        {
            try
            {
                return ServiceContainer.GetService<T>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceLocator] Legacy service resolution failed for {typeof(T).Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if a service is registered (delegates to ServiceContainer)
        /// </summary>
        [Obsolete("Use ServiceContainer.IsServiceRegistered<T>() instead")]
        public bool IsRegistered<T>()
        {
            return ServiceContainer.IsServiceRegistered<T>();
        }

        /// <summary>
        /// Initialize all core services (delegates to ServiceContainer)
        /// </summary>
        [Obsolete("Use ServiceContainer.Initialize() instead")]
        public static void InitializeServices()
        {
            Debug.WriteLine("[ServiceLocator] DEPRECATED: Delegating to ServiceContainer.Initialize()");
            ServiceContainer.Initialize();
        }

        /// <summary>
        /// Clear all registered services (delegates to ServiceContainer)
        /// </summary>
        [Obsolete("Use ServiceContainer.ShutdownAsync() instead")]
        public void Clear()
        {
            Debug.WriteLine("[ServiceLocator] DEPRECATED: Clear() called - use ServiceContainer.ShutdownAsync() instead");
        }

        // Legacy registration methods - no longer functional, just for compatibility
        [Obsolete("Service registration is now handled by ServiceContainer configuration")]
        public void RegisterSingleton<TInterface, TImplementation>(TImplementation instance)
            where TImplementation : class, TInterface
        {
            Debug.WriteLine($"[ServiceLocator] DEPRECATED: RegisterSingleton called for {typeof(TInterface).Name} - configure in ServiceContainer instead");
        }

        [Obsolete("Service registration is now handled by ServiceContainer configuration")]
        public void RegisterFactory<TInterface>(Func<TInterface> factory)
        {
            Debug.WriteLine($"[ServiceLocator] DEPRECATED: RegisterFactory called for {typeof(TInterface).Name} - configure in ServiceContainer instead");
        }
    }
}