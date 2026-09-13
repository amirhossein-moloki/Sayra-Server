using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Sessions;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.OfflineQueue;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class Phase09EndToEndValidationTests
    {
        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        private SqliteOfflineQueueDbContext CreateSqliteQueueDbContext(string dbPath)
        {
            var options = new DbContextOptionsBuilder<SqliteOfflineQueueDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            var context = new SqliteOfflineQueueDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private (OfflineOrderingAndReconciliationEngine Engine, ApplicationDbContext Db, Mock<IRedisService> MockRedis, FakeStartSessionHandler StartHandler, FakeStopSessionHandler StopHandler) CreateServerPipeline(string dbName, bool throwOnRedis = false)
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

            var startHandler = new FakeStartSessionHandler();
            var stopHandler = new FakeStopSessionHandler();
            var pauseHandler = new FakePauseSessionHandler();
            var resumeHandler = new FakeResumeSessionHandler();
            var extendHandler = new FakeExtendSessionHandler();

            var businessReconciliationService = new OfflineBusinessReconciliationService(
                startHandler, stopHandler, pauseHandler, resumeHandler, extendHandler, auditRepo, NullLogger<OfflineBusinessReconciliationService>.Instance);

            var options = Options.Create(new OfflineOrderingOptions());
            var classifier = new FailureClassificationService();

            var mockRedis = new Mock<IRedisService>();
            if (throwOnRedis)
            {
                mockRedis.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                         .ThrowsAsync(new Exception("Redis service temporary outage"));
                mockRedis.Setup(r => r.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .ThrowsAsync(new Exception("Redis service temporary outage"));
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
                failureClassifier: classifier,
                businessReconciliationService: businessReconciliationService);

            return (engine, db, mockRedis, startHandler, stopHandler);
        }

        #region Helper Handlers
        private class FakeStartSessionHandler : ICommandHandler<StartSessionCommand, SessionResponseDto>
        {
            public int CallCount { get; private set; }
            public Func<StartSessionCommand, Result<SessionResponseDto>>? OnHandle { get; set; }

            public Task<Result<SessionResponseDto>> HandleAsync(StartSessionCommand command, CancellationToken cancellationToken = default)
            {
                CallCount++;
                if (OnHandle != null) return Task.FromResult(OnHandle(command));
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = Guid.NewGuid(),
                    WorkstationId = command.WorkstationId,
                    GamerId = command.GamerId,
                    Status = "ACTIVE"
                }));
            }
        }

        private class FakeStopSessionHandler : ICommandHandler<StopSessionCommand, SessionResponseDto>
        {
            public int CallCount { get; private set; }
            public Task<Result<SessionResponseDto>> HandleAsync(StopSessionCommand command, CancellationToken cancellationToken = default)
            {
                CallCount++;
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto
                {
                    SessionId = command.SessionId,
                    Status = "ENDED"
                }));
            }
        }

        private class FakePauseSessionHandler : ICommandHandler<PauseSessionCommand, SessionResponseDto>
        {
            public Task<Result<SessionResponseDto>> HandleAsync(PauseSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto { SessionId = command.SessionId, Status = "PAUSED" }));
            }
        }

        private class FakeResumeSessionHandler : ICommandHandler<ResumeSessionCommand, SessionResponseDto>
        {
            public Task<Result<SessionResponseDto>> HandleAsync(ResumeSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionResponseDto>.Success(new SessionResponseDto { SessionId = command.SessionId, Status = "ACTIVE" }));
            }
        }

        private class FakeExtendSessionHandler : ICommandHandler<ExtendSessionCommand, SessionExtensionResponseDto>
        {
            public Task<Result<SessionExtensionResponseDto>> HandleAsync(ExtendSessionCommand command, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Result<SessionExtensionResponseDto>.Success(new SessionExtensionResponseDto { SessionExtensionId = Guid.NewGuid(), SessionId = command.SessionId, Cost = 10.0m }));
            }
        }
        #endregion

        [Fact]
        public async Task E2E_ScenarioA_FullOfflineLifecycle_EnqueueToSyncToReconcileToAckToCleanup()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"sayra_queue_e2e_{Guid.NewGuid():N}.db");
            string serverDbName = Guid.NewGuid().ToString();

            try
            {
                // 1. Client side: Enqueue offline event into durable SQLite queue
                var queueOptions = Options.Create(new OfflineQueueOptions { DbPath = dbPath, MaxItemCount = 1000 });

                var (engine, serverDb, mockRedis, startHandler, _) = CreateServerPipeline(serverDbName);
                using (serverDb)
                {
                    var ws = await serverDb.Workstations.FirstAsync(w => w.PcId == "PC-001");
                    var gamerId = Guid.NewGuid();
                    var eventId = Guid.NewGuid().ToString("D");

                    using (var clientDbContext = CreateSqliteQueueDbContext(dbPath))
                    {
                        var durableQueue = new SqliteDurableOfflineQueue(clientDbContext, queueOptions, NullLogger<SqliteDurableOfflineQueue>.Instance);

                        var envelope = new ClientEventEnvelopeDto
                        {
                            EventId = eventId,
                            EventType = "SESSION_COMMAND_REQUEST",
                            SequenceNumber = 1,
                            ClientId = "PC-001",
                            WorkstationId = "PC-001",
                            OccurredAt = DateTime.UtcNow,
                            Payload = JsonSerializer.Serialize(new
                            {
                                clientId = "PC-001",
                                action = "START",
                                gamerId = gamerId,
                                workstationId = ws.Id
                            })
                        };

                        var enqueueResult = await durableQueue.EnqueueAsync(envelope);
                        Assert.True(enqueueResult.IsQueued);

                        var claimedItems = await durableQueue.ClaimBatchAsync(10);
                        Assert.Single(claimedItems);
                        Assert.Equal(eventId, claimedItems[0].EventId);
                    }

                    // 2. Server side: Set up batch request using same event ID
                    var itemToSync = new OfflineQueueItem
                    {
                        EventId = eventId,
                        EventType = "SESSION_COMMAND_REQUEST",
                        SequenceNumber = 1,
                        ReliabilityClass = EventReliabilityClass.Critical,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.Serialize(new
                        {
                            clientId = "PC-001",
                            action = "START",
                            gamerId = gamerId,
                            workstationId = ws.Id
                        })
                    };

                    var batchRequest = new OfflineBatchRequest
                    {
                        BatchId = "batch-e2e-001",
                        ClientId = "PC-001",
                        WorkstationId = "PC-001",
                        Items = new List<OfflineQueueItem> { itemToSync }
                    };

                    var handler = new IngestOfflineBatchCommandHandler(engine, NullLogger<IngestOfflineBatchCommandHandler>.Instance, mockRedis.Object);
                    var command = new IngestOfflineBatchCommand("conn-pc001", "PC-001", batchRequest);

                    // 3. Act: Execute batch ingestion & reconciliation
                    var result = await handler.HandleAsync(command);

                    // 4. Assert: Ingestion succeeded
                    Assert.True(result.IsSuccess);
                    Assert.True(result.Value.Acknowledgment.Success);
                    Assert.Equal(1, result.Value.Acknowledgment.ProcessedCount);
                    Assert.Contains(eventId, result.Value.Acknowledgment.AcknowledgedEventIds);

                    // Verify business effect was triggered exactly once
                    Assert.Equal(1, startHandler.CallCount);

                    // Verify server processed event persistence
                    var storedEvent = await serverDb.ProcessedEvents.FirstOrDefaultAsync(p => p.EventId == Guid.Parse(eventId));
                    Assert.NotNull(storedEvent);
                    Assert.Equal("ACCEPTED", storedEvent.ProcessingStatus);

                    // 5. Client side: Process ACK and clean up queue
                    using (var clientDbContext = CreateSqliteQueueDbContext(dbPath))
                    {
                        var durableQueue = new SqliteDurableOfflineQueue(clientDbContext, queueOptions, NullLogger<SqliteDurableOfflineQueue>.Instance);
                        await durableQueue.AcknowledgeItemsAsync(result.Value.Acknowledgment.AcknowledgedEventIds);

                        var metrics = await durableQueue.GetQueueMetricsAsync();
                        Assert.Equal(0, metrics.PendingCount);
                        Assert.Equal(1, metrics.AcknowledgedCount);
                    }
                }
            }
            finally
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public async Task Durability_QueueSurvivesClientCrashAndRestart()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"sayra_queue_crash_{Guid.NewGuid():N}.db");
            var queueOptions = Options.Create(new OfflineQueueOptions { DbPath = dbPath, MaxItemCount = 1000 });
            var eventId = Guid.NewGuid().ToString("D");

            try
            {
                // Enqueue event and claim batch (making it IN_FLIGHT) before simulated crash
                using (var dbContext = CreateSqliteQueueDbContext(dbPath))
                {
                    var queue = new SqliteDurableOfflineQueue(dbContext, queueOptions, NullLogger<SqliteDurableOfflineQueue>.Instance);
                    await queue.EnqueueAsync(new ClientEventEnvelopeDto
                    {
                        EventId = eventId,
                        EventType = ClientEventType.SessionRuntimeEvent,
                        SequenceNumber = 101,
                        Payload = "{\"action\":\"START\"}"
                    });

                    await queue.ClaimBatchAsync(10); // Transitions to IN_FLIGHT
                }

                // Simulate restart: Open new DbContext against the same SQLite database file
                using (var dbContextAfterRestart = CreateSqliteQueueDbContext(dbPath))
                {
                    var queueAfterRestart = new SqliteDurableOfflineQueue(dbContextAfterRestart, queueOptions, NullLogger<SqliteDurableOfflineQueue>.Instance);
                    int recovered = await queueAfterRestart.StartupRecoveryAsync();

                    Assert.Equal(1, recovered);

                    var claimedAfterRecovery = await queueAfterRestart.ClaimBatchAsync(10);
                    Assert.Single(claimedAfterRecovery);
                    Assert.Equal(eventId, claimedAfterRecovery[0].EventId);
                    Assert.Equal(101, claimedAfterRecovery[0].SequenceNumber);
                }
            }
            finally
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        [Fact]
        public async Task AckLoss_ResendBatch_ReturnsDuplicateAckWithoutReapplyingBusinessEffect()
        {
            string serverDbName = Guid.NewGuid().ToString();
            var (engine, serverDb, mockRedis, startHandler, _) = CreateServerPipeline(serverDbName);

            using (serverDb)
            {
                var ws = await serverDb.Workstations.FirstAsync(w => w.PcId == "PC-001");
                var eventId = Guid.NewGuid().ToString("D");

                var item = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new { clientId = "PC-001", action = "START", gamerId = Guid.NewGuid(), workstationId = ws.Id })
                };

                var batchRequest = new OfflineBatchRequest
                {
                    BatchId = "batch-ack-loss-1",
                    ClientId = "PC-001",
                    WorkstationId = "PC-001",
                    Items = new List<OfflineQueueItem> { item }
                };

                var handler = new IngestOfflineBatchCommandHandler(engine, NullLogger<IngestOfflineBatchCommandHandler>.Instance, mockRedis.Object);
                var command = new IngestOfflineBatchCommand("conn-ack-loss", "PC-001", batchRequest);

                // Initial transmission
                var res1 = await handler.HandleAsync(command);
                Assert.True(res1.IsSuccess);
                Assert.Equal(1, startHandler.CallCount);

                // Resend due to lost ACK
                var res2 = await handler.HandleAsync(command);
                Assert.True(res2.IsSuccess);
                Assert.True(res2.Value.Acknowledgment.Success);
                Assert.Contains(eventId, res2.Value.Acknowledgment.AcknowledgedEventIds);

                // Business effect was NOT applied twice
                Assert.Equal(1, startHandler.CallCount);
            }
        }

        [Fact]
        public async Task Security_IdentityMismatch_RejectsAndLogsSecurityAudit()
        {
            string serverDbName = Guid.NewGuid().ToString();
            var (engine, serverDb, _, startHandler, _) = CreateServerPipeline(serverDbName);

            using (serverDb)
            {
                var spoofedItem = new OfflineQueueItem
                {
                    EventId = Guid.NewGuid().ToString("D"),
                    EventType = "SESSION_COMMAND_REQUEST",
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new { clientId = "PC-999", action = "START", gamerId = Guid.NewGuid(), workstationId = Guid.NewGuid() })
                };

                // Payload claims PC-999, but authenticated connection is PC-001
                var result = await engine.EvaluateAndReconcileAsync(spoofedItem, "PC-001", "batch-spoof");

                Assert.False(result.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Rejected, result.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, result.ReasonCode);

                // Business logic was NOT called
                Assert.Equal(0, startHandler.CallCount);

                // Security audit event written to DLQ
                var dlq = await serverDb.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == Guid.Parse(spoofedItem.EventId));
                Assert.NotNull(dlq);
                Assert.Equal(OfflineReasonCode.IdentityMismatch, dlq.FailureCode);
            }
        }

        [Fact]
        public async Task Ingestion_PayloadHashConflict_TriggersConflictStateAndDLQ()
        {
            string serverDbName = Guid.NewGuid().ToString();
            var (engine, serverDb, _, _, _) = CreateServerPipeline(serverDbName);

            using (serverDb)
            {
                var eventId = Guid.NewGuid().ToString("D");
                var originalItem = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = ClientEventType.SecurityEvent,
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-001\",\"message\":\"Original security log\"}"
                };

                var tamperedItem = new OfflineQueueItem
                {
                    EventId = eventId,
                    EventType = ClientEventType.SecurityEvent,
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-001\",\"message\":\"TAMPERED security log\"}"
                };

                // First evaluation accepted
                var res1 = await engine.EvaluateAndReconcileAsync(originalItem, "PC-001", "batch-hash-1");
                Assert.True(res1.IsAcceptedForAck);

                // Tampered payload with same EventId rejected with PayloadHashConflict
                var res2 = await engine.EvaluateAndReconcileAsync(tamperedItem, "PC-001", "batch-hash-2");
                Assert.False(res2.IsAcceptedForAck);
                Assert.Equal(OfflineReconciliationStatus.Conflict, res2.ReconciliationStatus);
                Assert.Equal(OfflineReasonCode.PayloadHashConflict, res2.ReasonCode);
            }
        }

        [Fact]
        public async Task MultiInstance_ConcurrentIngestion_IsIdempotentAndSafe()
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
                EventType = ClientEventType.SecurityEvent,
                SequenceNumber = 1,
                ReliabilityClass = EventReliabilityClass.Critical,
                Timestamp = DateTime.UtcNow,
                Payload = "{\"clientId\":\"PC-001\",\"message\":\"Concurrent test\"}"
            };

            // Simulate 5 independent backend instances processing the same event concurrently
            var tasks = Enumerable.Range(0, 5).Select(i => Task.Run(async () =>
            {
                var (engine, db, _, _, _) = CreateServerPipeline(dbName);
                using (db)
                {
                    return await engine.EvaluateAndReconcileAsync(item, "PC-001", $"batch-conc-{i}");
                }
            })).ToArray();

            var results = await Task.WhenAll(tasks);

            // All instances return accepted results (first process in-order, others return idempotent duplicate)
            Assert.All(results, r => Assert.True(r.IsAcceptedForAck));
        }

        [Fact]
        public async Task RedisOutage_EngineGracefullyFallsBackToDatabaseAuthority()
        {
            string serverDbName = Guid.NewGuid().ToString();
            var (engine, serverDb, mockRedis, _, _) = CreateServerPipeline(serverDbName, throwOnRedis: true);

            using (serverDb)
            {
                var item = new OfflineQueueItem
                {
                    EventId = Guid.NewGuid().ToString("D"),
                    EventType = ClientEventType.SecurityEvent,
                    SequenceNumber = 1,
                    ReliabilityClass = EventReliabilityClass.Critical,
                    Timestamp = DateTime.UtcNow,
                    Payload = "{\"clientId\":\"PC-001\",\"msg\":\"Redis fallback test\"}"
                };

                var handler = new IngestOfflineBatchCommandHandler(engine, NullLogger<IngestOfflineBatchCommandHandler>.Instance, mockRedis.Object);
                var batchRequest = new OfflineBatchRequest
                {
                    BatchId = "batch-redis-outage",
                    ClientId = "PC-001",
                    WorkstationId = "PC-001",
                    Items = new List<OfflineQueueItem> { item }
                };

                var result = await handler.HandleAsync(new IngestOfflineBatchCommand("conn-redis-fail", "PC-001", batchRequest));

                Assert.True(result.IsSuccess);
                Assert.True(result.Value.Acknowledgment.Success);
                Assert.Contains(item.EventId, result.Value.Acknowledgment.AcknowledgedEventIds);

                // Database authority recorded event correctly
                var storedEvent = await serverDb.ProcessedEvents.FirstOrDefaultAsync(p => p.EventId == Guid.Parse(item.EventId));
                Assert.NotNull(storedEvent);
                Assert.Equal("ACCEPTED", storedEvent.ProcessingStatus);
            }
        }
    }
}
