using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.OfflineQueue;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.UnitTests.Fixtures;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class DurableOfflineQueueUnitTests
    {
        private SqliteOfflineQueueDbContext CreateTestDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<SqliteOfflineQueueDbContext>()
                .UseSqlite($"Data Source={dbName}")
                .Options;

            var context = new SqliteOfflineQueueDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task Enqueue_ValidQueueableEvent_SucceedsAndPersistsInSqlite()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var envelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
                envelope.EventType = ClientEventType.SessionRuntimeEvent;

                var result = await queue.EnqueueAsync(envelope);

                Assert.True(result.IsQueued);
                Assert.Equal("Success", result.Reason);
                Assert.NotNull(result.Item);
                Assert.Equal(envelope.EventId, result.Item.EventId);
                Assert.Equal(EventReliabilityClass.Critical, result.Item.ReliabilityClass);
                Assert.Equal(OfflineQueueItemStatus.Pending, result.Item.Status);

                var persisted = await context.QueueItems.FirstOrDefaultAsync(x => x.EventId == envelope.EventId);
                Assert.NotNull(persisted);
                Assert.Equal(envelope.Payload, persisted.Payload);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Theory]
        [InlineData("TELEMETRY")]
        [InlineData("HEARTBEAT")]
        [InlineData("PONG")]
        public async Task Enqueue_NonQueueableEvent_IsRejected(string eventType)
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var envelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
                envelope.EventType = eventType;

                var result = await queue.EnqueueAsync(envelope);

                Assert.False(result.IsQueued);
                Assert.Equal("NonQueueable", result.Reason);
                Assert.Null(result.Item);

                int count = await context.QueueItems.CountAsync();
                Assert.Equal(0, count);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task Enqueue_DuplicateEventId_IsRejected_AndNotDuplicatedInDatabase()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var envelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
                envelope.EventType = ClientEventType.ClientStarted;

                var result1 = await queue.EnqueueAsync(envelope);
                Assert.True(result1.IsQueued);

                var result2 = await queue.EnqueueAsync(envelope);
                Assert.False(result2.IsQueued);
                Assert.Equal("DuplicateEventId", result2.Reason);

                int count = await context.QueueItems.CountAsync(x => x.EventId == envelope.EventId);
                Assert.Equal(1, count);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task Enqueue_OversizedPayload_IsRejected()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var options = new OfflineQueueOptions { MaxPayloadSizeBytes = 100 };
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(options), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var envelope = OfflineContractFixtures.CreateValidClientEventEnvelope();
                envelope.EventType = ClientEventType.DiagnosticEvent;
                envelope.Payload = new string('A', 500);

                var result = await queue.EnqueueAsync(envelope);

                Assert.False(result.IsQueued);
                Assert.Equal("OversizedPayload", result.Reason);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task ClaimBatch_ReturnsItemsInPriorityAndSequenceOrder_AndTransitionsToInFlight()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var normal = new ClientEventEnvelopeDto
                {
                    EventId = "normal-1",
                    EventType = ClientEventType.ApplicationStarted,
                    SequenceNumber = 10,
                    Payload = "{}"
                };

                var critical = new ClientEventEnvelopeDto
                {
                    EventId = "critical-1",
                    EventType = ClientEventType.SecurityEvent,
                    SequenceNumber = 5,
                    Payload = "{}"
                };

                var important = new ClientEventEnvelopeDto
                {
                    EventId = "important-1",
                    EventType = ClientEventType.ClientStarted,
                    SequenceNumber = 1,
                    Payload = "{}"
                };

                await queue.EnqueueAsync(normal);
                await queue.EnqueueAsync(critical);
                await queue.EnqueueAsync(important);

                var claimed = await queue.ClaimBatchAsync(10);

                Assert.Equal(3, claimed.Count);
                Assert.Equal("critical-1", claimed[0].EventId);
                Assert.Equal("important-1", claimed[1].EventId);
                Assert.Equal("normal-1", claimed[2].EventId);

                Assert.All(claimed, item => Assert.Equal(OfflineQueueItemStatus.InFlight, item.Status));
                Assert.All(claimed, item => Assert.NotNull(item.LastAttemptAt));
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task AcknowledgeItems_TransitionsStatusToAcknowledged()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var env = OfflineContractFixtures.CreateValidClientEventEnvelope();
                await queue.EnqueueAsync(env);
                await queue.ClaimBatchAsync(10);

                await queue.AcknowledgeItemsAsync(new[] { env.EventId });

                var item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(OfflineQueueItemStatus.Acknowledged, item.Status);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task MarkFailed_UpdatesRetryMetadataAndStatus()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var env = OfflineContractFixtures.CreateValidClientEventEnvelope();
                await queue.EnqueueAsync(env);

                await queue.MarkFailedAsync(env.EventId, "Transient network timeout", isPermanent: false);

                var item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(1, item.RetryCount);
                Assert.Equal("Transient network timeout", item.LastError);
                Assert.Equal(OfflineQueueItemStatus.Pending, item.Status);

                await queue.MarkFailedAsync(env.EventId, "Fatal payload schema error", isPermanent: true);

                item = await context.QueueItems.FirstAsync(x => x.EventId == env.EventId);
                Assert.Equal(2, item.RetryCount);
                Assert.Equal("Fatal payload schema error", item.LastError);
                Assert.Equal(OfflineQueueItemStatus.Failed, item.Status);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task StartupRecovery_ResetsInFlightItemsBackToPending()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using (var context = CreateTestDbContext(dbName))
                {
                    var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                    var env = OfflineContractFixtures.CreateValidClientEventEnvelope();
                    await queue.EnqueueAsync(env);
                    await queue.ClaimBatchAsync(10); // Transitions to IN_FLIGHT
                }

                // Simulate app restart by creating a new DbContext instance on same db file
                using (var restartedContext = CreateTestDbContext(dbName))
                {
                    var queue = new SqliteDurableOfflineQueue(restartedContext, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                    int recoveredCount = await queue.StartupRecoveryAsync();
                    Assert.Equal(1, recoveredCount);

                    var item = await restartedContext.QueueItems.FirstAsync();
                    Assert.Equal(OfflineQueueItemStatus.Pending, item.Status);
                }
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task PersistenceAndRestartRecovery_StoreReopened_EventDataIntactAndIdentical()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                var original = OfflineContractFixtures.CreateValidClientEventEnvelope();
                original.EventType = ClientEventType.SecurityEvent;

                using (var context = CreateTestDbContext(dbName))
                {
                    var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);
                    await queue.EnqueueAsync(original);
                }

                using (var restartedContext = CreateTestDbContext(dbName))
                {
                    var item = await restartedContext.QueueItems.FirstOrDefaultAsync(x => x.EventId == original.EventId);

                    Assert.NotNull(item);
                    Assert.Equal(original.EventId, item.EventId);
                    Assert.Equal(original.EventType, item.EventType);
                    Assert.Equal(original.ClientId, item.ClientId);
                    Assert.Equal(original.WorkstationId, item.WorkstationId);
                    Assert.Equal(original.SequenceNumber, item.SequenceNumber);
                    Assert.Equal(original.Payload, item.Payload);
                    Assert.Equal(EventReliabilityClass.Critical, item.ReliabilityClass);
                }
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task CapacityOverflowPolicy_EvictsNormalAndImportantToProtectCritical()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var options = new OfflineQueueOptions { MaxItemCount = 2 };
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(options), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var normal1 = new ClientEventEnvelopeDto { EventId = "n1", EventType = ClientEventType.ApplicationStarted, OccurredAt = DateTime.UtcNow.AddMinutes(-10), Payload = "{}" };
                var normal2 = new ClientEventEnvelopeDto { EventId = "n2", EventType = ClientEventType.DeviceChanged, OccurredAt = DateTime.UtcNow.AddMinutes(-5), Payload = "{}" };
                var critical = new ClientEventEnvelopeDto { EventId = "c1", EventType = ClientEventType.SecurityEvent, OccurredAt = DateTime.UtcNow, Payload = "{}" };

                await queue.EnqueueAsync(normal1);
                await queue.EnqueueAsync(normal2);

                // Queue is now at capacity = 2 items (normal1, normal2)
                // Enqueuing critical event should evict normal1 (oldest normal)
                var result = await queue.EnqueueAsync(critical);

                Assert.True(result.IsQueued);

                var n1Item = await context.QueueItems.FirstAsync(x => x.EventId == "n1");
                Assert.Equal(OfflineQueueItemStatus.Expired, n1Item.Status);

                var c1Item = await context.QueueItems.FirstAsync(x => x.EventId == "c1");
                Assert.Equal(OfflineQueueItemStatus.Pending, c1Item.Status);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task GetQueueMetrics_AccuratelyReportsQueueDepthAndClassBreakdown()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                await queue.EnqueueAsync(new ClientEventEnvelopeDto { EventId = "e1", EventType = ClientEventType.SecurityEvent, Payload = "{}" });
                await queue.EnqueueAsync(new ClientEventEnvelopeDto { EventId = "e2", EventType = ClientEventType.ClientStarted, Payload = "{}" });
                await queue.EnqueueAsync(new ClientEventEnvelopeDto { EventId = "e3", EventType = ClientEventType.ApplicationStarted, Payload = "{}" });

                var metrics = await queue.GetQueueMetricsAsync();

                Assert.Equal(3, metrics.TotalCount);
                Assert.Equal(3, metrics.PendingCount);
                Assert.Equal(1, metrics.CriticalCount);
                Assert.Equal(1, metrics.ImportantCount);
                Assert.Equal(1, metrics.NormalCount);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }

        [Fact]
        public async Task ConcurrentEnqueueAndClaim_IsThreadSafeAndDeterministic()
        {
            string dbName = $"test_queue_{Guid.NewGuid()}.db";
            try
            {
                using var context = CreateTestDbContext(dbName);
                var queue = new SqliteDurableOfflineQueue(context, Options.Create(new OfflineQueueOptions()), NullLogger<SqliteDurableOfflineQueue>.Instance);

                var tasks = Enumerable.Range(1, 20).Select(i => Task.Run(async () =>
                {
                    var env = new ClientEventEnvelopeDto
                    {
                        EventId = $"concurrent-{i}",
                        EventType = ClientEventType.SecurityEvent,
                        SequenceNumber = i,
                        Payload = "{}"
                    };
                    return await queue.EnqueueAsync(env);
                })).ToArray();

                var results = await Task.WhenAll(tasks);

                Assert.All(results, r => Assert.True(r.IsQueued));

                var metrics = await queue.GetQueueMetricsAsync();
                Assert.Equal(20, metrics.PendingCount);

                var claimed = await queue.ClaimBatchAsync(10);
                Assert.Equal(10, claimed.Count);
            }
            finally
            {
                if (File.Exists(dbName)) File.Delete(dbName);
            }
        }
    }
}
