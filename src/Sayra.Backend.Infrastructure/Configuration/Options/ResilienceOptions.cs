using System;

namespace Sayra.Backend.Infrastructure.Configuration.Options
{
    public class ResilienceOptions
    {
        public const string SectionName = "Resilience";

        public int MaxRetryAttempts { get; set; } = 3;
        public double InitialBackoffSeconds { get; set; } = 0.5;
        public double MaxBackoffSeconds { get; set; } = 5.0;
        public double JitterFactor { get; set; } = 0.2;
        public double OverallTimeoutSeconds { get; set; } = 10.0;
        public double AttemptTimeoutSeconds { get; set; } = 3.0;
        public int CircuitBreakerFailureThreshold { get; set; } = 5;
        public double CircuitBreakerBreakDurationSeconds { get; set; } = 15.0;
    }
}
