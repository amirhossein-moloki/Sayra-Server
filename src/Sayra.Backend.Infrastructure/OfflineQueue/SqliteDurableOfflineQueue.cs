using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.Infrastructure.OfflineQueue
{
    public class SqliteDurableOfflineQueue : IDurableOfflineQueue
    {
        private readonly SqliteOfflineQueueDbContext _dbContext;
        private readonly OfflineQueueOptions _options;
        private readonly OfflineRetryPolicyCalculator _retryCalculator;
        private readonly ILogger<SqliteDurableOfflineQueue> _logger;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public SqliteDurableOfflineQueue(
            SqliteOfflineQueueDbContext dbContext,
            IOptions<OfflineQueueOptions> options,
            ILogger<SqliteDurableOfflineQueue> logger,
            OfflineRetryPolicyCalculator? retryCalculator = null)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _options = options?.Value ?? new OfflineQueueOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryCalculator = retryCalculator ?? new OfflineRetryPolicyCalculator(new OfflineRetryOptions());

            _dbContext.Database.EnsureCreated();
        }

        public async Task<EnqueueResult> EnqueueAsync(ClientEventEnvelopeDto envelope, CancellationToken ct = default)
        {
            if (envelope == null)
            {
                return EnqueueResult.Rejected("NullEnvelope");
            }

            if (string.IsNullOrWhiteSpace(envelope.EventId))
            {
                return EnqueueResult.Rejected("MissingEventId");
            }

            string reliabilityClass = ResolveReliabilityClass(envelope.EventType);
            if (string.Equals(reliabilityClass, EventReliabilityClass.Ephemeral, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reliabilityClass, EventReliabilityClass.NotQueueable, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Event {EventId} of type {EventType} classified as {ReliabilityClass} - rejected from durable queue.",
                    envelope.EventId, envelope.EventType, reliabilityClass);
                return EnqueueResult.Rejected("NonQueueable");
            }

            string payloadJson = envelope.Payload ?? "{}";
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (payloadBytes.Length > _options.MaxPayloadSizeBytes)
            {
                _logger.LogWarning("Event {EventId} payload size {Size} bytes exceeds max allowed limit {MaxLimit} bytes.",
                    envelope.EventId, payloadBytes.Length, _options.MaxPayloadSizeBytes);
                return EnqueueResult.Rejected("OversizedPayload");
            }

            await _lock.WaitAsync(ct);
            try
            {
                bool exists = await _dbContext.QueueItems.AnyAsync(x => x.EventId == envelope.EventId, ct);
                if (exists)
                {
                    _logger.LogWarning("Duplicate EventId {EventId} rejected from local queue.", envelope.EventId);
                    return EnqueueResult.Rejected("DuplicateEventId");
                }

                await EnforceCapacityLimitsAsync(reliabilityClass, payloadBytes.Length, ct);

                DateTime nowUtc = DateTime.UtcNow;
                var entity = new DurableQueueItemEntity
                {
                    EventId = envelope.EventId,
                    EventType = envelope.EventType,
                    ClientId = envelope.ClientId,
                    WorkstationId = envelope.WorkstationId,
                    SessionId = envelope.SessionId,
                    CorrelationId = envelope.CorrelationId,
                    SequenceNumber = envelope.SequenceNumber,
                    ContractVersion = string.IsNullOrWhiteSpace(envelope.ContractVersion) ? "1.0" : envelope.ContractVersion,
                    ReliabilityClass = reliabilityClass,
                    Payload = payloadJson,
                    OccurredAt = envelope.OccurredAt == default ? nowUtc : envelope.OccurredAt.ToUniversalTime(),
                    CreatedAt = nowUtc,
                    Status = OfflineQueueItemStatus.Pending,
                    RetryCount = 0
                };

                _dbContext.QueueItems.Add(entity);
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogDebug("Successfully enqueued event {EventId} ({EventType}, {ReliabilityClass}) to local queue.",
                    entity.EventId, entity.EventType, entity.ReliabilityClass);

                return EnqueueResult.Success(entity);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true || ex.Message.Contains("UNIQUE"))
            {
                _logger.LogWarning("DbUpdateException UNIQUE constraint hit for EventId {EventId}.", envelope.EventId);
                return EnqueueResult.Rejected("DuplicateEventId");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IReadOnlyList<DurableQueueItemEntity>> ClaimBatchAsync(int maxItems, CancellationToken ct = default)
        {
            if (maxItems <= 0) return Array.Empty<DurableQueueItemEntity>();

            await _lock.WaitAsync(ct);
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                var pendingCandidates = await _dbContext.QueueItems
                    .Where(x => x.Status == OfflineQueueItemStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= nowUtc))
                    .ToListAsync(ct);

                var orderedItems = pendingCandidates
                    .OrderBy(x => GetPriorityRank(x.ReliabilityClass))
                    .ThenBy(x => x.SequenceNumber)
                    .ThenBy(x => x.OccurredAt)
                    .Take(maxItems)
                    .ToList();

                if (!orderedItems.Any())
                {
                    return Array.Empty<DurableQueueItemEntity>();
                }

                foreach (var item in orderedItems)
                {
                    item.Status = OfflineQueueItemStatus.InFlight;
                    item.LastAttemptAt = nowUtc;
                }

                await _dbContext.SaveChangesAsync(ct);
                return orderedItems.AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task AcknowledgeItemsAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
        {
            if (eventIds == null || !eventIds.Any()) return;

            var idList = eventIds.Distinct().ToList();

            await _lock.WaitAsync(ct);
            try
            {
                var items = await _dbContext.QueueItems
                    .Where(x => idList.Contains(x.EventId))
                    .ToListAsync(ct);

                foreach (var item in items)
                {
                    item.Status = OfflineQueueItemStatus.Acknowledged;
                }

                await _dbContext.SaveChangesAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task MarkFailedAsync(string eventId, string errorReason, bool isPermanent = false, CancellationToken ct = default)
        {
            await ScheduleRetryOrDeadLetterAsync(eventId, errorReason, isPermanent, null, ct);
        }

        public async Task ScheduleRetryOrDeadLetterAsync(string eventId, string errorReason, bool isPermanent, TimeSpan? backoffDelay = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;

            await _lock.WaitAsync(ct);
            try
            {
                var item = await _dbContext.QueueItems
                    .FirstOrDefaultAsync(x => x.EventId == eventId, ct);

                if (item != null)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    item.RetryCount++;
                    item.LastAttemptAt = nowUtc;
                    item.LastError = errorReason;

                    bool isExhausted = _retryCalculator.IsRetryExhausted(item.RetryCount);

                    if (isPermanent || isExhausted)
                    {
                        item.Status = OfflineQueueItemStatus.Failed;
                        item.ExpiresAt = nowUtc;
                        _logger.LogWarning("Event {EventId} permanently failed or exhausted retries ({Count}/{Max}). Status set to FAILED.",
                            eventId, item.RetryCount, _retryCalculator.MaxRetryCount);
                    }
                    else
                    {
                        item.Status = OfflineQueueItemStatus.Pending;
                        TimeSpan delay = backoffDelay ?? _retryCalculator.CalculateNextAttemptDelay(item.RetryCount);
                        item.NextAttemptAt = nowUtc.Add(delay);

                        _logger.LogInformation("Event {EventId} retry scheduled ({Count}/{Max}) after backoff {Delay}s. NextAttemptAt={NextAttemptAt}.",
                            eventId, item.RetryCount, _retryCalculator.MaxRetryCount, delay.TotalSeconds, item.NextAttemptAt);
                    }

                    await _dbContext.SaveChangesAsync(ct);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                var candidates = await _dbContext.QueueItems
                    .Where(x => x.Status == OfflineQueueItemStatus.Pending || x.Status == OfflineQueueItemStatus.InFlight || x.Status == OfflineQueueItemStatus.Failed)
                    .ToListAsync(ct);

                int expiredCount = 0;
                foreach (var item in candidates)
                {
                    bool isExpired = IsItemExpired(item, nowUtc);
                    if (isExpired)
                    {
                        item.Status = OfflineQueueItemStatus.Expired;
                        item.ExpiresAt = nowUtc;
                        expiredCount++;
                    }
                }

                if (expiredCount > 0)
                {
                    await _dbContext.SaveChangesAsync(ct);
                    _logger.LogInformation("Purged/Marked {Count} items as EXPIRED in durable offline queue.", expiredCount);
                }

                return expiredCount;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<int> StartupRecoveryAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var inFlightItems = await _dbContext.QueueItems
                    .Where(x => x.Status == OfflineQueueItemStatus.InFlight)
                    .ToListAsync(ct);

                if (!inFlightItems.Any())
                {
                    return 0;
                }

                foreach (var item in inFlightItems)
                {
                    item.Status = OfflineQueueItemStatus.Pending;
                }

                await _dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("Startup recovery reset {Count} IN_FLIGHT queue items back to PENDING.", inFlightItems.Count);

                return inFlightItems.Count;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<QueueMetricsDto> GetQueueMetricsAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var items = await _dbContext.QueueItems.ToListAsync(ct);
                DateTime nowUtc = DateTime.UtcNow;

                var pendingItems = items.Where(x => x.Status == OfflineQueueItemStatus.Pending).ToList();
                double? oldestAgeSec = pendingItems.Any()
                    ? pendingItems.Min(x => (nowUtc - x.OccurredAt).TotalSeconds)
                    : null;

                long totalBytes = items.Sum(x => (long)Encoding.UTF8.GetByteCount(x.Payload ?? "{}"));

                return new QueueMetricsDto
                {
                    TotalCount = items.Count,
                    PendingCount = pendingItems.Count,
                    InFlightCount = items.Count(x => x.Status == OfflineQueueItemStatus.InFlight),
                    AcknowledgedCount = items.Count(x => x.Status == OfflineQueueItemStatus.Acknowledged),
                    FailedCount = items.Count(x => x.Status == OfflineQueueItemStatus.Failed),
                    ExpiredCount = items.Count(x => x.Status == OfflineQueueItemStatus.Expired),
                    TotalSizeBytes = totalBytes,
                    CriticalCount = items.Count(x => string.Equals(x.ReliabilityClass, EventReliabilityClass.Critical, StringComparison.OrdinalIgnoreCase)),
                    ImportantCount = items.Count(x => string.Equals(x.ReliabilityClass, EventReliabilityClass.Important, StringComparison.OrdinalIgnoreCase)),
                    NormalCount = items.Count(x => string.Equals(x.ReliabilityClass, EventReliabilityClass.Normal, StringComparison.OrdinalIgnoreCase)),
                    OldestPendingAgeSeconds = oldestAgeSec
                };
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task EnforceCapacityLimitsAsync(string incomingReliabilityClass, int incomingByteSize, CancellationToken ct)
        {
            var activeItems = await _dbContext.QueueItems
                .Where(x => x.Status == OfflineQueueItemStatus.Pending || x.Status == OfflineQueueItemStatus.InFlight)
                .ToListAsync(ct);

            long currentTotalBytes = activeItems.Sum(x => (long)Encoding.UTF8.GetByteCount(x.Payload ?? "{}"));

            bool countOverflow = activeItems.Count >= _options.MaxItemCount;
            bool sizeOverflow = (currentTotalBytes + incomingByteSize) > _options.MaxStorageSizeBytes;

            if (!countOverflow && !sizeOverflow)
            {
                return;
            }

            _logger.LogWarning("Queue capacity threshold reached (Count={Count}/{MaxCount}, Size={Size}/{MaxSize}). Applying overflow eviction policy for incoming {ReliabilityClass} event.",
                activeItems.Count, _options.MaxItemCount, currentTotalBytes, _options.MaxStorageSizeBytes, incomingReliabilityClass);

            var normalCandidates = activeItems
                .Where(x => string.Equals(x.ReliabilityClass, EventReliabilityClass.Normal, StringComparison.OrdinalIgnoreCase) && x.Status == OfflineQueueItemStatus.Pending)
                .OrderBy(x => x.OccurredAt)
                .ToList();

            while ((activeItems.Count >= _options.MaxItemCount || currentTotalBytes + incomingByteSize > _options.MaxStorageSizeBytes) && normalCandidates.Any())
            {
                var evictItem = normalCandidates[0];
                normalCandidates.RemoveAt(0);
                activeItems.Remove(evictItem);
                currentTotalBytes -= Encoding.UTF8.GetByteCount(evictItem.Payload ?? "{}");

                evictItem.Status = OfflineQueueItemStatus.Expired;
                evictItem.ExpiresAt = DateTime.UtcNow;
                evictItem.LastError = "Evicted due to queue capacity overflow policy.";
            }

            if (string.Equals(incomingReliabilityClass, EventReliabilityClass.Critical, StringComparison.OrdinalIgnoreCase))
            {
                var importantCandidates = activeItems
                    .Where(x => string.Equals(x.ReliabilityClass, EventReliabilityClass.Important, StringComparison.OrdinalIgnoreCase) && x.Status == OfflineQueueItemStatus.Pending)
                    .OrderBy(x => x.OccurredAt)
                    .ToList();

                while ((activeItems.Count >= _options.MaxItemCount || currentTotalBytes + incomingByteSize > _options.MaxStorageSizeBytes) && importantCandidates.Any())
                {
                    var evictItem = importantCandidates[0];
                    importantCandidates.RemoveAt(0);
                    activeItems.Remove(evictItem);
                    currentTotalBytes -= Encoding.UTF8.GetByteCount(evictItem.Payload ?? "{}");

                    evictItem.Status = OfflineQueueItemStatus.Expired;
                    evictItem.ExpiresAt = DateTime.UtcNow;
                    evictItem.LastError = "Evicted to preserve CRITICAL event capacity.";
                }
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        private static string ResolveReliabilityClass(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType)) return EventReliabilityClass.NotQueueable;

            return eventType.ToUpperInvariant() switch
            {
                ClientEventType.SessionRuntimeEvent or ClientEventType.SecurityEvent or "SESSION_COMMAND_REQUEST" => EventReliabilityClass.Critical,
                ClientEventType.ClientStarted or ClientEventType.ClientStopped or ClientEventType.ApplicationCrashed or ClientEventType.WorkstationStateChanged or ClientEventType.ConfigurationChanged => EventReliabilityClass.Important,
                ClientEventType.ApplicationStarted or ClientEventType.ApplicationExited or ClientEventType.DeviceChanged or ClientEventType.NetworkChanged or ClientEventType.DiagnosticEvent => EventReliabilityClass.Normal,
                "TELEMETRY" or "HEARTBEAT" => EventReliabilityClass.Ephemeral,
                _ => EventReliabilityClass.NotQueueable
            };
        }

        private static int GetPriorityRank(string reliabilityClass)
        {
            return reliabilityClass?.ToUpperInvariant() switch
            {
                EventReliabilityClass.Critical => 1,
                EventReliabilityClass.Important => 2,
                EventReliabilityClass.Normal => 3,
                _ => 4
            };
        }

        private bool IsItemExpired(DurableQueueItemEntity item, DateTime nowUtc)
        {
            TimeSpan age = nowUtc - item.OccurredAt;
            return item.ReliabilityClass?.ToUpperInvariant() switch
            {
                EventReliabilityClass.Critical => age.TotalDays > _options.CriticalRetentionDays,
                EventReliabilityClass.Important => age.TotalDays > _options.ImportantRetentionDays,
                EventReliabilityClass.Normal => age.TotalHours > _options.NormalRetentionHours,
                _ => false
            };
        }
    }
}
