using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.Resilience;

#nullable enable

namespace Sayra.Backend.Infrastructure.Diagnostics
{
    public class ResilienceMetrics : IResilienceMetrics
    {
        public const string MeterName = "Sayra.Backend.Resilience";

        private readonly Meter _meter;
        private readonly Counter<long> _retryAttemptsCounter;
        private readonly Counter<long> _retryExhaustedCounter;
        private readonly Counter<long> _timeoutsCounter;
        private readonly Counter<long> _circuitBreakerTripsCounter;
        private readonly Counter<long> _fallbacksCounter;
        private readonly Counter<long> _cancellationsCounter;

        public ResilienceMetrics()
        {
            _meter = new Meter(MeterName, "1.0.0");
            _retryAttemptsCounter = _meter.CreateCounter<long>("resilience_retry_attempts_total", "attempts", "Total number of resilience retry attempts");
            _retryExhaustedCounter = _meter.CreateCounter<long>("resilience_retry_exhausted_total", "failures", "Total operations where retries were exhausted");
            _timeoutsCounter = _meter.CreateCounter<long>("resilience_timeouts_total", "timeouts", "Total operation timeouts");
            _circuitBreakerTripsCounter = _meter.CreateCounter<long>("resilience_circuit_breaker_trips_total", "trips", "Total circuit breaker state trips");
            _fallbacksCounter = _meter.CreateCounter<long>("resilience_fallbacks_total", "fallbacks", "Total degraded fallbacks executed");
            _cancellationsCounter = _meter.CreateCounter<long>("resilience_cancellations_total", "cancellations", "Total operation cancellations");
        }

        public void RecordRetryAttempt(string dependency, string operationName, int attemptNumber, string failureCategory)
        {
            _retryAttemptsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("operation", SanitizeLabel(operationName)),
                new("attempt", attemptNumber),
                new("category", SanitizeLabel(failureCategory))
            });
        }

        public void RecordRetryExhausted(string dependency, string operationName, int totalAttempts)
        {
            _retryExhaustedCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("operation", SanitizeLabel(operationName)),
                new("attempts", totalAttempts)
            });
        }

        public void RecordTimeout(string dependency, string operationName, bool isOverallTimeout)
        {
            _timeoutsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("operation", SanitizeLabel(operationName)),
                new("timeout_type", isOverallTimeout ? "overall" : "attempt")
            });
        }

        public void RecordCircuitBreakerTrip(string dependency, string fromState, string toState)
        {
            _circuitBreakerTripsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("from_state", SanitizeLabel(fromState)),
                new("to_state", SanitizeLabel(toState))
            });
        }

        public void RecordFallback(string dependency, string operationName, string fallbackType)
        {
            _fallbacksCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("operation", SanitizeLabel(operationName)),
                new("fallback_type", SanitizeLabel(fallbackType))
            });
        }

        public void RecordCancellation(string dependency, string operationName)
        {
            _cancellationsCounter.Add(1, new KeyValuePair<string, object?>[]
            {
                new("dependency", SanitizeLabel(dependency)),
                new("operation", SanitizeLabel(operationName))
            });
        }

        private static string SanitizeLabel(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            string trimmed = input.Trim().ToLowerInvariant();
            return trimmed.Length > 64 ? trimmed.Substring(0, 64) : trimmed;
        }
    }
}
