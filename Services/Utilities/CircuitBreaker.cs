using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Circuit breaker pattern to prevent cascading failures
    /// </summary>
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state;
        private readonly object _lock = new object();

        public enum CircuitState
        {
            Closed,  // Normal operation
            Open,    // Circuit is open, requests fail immediately
            HalfOpen // Testing if service has recovered
        }

        public CircuitState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        public bool IsOpen => State == CircuitState.Open;

        /// <summary>
        /// </summary>
        /// <param name="failureThreshold">Number of failures before opening circuit</param>
        /// <param name="resetTimeoutSeconds">Seconds to wait before attempting to close circuit</param>
        public CircuitBreaker(int failureThreshold = 3, int resetTimeoutSeconds = 60)
        {
            if (failureThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            if (resetTimeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(resetTimeoutSeconds));

            _failureThreshold = failureThreshold;
            _resetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);
            _failureCount = 0;
            _lastFailureTime = DateTime.MinValue;
            _state = CircuitState.Closed;
        }

        /// <summary>
        /// Execute an action through the circuit breaker
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    // Check if we should try to close the circuit
                    if (DateTime.Now - _lastFailureTime >= _resetTimeout)
                    {
                        Debug.WriteLine("[CircuitBreaker] Attempting to close circuit (half-open state)");
                        _state = CircuitState.HalfOpen;
                    }
                    else
                    {
                        var remainingTime = _resetTimeout - (DateTime.Now - _lastFailureTime);
                        throw new CircuitBreakerOpenException(
                            $"Circuit breaker is open. Service unavailable. Retry in {remainingTime.TotalSeconds:F0} seconds.");
                    }
                }
            }

            try
            {
                var result = await action();
                RecordSuccess();
                return result;
            }
            catch (Exception)
            {
                RecordFailure();
                throw;
            }
        }

        /// <summary>
        /// </summary>
        public async Task ExecuteAsync(Func<Task> action)
        {
            await ExecuteAsync(async () =>
            {
                await action();
                return true;
            });
        }

        /// <summary>
        /// Record a successful operation
        /// </summary>
        public void RecordSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
                
                if (_state == CircuitState.HalfOpen)
                {
                    Debug.WriteLine("[CircuitBreaker] Circuit closed after successful test");
                    _state = CircuitState.Closed;
                }
            }
        }

        /// <summary>
        /// Record a failed operation
        /// </summary>
        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.Now;

                if (_state == CircuitState.HalfOpen)
                {
                    Debug.WriteLine("[CircuitBreaker] Circuit opened again after failed test");
                    _state = CircuitState.Open;
                }
                else if (_failureCount >= _failureThreshold)
                {
                    Debug.WriteLine($"[CircuitBreaker] Circuit opened after {_failureCount} failures");
                    _state = CircuitState.Open;
                }
                else
                {
                    Debug.WriteLine($"[CircuitBreaker] Failure {_failureCount}/{_failureThreshold}");
                }
            }
        }

        /// <summary>
        /// Manually reset the circuit breaker
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
                Debug.WriteLine("[CircuitBreaker] Circuit manually reset");
            }
        }
    }

    /// <summary>
    /// Exception thrown when circuit breaker is open
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message)
        {
        }
    }
}
