using System;

#nullable enable

namespace Sayra.Backend.Application.Resilience
{
    public interface IResilienceMetrics
    {
        void RecordRetryAttempt(string dependency, string operationName, int attemptNumber, string failureCategory);
        void RecordRetryExhausted(string dependency, string operationName, int totalAttempts);
        void RecordTimeout(string dependency, string operationName, bool isOverallTimeout);
        void RecordCircuitBreakerTrip(string dependency, string fromState, string toState);
        void RecordFallback(string dependency, string operationName, string fallbackType);
        void RecordCancellation(string dependency, string operationName);
    }
}
