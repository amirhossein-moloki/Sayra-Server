using System;

namespace Sayra.Backend.Application.Resilience
{
    public class CircuitBreakerOpenException : InvalidOperationException
    {
        public string DependencyName { get; }

        public CircuitBreakerOpenException(string dependencyName)
            : base($"Circuit breaker for dependency '{dependencyName}' is OPEN. Operation rejected to prevent downstream load amplification.")
        {
            DependencyName = dependencyName;
        }

        public CircuitBreakerOpenException(string dependencyName, string message)
            : base(message)
        {
            DependencyName = dependencyName;
        }
    }
}
