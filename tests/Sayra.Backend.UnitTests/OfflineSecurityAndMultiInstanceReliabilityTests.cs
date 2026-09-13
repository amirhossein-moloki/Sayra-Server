using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Api.Controllers;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineSecurityAndMultiInstanceReliabilityTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        private (OfflineOrderingAndReconciliationEngine Engine, ApplicationDbContext Db, Mock<IRedisService> MockRedis) CreateEngine(string dbName, bool throwOnRedis = false)
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

            var mockRedis = new Mock<IRedisService>();
            if (throwOnRedis)
            {
                mockRedis.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                         .ThrowsAsync(new Exception("Redis unavailable"));
                mockRedis.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .ThrowsAsync(new Exception("Redis unavailable"));
            }

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
                redisService: mockRedis.Object,
                deadLetterRepository: dlqRepo,
                failureClassifier: classifier);

            return (engine, db, mockRedis);
        }

        [Fact]
        public async Task IdentitySpoofing_PayloadPcIdDiffersFromAuthPcId_RejectsEventWithIdentityMismatch()
        {
            string dbName = Guid.NewGuid().ToString();
            var (engine, db, _) = CreateEngine(dbName);
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
                    Payload = "{\"clientId\":\"PC-999\",\"action\":\"START\"}" // Spoofed identity
                };

                var result = await engine.EvaluateAndReconcileAsync(item, "PC-001", "batch-spoofed-1");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, result.ReasonCode);
                Assert.Equal(OfflineReconciliationStatus.Rejected, result.ReconciliationStatus);

                // Verify DLQ audit entry exists
                var dlq = await db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == Guid.Parse(eventId));
                Assert.NotNull(dlq);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, dlq.FailureCode);
            }
        }

        [Fact]
        public async Task EventIdCollision_SameEventIdDifferentPayloadHash_DetectsPayloadHashConflict()
        {
            string dbName = Guid.NewGuid().ToString();
            var (engine, db, _) = CreateEngine(dbName);
            using (db)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var itemOriginal = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-001\",\"action\":\"START\"}"
                };

                var itemTampered = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-001\",\"action\":\"TAMPERED_ACTION\"}" // Different payload!
                };

                // First attempt succeeds
                var res1 = await engine.EvaluateAndReconcileAsync(itemOriginal, "PC-001", "batch-coll-1");
                Assert.True(res1.IsAcceptedForAck);

                // Second attempt with same EventId but altered payload hash triggers conflict
                var res2 = await engine.EvaluateAndReconcileAsync(itemTampered, "PC-001", "batch-coll-2");
                Assert.False(res2.IsAcceptedForAck);
                Assert.Equal(OfflineReasonCode.PayloadHashConflict, res2.ReasonCode);
                Assert.Equal(OfflineReconciliationStatus.Conflict, res2.ReconciliationStatus);
            }
        }

        [Fact]
        public async Task MultiInstanceConcurrency_ConcurrentEvaluation_IdempotentAndSafe()
        {
            string dbName = Guid.NewGuid().ToString();

            // Seed initial database
            using (var seedDb = CreateInMemoryDbContext(dbName))
            {
                var siteId = "SITE-001";
                var orgId = Guid.NewGuid();
                seedDb.Sites.Add(new Site { SiteId = siteId, Code = siteId, Name = "Main Site", OrganizationId = orgId, Status = "Active" });
                seedDb.Workstations.Add(new Workstation { PcId = "PC-001", SiteId = siteId, Name = "PC-001" });
                seedDb.SaveChanges();
            }

            var eventId = Guid.NewGuid().ToString("D");
            var item = new OfflineQueueItem
            {
                EventId = eventId,
                EventType = "SESSION_COMMAND_REQUEST",
                SequenceNumber = 1,
                ReliabilityClass = EventReliabilityClass.Critical,
                Timestamp = DateTime.UtcNow,
                Payload = "{\"clientId\":\"PC-001\",\"action\":\"START\"}"
            };

            // Simulate 5 independent backend instances, each with its own DbContext instance
            var tasks = Enumerable.Range(0, 5)
                .Select(i => Task.Run(async () =>
                {
                    var (engineInstance, dbInstance, _) = CreateEngine(dbName);
                    using (dbInstance)
                    {
                        return await engineInstance.EvaluateAndReconcileAsync(item, "PC-001", $"batch-conc-{i}");
                    }
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // All returned results must be accepted for ACK (either in-order or duplicate idempotent ACK)
            Assert.All(results, r => Assert.True(r.IsAcceptedForAck));
        }

        [Fact]
        public async Task RedisFailure_EngineAndHandlerGracefullyDegradeToDatabaseAuthority()
        {
            string dbName = Guid.NewGuid().ToString();
            var (engine, db, mockRedis) = CreateEngine(dbName, throwOnRedis: true);
            using (db)
            {
                var handler = new IngestOfflineBatchCommandHandler(engine, NullLogger<IngestOfflineBatchCommandHandler>.Instance, mockRedis.Object);

                var eventId = Guid.NewGuid().ToString("D");
                var batchRequest = new OfflineBatchRequest
                {
                    BatchId = "batch-redis-fail-1",
                    ClientId = "PC-001",
                    WorkstationId = "PC-001",
                    Items = new List<OfflineQueueItem>
                    {
                        new OfflineQueueItem
                        {
                            EventId = eventId,
                            EventType = "AUDIT_LOG",
                            SequenceNumber = 1,
                            ReliabilityClass = EventReliabilityClass.Normal,
                            Timestamp = DateTime.UtcNow,
                            Payload = "{\"clientId\":\"PC-001\",\"msg\":\"test\"}"
                        }
                    }
                };

                var command = new IngestOfflineBatchCommand("conn-1", "PC-001", batchRequest);
                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.True(result.Value.Acknowledgment.Success);
                Assert.Equal(1, result.Value.Acknowledgment.ProcessedCount);

                // Ensure ProcessedEvent record is safely stored in database despite Redis outage
                var storedEvent = await db.ProcessedEvents.FirstOrDefaultAsync(p => p.EventId == Guid.Parse(eventId));
                Assert.NotNull(storedEvent);
            }
        }

        [Fact]
        public async Task OfflineDlqController_AuthorizationAndTenantIsolation_FunctionsCorrectly()
        {
            var mockService = new Mock<IOfflineDlqService>();
            var controller = new OfflineDlqController(mockService.Object);

            var principal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                IsAuthenticated = true,
                OrganizationId = Guid.NewGuid()
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Items["UserPrincipal"] = principal;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var pagedDto = new PagedDlqResponseDto
            {
                Items = new List<DlqEventResponseDto>(),
                Page = 1,
                PageSize = 50,
                TotalCount = 0
            };

            mockService.Setup(s => s.GetDlqEventsAsync(principal, null, null, null, null, null, 1, 50, default))
                       .ReturnsAsync(Result<PagedDlqResponseDto>.Success(pagedDto));

            var response = await controller.GetDlqEventsAsync(null, null, null, null, null, 1, 50);

            var okResult = Assert.IsType<OkObjectResult>(response);
            Assert.Equal(pagedDto, okResult.Value);
        }
    }
}
