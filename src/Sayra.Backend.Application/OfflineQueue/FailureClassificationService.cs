using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class FailureClassificationService : IFailureClassificationService
    {
        public FailureClassificationResult ClassifyReasonCode(string reasonCode, string? errorMessage = null)
        {
            if (string.IsNullOrWhiteSpace(reasonCode))
            {
                return new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "UNKNOWN_FAILURE",
                    errorMessage ?? "Unknown failure encountered.",
                    IsRetryable: true);
            }

            string upperReason = reasonCode.ToUpperInvariant();

            if (upperReason.Contains("INVALID") ||
                upperReason.Contains("MALFORMED") ||
                upperReason.Contains("REJECTED") ||
                upperReason.Contains("PERMANENT") ||
                upperReason.Contains("SECURITY") ||
                upperReason.Contains("VIOLATION") ||
                upperReason.Contains("EXPIRED"))
            {
                return new FailureClassificationResult(
                    FailureCategory.NonRetryable,
                    reasonCode,
                    errorMessage ?? "Non-retryable event failure.",
                    IsRetryable: false);
            }

            return upperReason switch
            {
                OfflineReasonCode.IdentityMismatch => new FailureClassificationResult(
                    FailureCategory.SecurityViolation,
                    OfflineReasonCode.IdentityMismatch,
                    errorMessage ?? "Payload identity does not match authenticated connection.",
                    IsRetryable: false),

                OfflineReasonCode.WorkstationNotFound => new FailureClassificationResult(
                    FailureCategory.NonRetryable,
                    OfflineReasonCode.WorkstationNotFound,
                    errorMessage ?? "Workstation does not exist on server.",
                    IsRetryable: false),

                OfflineReasonCode.SiteMismatch => new FailureClassificationResult(
                    FailureCategory.NonRetryable,
                    OfflineReasonCode.SiteMismatch,
                    errorMessage ?? "Workstation site is inactive or mismatched.",
                    IsRetryable: false),

                OfflineReasonCode.ExpiredRetention => new FailureClassificationResult(
                    FailureCategory.Expired,
                    OfflineReasonCode.ExpiredRetention,
                    errorMessage ?? "Event timestamp exceeds retention policy threshold.",
                    IsRetryable: false),

                OfflineReasonCode.FutureTimestampClockSkew => new FailureClassificationResult(
                    FailureCategory.Malformed,
                    OfflineReasonCode.FutureTimestampClockSkew,
                    errorMessage ?? "Event timestamp violates future clock skew limit.",
                    IsRetryable: false),

                OfflineReasonCode.SequenceGapWait => new FailureClassificationResult(
                    FailureCategory.Retryable,
                    OfflineReasonCode.SequenceGapWait,
                    errorMessage ?? "Event held waiting for preceding sequence numbers.",
                    IsRetryable: true),

                OfflineReasonCode.StaleSequence => new FailureClassificationResult(
                    FailureCategory.Conflict,
                    OfflineReasonCode.StaleSequence,
                    errorMessage ?? "Stale or duplicate sequence number received.",
                    IsRetryable: false),

                OfflineReasonCode.PayloadHashConflict => new FailureClassificationResult(
                    FailureCategory.Conflict,
                    OfflineReasonCode.PayloadHashConflict,
                    errorMessage ?? "Retransmitted EventId with altered payload content.",
                    IsRetryable: false),

                OfflineReasonCode.InvalidSessionReference => new FailureClassificationResult(
                    FailureCategory.Conflict,
                    OfflineReasonCode.InvalidSessionReference,
                    errorMessage ?? "Referenced session does not exist.",
                    IsRetryable: false),

                OfflineReasonCode.MalformedPayload => new FailureClassificationResult(
                    FailureCategory.Malformed,
                    OfflineReasonCode.MalformedPayload,
                    errorMessage ?? "Malformed JSON payload or payload size limits exceeded.",
                    IsRetryable: false),

                "TRANSIENT_FAILURE" or "NETWORK_TIMEOUT" or "CONNECTION_RESET" => new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "TRANSIENT_FAILURE",
                    errorMessage ?? "Transient transport or network failure.",
                    IsRetryable: true),

                "DATABASE_UNAVAILABLE" or "DB_TIMEOUT" => new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "DATABASE_UNAVAILABLE",
                    errorMessage ?? "Transient database error or connection timeout.",
                    IsRetryable: true),

                "CONCURRENCY_CONFLICT" => new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "CONCURRENCY_CONFLICT",
                    errorMessage ?? "Optimistic concurrency conflict occurred.",
                    IsRetryable: true),

                "RETRY_EXHAUSTED" => new FailureClassificationResult(
                    FailureCategory.NonRetryable,
                    "RETRY_EXHAUSTED",
                    errorMessage ?? "Maximum delivery retry attempts exceeded.",
                    IsRetryable: false),

                _ => new FailureClassificationResult(
                    FailureCategory.Retryable,
                    reasonCode,
                    errorMessage ?? "General operational error.",
                    IsRetryable: true)
            };
        }

        public FailureClassificationResult ClassifyException(Exception exception)
        {
            if (exception == null)
            {
                return ClassifyReasonCode("UNKNOWN_FAILURE", "Null exception provided.");
            }

            if (exception is TimeoutException or SocketException or IOException or TaskCanceledException)
            {
                return new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "TRANSIENT_FAILURE",
                    $"Transient network or I/O failure: {exception.Message}",
                    IsRetryable: true);
            }

            if (exception.GetType().Name.Contains("DbUpdateConcurrencyException") || exception.GetType().Name.Contains("ConcurrencyException"))
            {
                return new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "CONCURRENCY_CONFLICT",
                    $"Concurrency failure: {exception.Message}",
                    IsRetryable: true);
            }

            if (exception.GetType().Name.Contains("NpgsqlException") || exception.GetType().Name.Contains("DbException") || exception.GetType().Name.Contains("SqliteException"))
            {
                return new FailureClassificationResult(
                    FailureCategory.Retryable,
                    "DATABASE_UNAVAILABLE",
                    $"Database operational failure: {exception.Message}",
                    IsRetryable: true);
            }

            return new FailureClassificationResult(
                FailureCategory.Retryable,
                "SYSTEM_EXCEPTION",
                $"Unhandled exception: {exception.Message}",
                IsRetryable: true);
        }
    }
}
