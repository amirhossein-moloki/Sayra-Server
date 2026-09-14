using System;

#nullable enable

namespace Sayra.Backend.Application.Resilience
{
    public enum ResilienceFailureCategory
    {
        Retryable,
        NonRetryable,
        FailFast,
        Degraded,
        RequiresReconciliation
    }

    public enum OperationRetrySafety
    {
        SafeRead,
        IdempotentWrite,
        ConditionallyRetryable,
        NonRetryableWrite,
        RequiresReconciliation
    }

    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    public record ResilienceOperationResult<T>(
        bool IsSuccess,
        T? Value,
        ResilienceFailureCategory? FailureCategory,
        string? ErrorMessage,
        Exception? Exception,
        int RetryAttempts,
        bool IsDegraded,
        TimeSpan Duration);
}
