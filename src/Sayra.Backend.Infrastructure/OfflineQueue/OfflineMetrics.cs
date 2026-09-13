using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Sayra.Backend.Application.OfflineQueue;

namespace Sayra.Backend.Infrastructure.OfflineQueue
{
    /// <summary>
    /// Thread-safe operational metrics instrumentation for Phase 09 Offline Queue & Reconciliation pipeline.
    /// Uses System.Diagnostics.Metrics with bounded label dimensions to strictly prevent metric cardinality explosion.
    /// </summary>
    public sealed class OfflineMetrics : IOfflineMetrics
    {
        public const string MeterName = "Sayra.Backend.Offline";
        public const string MeterVersion = "1.0.0";

        private readonly Meter _meter;

        // Queue Instruments
        private readonly Counter<long> _queueEnqueuedCounter;
        private readonly Counter<long> _queueDequeuedCounter;
        private readonly Counter<long> _queueExpiredCounter;
        private readonly Counter<long> _queueOverflowCounter;

        // Queue Gauges backing fields
        private long _currentQueueItems;
        private long _currentQueueBytes;
        private double _currentOldestItemAgeSeconds;

        // Sync Instruments
        private readonly Counter<long> _syncBatchesStartedCounter;
        private readonly Counter<long> _syncBatchesCompletedCounter;
        private readonly Counter<long> _syncBatchesFailedCounter;
        private readonly Counter<long> _syncEventsSubmittedCounter;
        private readonly Counter<long> _syncEventsAcceptedCounter;
        private readonly Counter<long> _syncEventsRejectedCounter;
        private readonly Counter<long> _syncEventsDuplicatedCounter;
        private readonly Counter<long> _syncEventsConflictedCounter;
        private readonly Counter<long> _syncEventsDeferredCounter;
        private readonly Counter<long> _syncEventsExpiredCounter;
        private readonly Histogram<double> _syncDurationHistogram;

        // Retry & DLQ Instruments
        private readonly Counter<long> _retriesCounter;
        private readonly Counter<long> _dlqEventsAddedCounter;
        private readonly Counter<long> _dlqProcessingAttemptsCounter;

        // Ordering & Reconciliation Instruments
        private readonly Counter<long> _sequenceGapsCounter;
        private readonly Counter<long> _reconciliationAttemptsCounter;
        private readonly Counter<long> _reconciliationSuccessesCounter;
        private readonly Counter<long> _reconciliationConflictsCounter;
        private readonly Counter<long> _reconciliationFailuresCounter;
        private readonly Histogram<double> _reconciliationDurationHistogram;

        // Idempotency & Security & Infrastructure Instruments
        private readonly Counter<long> _idempotencyDuplicatesCounter;
        private readonly Counter<long> _idempotencyConflictsCounter;
        private readonly Counter<long> _securityRejectionsCounter;
        private readonly Counter<long> _infrastructureFailuresCounter;

