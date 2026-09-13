using System;

namespace Sayra.Backend.Application.OfflineQueue
{
    public enum FailureCategory
    {
        Retryable,
        NonRetryable,
        SecurityViolation,
        Conflict,
        Expired,
        Malformed
    }

    public record FailureClassificationResult(
        FailureCategory Category,
        string FailureCode,
        string Description,
        bool IsRetryable);

    public interface IFailureClassificationService
    {
        FailureClassificationResult ClassifyReasonCode(string reasonCode, string? errorMessage = null);
        FailureClassificationResult ClassifyException(Exception exception);
    }
}
