using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineBatchServerHandlerUnitTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task HandleAsync_IdentityMismatch_RejectsWithSecurityViolation()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-999",
                WorkstationId = "PC-999",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = Guid.NewGuid().ToString("D"), EventType = "TEST", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            var ack = result.Value.Acknowledgment;
            Assert.False(ack.Success);
            Assert.Contains("SECURITY_VIOLATION", ack.ErrorMessage);
        }

        [Fact]
        public async Task HandleAsync_ValidBatch_AcceptsItemsAndReturnsAcknowledgedIds()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var id1 = Guid.NewGuid().ToString("D");
            var id2 = Guid.NewGuid().ToString("D");

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = id1, EventType = "SESSION_COMMAND_REQUEST", Payload = "{}" },
                    new OfflineQueueItem { EventId = id2, EventType = "SECURITY_EVENT", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            var ack = result.Value.Acknowledgment;
            Assert.True(ack.Success);
            Assert.Equal(2, ack.ProcessedCount);
            Assert.Contains(id1, ack.AcknowledgedEventIds);
            Assert.Contains(id2, ack.AcknowledgedEventIds);
            Assert.Empty(ack.RejectedEventIds);

            // Verify persistence in DB
            var stored1 = await db.ProcessedEvents.FirstOrDefaultAsync(e => e.EventId == Guid.Parse(id1));
            Assert.NotNull(stored1);
            Assert.Equal("ACCEPTED", stored1.ProcessingStatus);
        }

        [Fact]
        public async Task HandleAsync_DuplicateBatch_DeduplicatesAndAcknowledges()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var eventId = Guid.NewGuid().ToString("D");
            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = eventId, EventType = "SESSION_COMMAND_REQUEST", Payload = "{\"action\":\"START\"}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);

            // First submission
            var res1 = await handler.HandleAsync(command);
            Assert.True(res1.Value.Acknowledgment.Success);
            Assert.Contains(eventId, res1.Value.Acknowledgment.AcknowledgedEventIds);

            // Second duplicate submission
            var res2 = await handler.HandleAsync(command);
            Assert.True(res2.Value.Acknowledgment.Success);
            Assert.Contains(eventId, res2.Value.Acknowledgment.AcknowledgedEventIds);

            // Verify status in DB changed to DUPLICATE safely without failing
            var stored = await db.ProcessedEvents.FirstOrDefaultAsync(e => e.EventId == Guid.Parse(eventId));
            Assert.NotNull(stored);
            Assert.Equal("DUPLICATE", stored.ProcessingStatus);
        }

        [Fact]
        public async Task HandleAsync_SameEventId_DifferentPayload_RejectsWithConflict()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var eventId = Guid.NewGuid().ToString("D");
            var req1 = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = eventId, EventType = "SESSION_COMMAND_REQUEST", Payload = "{\"action\":\"START\"}" }
                }
            };

            var req2 = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = eventId, EventType = "SESSION_COMMAND_REQUEST", Payload = "{\"action\":\"STOP\"}" }
                }
            };

            // First submission
            await handler.HandleAsync(new IngestOfflineBatchCommand("conn-1", "PC-001", req1));

            // Tampered retransmission
            var res2 = await handler.HandleAsync(new IngestOfflineBatchCommand("conn-1", "PC-001", req2));
            var ack2 = res2.Value.Acknowledgment;

            Assert.True(ack2.Success);
            Assert.Contains(eventId, ack2.RejectedEventIds);

            var stored = await db.ProcessedEvents.FirstOrDefaultAsync(e => e.EventId == Guid.Parse(eventId));
            Assert.NotNull(stored);
            Assert.Equal("CONFLICT", stored.ProcessingStatus);
        }

        [Fact]
        public async Task HandleAsync_InvalidEventId_RejectsIndividualEvent()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var validId = Guid.NewGuid().ToString("D");
            var invalidId = "not-a-valid-guid";

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = validId, EventType = "SESSION_COMMAND_REQUEST", Payload = "{}" },
                    new OfflineQueueItem { EventId = invalidId, EventType = "SECURITY_EVENT", Payload = "{}" }
                }
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            var ack = result.Value.Acknowledgment;
            Assert.True(ack.Success);
            Assert.Equal(1, ack.ProcessedCount);
            Assert.Contains(validId, ack.AcknowledgedEventIds);
            Assert.Contains(invalidId, ack.RejectedEventIds);
        }

        [Fact]
        public async Task HandleAsync_OversizedBatch_RejectsBatch()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var items = new List<OfflineQueueItem>();
            for (int i = 0; i < 101; i++)
            {
                items.Add(new OfflineQueueItem { EventId = Guid.NewGuid().ToString("D"), EventType = "TEST", Payload = "{}" });
            }

            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = items
            };

            var command = new IngestOfflineBatchCommand("conn-1", "PC-001", req);
            var result = await handler.HandleAsync(command);

            Assert.True(result.IsSuccess);
            var ack = result.Value.Acknowledgment;
            Assert.False(ack.Success);
            Assert.Contains("exceeds maximum limit", ack.ErrorMessage);
        }

        [Fact]
        public async Task HandleAsync_ConcurrentDuplicateSubmissions_AtMostOneAccepted()
        {
            using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var processedEventRepo = new ProcessedEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var handler = new IngestOfflineBatchCommandHandler(
                processedEventRepo,
                workstationRepo,
                db,
                NullLogger<IngestOfflineBatchCommandHandler>.Instance);

            var sharedEventId = Guid.NewGuid().ToString("D");
            var req = new OfflineBatchRequest
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ClientId = "PC-001",
                WorkstationId = "WS-001",
                Items = new List<OfflineQueueItem>
                {
                    new OfflineQueueItem { EventId = sharedEventId, EventType = "SECURITY_EVENT", Payload = "{\"violation\":\"TAMPER\"}" }
                }
            };

            var tasks = new List<Task<Shared.Result<IngestOfflineBatchResult>>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(() => handler.HandleAsync(new IngestOfflineBatchCommand($"conn-{i}", "PC-001", req))));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var res in results)
            {
                Assert.True(res.IsSuccess);
                Assert.True(res.Value.Acknowledgment.Success);
                Assert.Contains(sharedEventId, res.Value.Acknowledgment.AcknowledgedEventIds);
            }

            // Exactly 1 record persisted for that EventId
            var count = await db.ProcessedEvents.CountAsync(e => e.EventId == Guid.Parse(sharedEventId));
            Assert.Equal(1, count);
        }
    }
}