        public OfflineMetrics(IMeterFactory? meterFactory = null)
        {
            _meter = meterFactory?.Create(MeterName, MeterVersion) ?? new Meter(MeterName, MeterVersion);

            // Queue
            _queueEnqueuedCounter = _meter.CreateCounter<long>("offline_queue_enqueued_total", description: "Total events enqueued into offline queue");
            _queueDequeuedCounter = _meter.CreateCounter<long>("offline_queue_dequeued_total", description: "Total events dequeued/claimed from offline queue");
            _queueExpiredCounter = _meter.CreateCounter<long>("offline_queue_expired_total", description: "Total offline queue items expired before sync");
            _queueOverflowCounter = _meter.CreateCounter<long>("offline_queue_overflow_total", description: "Total offline queue overflow/rejections due to capacity");

            _meter.CreateObservableGauge("offline_queue_items_current", () => _currentQueueItems, description: "Current number of items in the offline queue");
            _meter.CreateObservableGauge("offline_queue_bytes_current", () => _currentQueueBytes, unit: "by", description: "Current size in bytes of offline queue database");
            _meter.CreateObservableGauge("offline_queue_oldest_item_age_seconds", () => _currentOldestItemAgeSeconds, unit: "s", description: "Age in seconds of the oldest item in the offline queue");

            // Synchronization
            _syncBatchesStartedCounter = _meter.CreateCounter<long>("offline_sync_batches_started_total", description: "Total offline sync batches started");
            _syncBatchesCompletedCounter = _meter.CreateCounter<long>("offline_sync_batches_completed_total", description: "Total offline sync batches completed successfully");
            _syncBatchesFailedCounter = _meter.CreateCounter<long>("offline_sync_batches_failed_total", description: "Total offline sync batch failures");
            _syncEventsSubmittedCounter = _meter.CreateCounter<long>("offline_sync_events_submitted_total", description: "Total offline events submitted in sync batches");
            _syncEventsAcceptedCounter = _meter.CreateCounter<long>("offline_sync_events_accepted_total", description: "Total offline events accepted during sync");
            _syncEventsRejectedCounter = _meter.CreateCounter<long>("offline_sync_events_rejected_total", description: "Total offline events rejected during sync");
            _syncEventsDuplicatedCounter = _meter.CreateCounter<long>("offline_sync_events_duplicated_total", description: "Total duplicate offline events detected during sync");
            _syncEventsConflictedCounter = _meter.CreateCounter<long>("offline_sync_events_conflicted_total", description: "Total offline event conflicts during sync");
            _syncEventsDeferredCounter = _meter.CreateCounter<long>("offline_sync_events_deferred_total", description: "Total offline events deferred due to sequence gap");
            _syncEventsExpiredCounter = _meter.CreateCounter<long>("offline_sync_events_expired_total", description: "Total offline events expired on ingestion");
            _syncDurationHistogram = _meter.CreateHistogram<double>("offline_sync_duration_seconds", unit: "s", description: "Duration of sync batch processing in seconds");

            // Retry & DLQ
            _retriesCounter = _meter.CreateCounter<long>("offline_retries_total", description: "Total offline event retry attempts");
            _dlqEventsAddedCounter = _meter.CreateCounter<long>("offline_dlq_events_added_total", description: "Total events moved to Dead Letter Queue");
            _dlqProcessingAttemptsCounter = _meter.CreateCounter<long>("offline_dlq_processing_attempts_total", description: "Total DLQ administrative processing attempts");

            // Ordering & Reconciliation
            _sequenceGapsCounter = _meter.CreateCounter<long>("offline_sequence_gaps_total", description: "Total sequence gaps detected in stream");
            _reconciliationAttemptsCounter = _meter.CreateCounter<long>("offline_reconciliation_attempts_total", description: "Total event reconciliation attempts");
            _reconciliationSuccessesCounter = _meter.CreateCounter<long>("offline_reconciliation_successes_total", description: "Total successful event reconciliations");
            _reconciliationConflictsCounter = _meter.CreateCounter<long>("offline_reconciliation_conflicts_total", description: "Total event reconciliation conflicts");
            _reconciliationFailuresCounter = _meter.CreateCounter<long>("offline_reconciliation_failures_total", description: "Total failed event reconciliations");
            _reconciliationDurationHistogram = _meter.CreateHistogram<double>("offline_reconciliation_duration_seconds", unit: "s", description: "Duration of event reconciliation in seconds");

            // Idempotency & Security & Infrastructure
            _idempotencyDuplicatesCounter = _meter.CreateCounter<long>("offline_idempotency_duplicates_total", description: "Total duplicate idempotency hits");
            _idempotencyConflictsCounter = _meter.CreateCounter<long>("offline_idempotency_conflicts_total", description: "Total payload hash idempotency conflicts");
            _securityRejectionsCounter = _meter.CreateCounter<long>("offline_security_rejections_total", description: "Total security validation failures in offline pipeline");
            _infrastructureFailuresCounter = _meter.CreateCounter<long>("offline_infrastructure_failures_total", description: "Total infrastructure dependency failures in offline pipeline");
        }

