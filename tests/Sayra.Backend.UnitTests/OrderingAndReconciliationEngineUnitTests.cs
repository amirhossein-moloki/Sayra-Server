using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OrderingAndReconciliationEngineUnitTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        private (OfflineOrderingAndReconciliationEngine Engine, ApplicationDbContext Db) CreateEngine(string dbName, OfflineOrderingOptions? options = null)
        {
            var db = CreateInMemoryDbContext(dbName);

            // Seed workstation & active site
            if (!db.Sites.Any(s => s.SiteId == "SITE-001"))
            {
                var site = new Site { SiteId = "SITE-001", Code = "SITE-001", Name = "Main Site", OrganizationId = Guid.NewGuid(), Status = "Active" };
                db.Sites.Add(site);
            }

            if (!db.Workstations.Any(w => w.PcId == "PC-001"))
            {
                db.Workstations.Add(new Workstation { PcId = "PC-001", SiteId = "SITE-001", Name = "PC-001" });
            }

            db.SaveChanges();

            var processedEventRepo = new ProcessedEventRepository(db);
            var streamStateRepo = new WorkstationStreamStateRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var siteRepo = new Repository<Site>(db);
            var sessionRepo = new Repository<Session>(db);
            var auditRepo = new Repository<AuditEvent>(db);
            var opts = Options.Create(options ?? new OfflineOrderingOptions());

            var engine = new OfflineOrderingAndReconciliationEngine(
                processedEventRepo,
                streamStateRepo,
                workstationRepo,
                siteRepo,
                sessionRepo,
                auditRepo,
                db,
                opts,
                NullLogger<OfflineOrderingAndReconciliationEngine>.Instance);

            return (engine, db);
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_InOrderCriticalEvent_ReturnsReadyForReconciliationAndAdvancesStream()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"action\":\"START\"}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.True(result.IsAcceptedForAck);
                Assert.Equal(OfflineOrderingStatus.InOrder, result.OrderingStatus);
                Assert.Equal(OfflineReconciliationStatus.ReadyForReconciliation, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.SuccessInOrder, result.ReasonCode);

                // Verify stream state updated in DB
                var streamState = await db.WorkstationStreamStates.FirstOrDefaultAsync(s => s.ClientId == "PC-001");
                Assert.NotNull(streamState);
                Assert.Equal(1, streamState.LastSequenceNumber);

                // Verify AuditEvent logged
                var audit = await db.AuditEvents.FirstOrDefaultAsync(a => a.EventId == Guid.Parse(eventId));
                Assert.NotNull(audit);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_SequenceGap_WithWaitPolicy_HoldsEventInWaitingStatus()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName, new OfflineOrderingOptions { GapPolicy = "WAIT" });
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 5, // Expected sequence is 1!
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"action\":\"START\"}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.True(result.IsAcceptedForAck);
                Assert.Equal(OfflineOrderingStatus.GapDetected, result.OrderingStatus);
                Assert.Equal(OfflineReconciliationStatus.WaitingForSequence, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.SequenceGapWait, result.ReasonCode);

                // Stream state sequence must NOT advance past gap
                var streamState = await db.WorkstationStreamStates.FirstOrDefaultAsync(s => s.ClientId == "PC-001");
                Assert.NotNull(streamState);
                Assert.Equal(0, streamState.LastSequenceNumber);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_SequenceGap_WithAcceptWithGapPolicy_AdvancesStream()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName, new OfflineOrderingOptions { GapPolicy = "ACCEPT_WITH_GAP" });
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 5,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"action\":\"START\"}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.True(result.IsAcceptedForAck);
                Assert.Equal(OfflineOrderingStatus.GapDetected, result.OrderingStatus);
                Assert.Equal(OfflineReconciliationStatus.ReadyForReconciliation, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.SuccessGapAccepted, result.ReasonCode);

                // Stream state sequence advances to gap sequence
                var streamState = await db.WorkstationStreamStates.FirstOrDefaultAsync(s => s.ClientId == "PC-001");
                Assert.NotNull(streamState);
                Assert.Equal(5, streamState.LastSequenceNumber);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_GapResolution_WhenMissingSequenceArrives_UnblocksHeldEvents()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName, new OfflineOrderingOptions { GapPolicy = "WAIT" });
            using (db)
            {
                var idSeq2 = Guid.NewGuid().ToString("D");
                var idSeq1 = Guid.NewGuid().ToString("D");

                // 1. Seq 2 arrives first (gap!)
                var itemSeq2 = new OfflineQueueItem
                {
                    EventId = idSeq2,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 2,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"action\":\"EXTEND\"}"
                };
                var resGap = await engine.EvaluateAndReconcileAsync(itemSeq2, "PC-001", "batch-1");
                Assert.Equal(OfflineReconciliationStatus.WaitingForSequence, resGap.ReconciliationStatus);

                // 2. Seq 1 arrives later (in order)
                var itemSeq1 = new OfflineQueueItem
                {
                    EventId = idSeq1,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"action\":\"START\"}"
                };
                var resInOrder = await engine.EvaluateAndReconcileAsync(itemSeq1, "PC-001", "batch-2");
                Assert.Equal(OfflineReconciliationStatus.ReadyForReconciliation, resInOrder.ReconciliationStatus);

                // 3. Verify Seq 2 was automatically unblocked and transitioned to READY_FOR_RECONCILIATION!
                var heldEvent = await db.ProcessedEvents.FirstOrDefaultAsync(e => e.EventId == Guid.Parse(idSeq2));
                Assert.NotNull(heldEvent);
                Assert.Equal(OfflineOrderingStatus.InOrder, heldEvent.OrderingStatus);
                Assert.Equal(OfflineReconciliationStatus.ReadyForReconciliation, heldEvent.ProcessingStatus);

                // Stream sequence advances to 2!
                var streamState = await db.WorkstationStreamStates.FirstOrDefaultAsync(s => s.ClientId == "PC-001");
                Assert.NotNull(streamState);
                Assert.Equal(2, streamState.LastSequenceNumber);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_UnorderedNormalEvent_ReturnsUnorderedReadyForReconciliation()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "APPLICATION_STARTED",
                    SequenceNumber = 0,
                    ReliabilityClass = EventReliabilityClass.Normal,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"processName\":\"game.exe\"}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.True(result.IsAcceptedForAck);
                Assert.Equal(OfflineOrderingStatus.Unordered, result.OrderingStatus);
                Assert.Equal(OfflineReconciliationStatus.ReadyForReconciliation, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.SuccessUnordered, result.ReasonCode);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_IdentityMismatch_RejectsEvent()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SECURITY_EVENT",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-999\"}" // Payload identity differs from auth identity PC-001!
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Rejected, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, result.ReasonCode);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_FutureTimestampClockSkew_ReturnsInvalid()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName, new OfflineOrderingOptions { MaxFutureClockSkewMinutes = 5 });
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow.AddMinutes(10), // 10 minutes in future!
                    Payload = "{}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Invalid, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.FutureTimestampClockSkew, result.ReasonCode);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_ExpiredRetentionThreshold_ReturnsExpired()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "APPLICATION_STARTED",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Normal, // Normal retention is 1 day
                    Timestamp = DateTime.UtcNow.AddDays(-2), // 2 days old!
                    Payload = "{}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Expired, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.ExpiredRetention, result.ReasonCode);
            }
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_InvalidSessionReference_ReturnsConflict()
        {
            var dbName = Guid.NewGuid().ToString();
            var (engine, db) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var nonExistentSessionId = Guid.NewGuid().ToString();

                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = $"{{\"sessionId\":\"{nonExistentSessionId}\"}}"
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Conflict, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.InvalidSessionReference, result.ReasonCode);
            }
        }
    }
}
