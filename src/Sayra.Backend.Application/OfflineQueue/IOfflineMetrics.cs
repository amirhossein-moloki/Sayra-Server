using System;

namespace Sayra.Backend.Application.OfflineQueue
{
    public interface IOfflineMetrics
    {
        // Queue Metrics
        void RecordQueueEnqueue(string eventType, string reliabilityClass);
        void RecordQueueDequeue(string eventType, string reliabilityClass);
        void RecordQueueExpiration(string eventType, string reliabilityClass);
        void RecordQueueOverflow(string eventType, string reliabilityClass);
        void RecordQueueState(long itemCount, long totalBytes, double oldestItemAgeSeconds);

        // Synchronization Metrics
        void RecordSyncBatchStarted();
        void RecordSyncBatchCompleted(double durationSeconds, int totalItems);
        void RecordSyncBatchFailed(string failureCategory, double durationSeconds);
        void RecordSyncEventSubmitted(string eventType, string reliabilityClass);
        void RecordSyncEventAccepted(string eventType, string reliabilityClass);
        void RecordSyncEventRejected(string eventType, string failureCategory);
        void RecordSyncEventDuplicated(string eventType);
        void RecordSyncEventConflicted(string eventType, string failureCategory);
        void RecordSyncEventDeferred(string eventType);
        void RecordSyncEventExpired(string eventType);

        // Retry & DLQ Metrics
        void RecordRetryAttempt(string failureCategory, bool isPermanent);
        void RecordEventMovedToDlq(string failureCategory, string eventType);
        void RecordDlqProcessingAttempt(string action, string resultCategory);

        // Ordering & Reconciliation Metrics
        void RecordSequenceGapDetected(string eventType);
        void RecordReconciliationAttempt(string eventType);
        void RecordReconciliationSuccess(string eventType, double durationSeconds);
        void RecordReconciliationConflict(string eventType, string failureCategory);
        void RecordReconciliationFailure(string eventType, string failureCategory, double durationSeconds);

        // Idempotency Metrics
        void RecordIdempotentDuplicate(string eventType);
        void RecordIdempotencyConflict(string eventType, string failureCategory);

        // Security Metrics
        void RecordSecurityRejection(string failureCategory);

        // Infrastructure Metrics
        void RecordInfrastructureFailure(string dependencyName, string operation);
    }
}