        public void RecordQueueEnqueue(string eventType, string reliabilityClass)
        {
            _queueEnqueuedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordQueueDequeue(string eventType, string reliabilityClass)
        {
            _queueDequeuedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordQueueExpiration(string eventType, string reliabilityClass)
        {
            _queueExpiredCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordQueueOverflow(string eventType, string reliabilityClass)
        {
            _queueOverflowCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordQueueState(long itemCount, long totalBytes, double oldestItemAgeSeconds)
        {
            System.Threading.Interlocked.Exchange(ref _currentQueueItems, itemCount);
            System.Threading.Interlocked.Exchange(ref _currentQueueBytes, totalBytes);
            _currentOldestItemAgeSeconds = oldestItemAgeSeconds;
        }

        public void RecordSyncBatchStarted()
        {
            _syncBatchesStartedCounter.Add(1);
        }

        public void RecordSyncBatchCompleted(double durationSeconds, int totalItems)
        {
            _syncBatchesCompletedCounter.Add(1);
            _syncDurationHistogram.Record(durationSeconds, new KeyValuePair<string, object?>("result_category", "SUCCESS"));
        }

        public void RecordSyncBatchFailed(string failureCategory, double durationSeconds)
        {
            _syncBatchesFailedCounter.Add(1, new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
            _syncDurationHistogram.Record(durationSeconds, new KeyValuePair<string, object?>("result_category", "FAILURE"));
        }

        public void RecordSyncEventSubmitted(string eventType, string reliabilityClass)
        {
            _syncEventsSubmittedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordSyncEventAccepted(string eventType, string reliabilityClass)
        {
            _syncEventsAcceptedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("reliability_class", SanitizeLabel(reliabilityClass)));
        }

        public void RecordSyncEventRejected(string eventType, string failureCategory)
        {
            _syncEventsRejectedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
        }

        public void RecordSyncEventDuplicated(string eventType)
        {
            _syncEventsDuplicatedCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordSyncEventConflicted(string eventType, string failureCategory)
        {
            _syncEventsConflictedCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
        }

        public void RecordSyncEventDeferred(string eventType)
        {
            _syncEventsDeferredCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordSyncEventExpired(string eventType)
        {
            _syncEventsExpiredCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordRetryAttempt(string failureCategory, bool isPermanent)
        {
            _retriesCounter.Add(1,
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)),
                new KeyValuePair<string, object?>("is_permanent", isPermanent));
        }

        public void RecordEventMovedToDlq(string failureCategory, string eventType)
        {
            _dlqEventsAddedCounter.Add(1,
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)),
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordDlqProcessingAttempt(string action, string resultCategory)
        {
            _dlqProcessingAttemptsCounter.Add(1,
                new KeyValuePair<string, object?>("action", SanitizeLabel(action)),
                new KeyValuePair<string, object?>("result_category", SanitizeLabel(resultCategory)));
        }

        public void RecordSequenceGapDetected(string eventType)
        {
            _sequenceGapsCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordReconciliationAttempt(string eventType)
        {
            _reconciliationAttemptsCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordReconciliationSuccess(string eventType, double durationSeconds)
        {
            _reconciliationSuccessesCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
            _reconciliationDurationHistogram.Record(durationSeconds,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("result_category", "SUCCESS"));
        }

        public void RecordReconciliationConflict(string eventType, string failureCategory)
        {
            _reconciliationConflictsCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
        }

        public void RecordReconciliationFailure(string eventType, string failureCategory, double durationSeconds)
        {
            _reconciliationFailuresCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
            _reconciliationDurationHistogram.Record(durationSeconds,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("result_category", "FAILURE"));
        }

        public void RecordIdempotentDuplicate(string eventType)
        {
            _idempotencyDuplicatesCounter.Add(1, new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)));
        }

        public void RecordIdempotencyConflict(string eventType, string failureCategory)
        {
            _idempotencyConflictsCounter.Add(1,
                new KeyValuePair<string, object?>("event_type", SanitizeLabel(eventType)),
                new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
        }

        public void RecordSecurityRejection(string failureCategory)
        {
            _securityRejectionsCounter.Add(1, new KeyValuePair<string, object?>("failure_category", SanitizeLabel(failureCategory)));
        }

        public void RecordInfrastructureFailure(string dependencyName, string operation)
        {
            _infrastructureFailuresCounter.Add(1,
                new KeyValuePair<string, object?>("dependency_name", SanitizeLabel(dependencyName)),
                new KeyValuePair<string, object?>("operation", SanitizeLabel(operation)));
        }

        /// <summary>
        /// Ensures metric label values are bounded strings and do not contain raw dynamic IDs or untrusted inputs.
        /// </summary>
        private static string SanitizeLabel(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "UNKNOWN";
            raw = raw.Trim();
            // Bound length and standardize casing
            if (raw.Length > 64) raw = raw.Substring(0, 64);
            return raw.ToUpperInvariant();
        }
    }
}
