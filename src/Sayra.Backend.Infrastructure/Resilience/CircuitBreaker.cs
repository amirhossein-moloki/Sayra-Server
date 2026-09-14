using System;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Resilience;

#nullable enable

namespace Sayra.Backend.Infrastructure.Resilience
{
    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly object _lock = new();
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;
        private readonly ILogger? _logger;

        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private DateTime _lastStateChangeUtc = DateTime.MinValue;

        public string DependencyName { get; }

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

        public CircuitBreaker(
            string dependencyName,
            int failureThreshold = 5,
            TimeSpan? breakDuration = null,
            ILogger? logger = null)
        {
            DependencyName = string.IsNullOrWhiteSpace(dependencyName) ? "Default" : dependencyName;
            _failureThreshold = failureThreshold > 0 ? failureThreshold : 5;
            _breakDuration = breakDuration ?? TimeSpan.FromSeconds(15);
            _logger = logger;
        }

        public bool AllowExecution()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Closed)
                {
                    return true;
                }

                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _lastStateChangeUtc >= _breakDuration)
                    {
                        TransitionTo(CircuitState.HalfOpen);
                        return true;
                    }
                    return false;
                }

                // HalfOpen allows one trial execution
                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                if (_state == CircuitState.HalfOpen)
                {
                    TransitionTo(CircuitState.Closed);
                }
            }
        }

        public void RecordFailure(Exception? ex = null)
        {
            lock (_lock)
            {
                _consecutiveFailures++;
                if (_state == CircuitState.HalfOpen)
                {
                    TransitionTo(CircuitState.Open);
                }
                else if (_state == CircuitState.Closed && _consecutiveFailures >= _failureThreshold)
                {
                    TransitionTo(CircuitState.Open);
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                TransitionTo(CircuitState.Closed);
            }
        }

        private void TransitionTo(CircuitState newState)
        {
            if (_state != newState)
            {
                var oldState = _state;
                _state = newState;
                _lastStateChangeUtc = DateTime.UtcNow;
                _logger?.LogWarning("Circuit breaker for dependency {DependencyName} transitioned from {OldState} to {NewState}.", DependencyName, oldState, newState);
            }
        }
    }
}
