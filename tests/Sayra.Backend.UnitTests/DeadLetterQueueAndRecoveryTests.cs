using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class DeadLetterQueueAndRecoveryTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        private (OfflineDlqService Service, OfflineOrderingAndReconciliationEngine Engine, ApplicationDbContext Db) CreateDlqService(string dbName)
        {
            var db = CreateInMemoryDbContext(dbName);

            var siteId = "SITE-001";
            var orgId = Guid.NewGuid();

            if (!db.Sites.Any(s => s.SiteId == siteId))
            {
                db.Sites.Add(new Site { SiteId = siteId, Code = siteId, Name = "Main Site", OrganizationId = orgId, Status = "Active" });
            }

            if (!db.Workstations.Any(w => w.PcId == "PC-001"))
            {
                db.Workstations.Add(new Workstation { PcId = "PC-001", SiteId = siteId, Name = "PC-001" });
            }

            db.SaveChanges();

            var processedEventRepo = new ProcessedEventRepository(db);
            var streamStateRepo = new WorkstationStreamStateRepository(db);
            var dlqRepo = new DeadLetterEventRepository(db);
            var workstationRepo = new Repository<Workstation>(db);
            var siteRepo = new Repository<Site>(db);
            var sessionRepo = new Repository<Session>(db);
            var auditRepo = new Repository<AuditEvent>(db);

            var options = Options.Create(new OfflineOrderingOptions());
            var classifier = new FailureClassificationService();

            var engine = new OfflineOrderingAndReconciliationEngine(
                processedEventRepo,
                streamStateRepo,
                workstationRepo,
                siteRepo,
                sessionRepo,
                auditRepo,
                db,
                options,
                NullLogger<OfflineOrderingAndReconciliationEngine>.Instance,
                redisService: null,
                deadLetterRepository: dlqRepo,
                failureClassifier: classifier);

            var service = new OfflineDlqService(
                dlqRepo,
                engine,
                auditRepo,
                db,
                NullLogger<OfflineDlqService>.Instance);

            return (service, engine, db);
        }

        [Fact]
        public async Task EvaluateAndReconcileAsync_PermanentFailure_RecordsDeadLetterEvent()
        {
            string dbName = Guid.NewGuid().ToString();
            var (service, engine, db) = CreateDlqService(dbName);
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
                    Payload = "{\"clientId\":\"PC-999\"}" // Identity mismatch failure!
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-dlq-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Rejected, result.ReconciliationStatus);

                // Verify DeadLetterEvent persisted in DB
                var dlq = await db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == Guid.Parse(eventId));
                Assert.NotNull(dlq);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, dlq.FailureCode);
                Assert.Equal(DeadLetterStatus.DeadLetter, dlq.ProcessingStatus);
            }
        }

        [Fact]
        public async Task GetDlqEventsAsync_WithTenantIsolation_FiltersCorrectly()
        {
            string dbName = Guid.NewGuid().ToString();
            var (service, engine, db) = CreateDlqService(dbName);
            using (db)
            {
                var orgId1 = Guid.NewGuid();
                var orgId2 = Guid.NewGuid();

                db.DeadLetterEvents.Add(new DeadLetterEvent
                {
                    EventId = Guid.NewGuid(),
                    BatchId = "b1",
                    ClientId = "PC-001",
                    EventType = "TEST",
                    OrganizationId = orgId1,
                    FailureCode = "IDENTITY_MISMATCH",
                    FailureReason = "Identity mismatch"
                });

                db.DeadLetterEvents.Add(new DeadLetterEvent
                {
                    EventId = Guid.NewGuid(),
                    BatchId = "b2",
                    ClientId = "PC-002",
                    EventType = "TEST",
                    OrganizationId = orgId2,
                    FailureCode = "IDENTITY_MISMATCH",
                    FailureReason = "Identity mismatch"
                });

                await db.SaveChangesAsync();

                var principalOrg1 = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    IsAuthenticated = true,
                    OrganizationId = orgId1
                };

                var result = await service.GetDlqEventsAsync(principalOrg1);

                Assert.True(result.IsSuccess);
                Assert.Equal(1, result.Value.TotalCount);
                Assert.Equal("PC-001", result.Value.Items[0].ClientId);
            }
        }

        [Fact]
        public async Task RetryDlqEventAsync_ValidEvent_ReEntersPipelineAndMarksRecovered()
        {
            string dbName = Guid.NewGuid().ToString();
            var (service, engine, db) = CreateDlqService(dbName);
            using (db)
            {
                var eventGuid = Guid.NewGuid();
                var dlq = new DeadLetterEvent
                {
                    EventId = eventGuid,
                    BatchId = "batch-retry-1",
                    ClientId = "PC-001",
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Payload = "{\"action\":\"START\"}",
                    FailureCode = "TRANSIENT_FAILURE",
                    FailureReason = "Transient socket reset",
                    ProcessingStatus = DeadLetterStatus.DeadLetter
                };

                db.DeadLetterEvents.Add(dlq);
                await db.SaveChangesAsync();

                var adminPrincipal = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    IsAuthenticated = true
                };

                var retryResult = await service.RetryDlqEventAsync(adminPrincipal, eventGuid);

                Assert.True(retryResult.IsSuccess);
                Assert.True(retryResult.Value.IsAcceptedForAck);
                Assert.Equal(OfflineOrderingStatus.InOrder, retryResult.Value.OrderingStatus);

                var updatedDlq = await db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == eventGuid);
                Assert.NotNull(updatedDlq);
                Assert.Equal(DeadLetterStatus.Recovered, updatedDlq.ProcessingStatus);
                Assert.NotNull(updatedDlq.RecoveredAt);
            }
        }

        [Fact]
        public async Task RejectDlqEventAsync_MarksStatusAsRejected()
        {
            string dbName = Guid.NewGuid().ToString();
            var (service, engine, db) = CreateDlqService(dbName);
            using (db)
            {
                var eventGuid = Guid.NewGuid();
                var dlq = new DeadLetterEvent
                {
                    EventId = eventGuid,
                    BatchId = "batch-reject-1",
                    ClientId = "PC-001",
                    EventType = "MALFORMED",
                    FailureCode = "MALFORMED_PAYLOAD",
                    FailureReason = "Unparseable JSON",
                    ProcessingStatus = DeadLetterStatus.DeadLetter
                };

                db.DeadLetterEvents.Add(dlq);
                await db.SaveChangesAsync();

                var adminPrincipal = new UserPrincipal
                {
                    UserId = Guid.NewGuid(),
                    IsAuthenticated = true
                };

                var rejectResult = await service.RejectDlqEventAsync(adminPrincipal, eventGuid, "Manually dismissed by operator");

                Assert.True(rejectResult.IsSuccess);
                Assert.Equal(DeadLetterStatus.Rejected, rejectResult.Value.ProcessingStatus);

                var updatedDlq = await db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == eventGuid);
                Assert.NotNull(updatedDlq);
                Assert.Equal(DeadLetterStatus.Rejected, updatedDlq.ProcessingStatus);
                Assert.Equal("Manually dismissed by operator", updatedDlq.FailureReason);
            }
        }
    }
}
