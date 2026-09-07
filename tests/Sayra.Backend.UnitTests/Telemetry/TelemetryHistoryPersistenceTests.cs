using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Events;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.ValueObjects;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class TelemetryHistoryPersistenceTests
    {
        private readonly Mock<IRedisService> _redisMock = new();
        private readonly Mock<ISecurityEventService> _securityEventServiceMock = new();

        private ApplicationDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task IngestTelemetry_ValidSnapshot_PersistsHistoricalRecordInDb()
        {
            // Arrange
            using var dbContext = CreateInMemoryDbContext(nameof(IngestTelemetry_ValidSnapshot_PersistsHistoricalRecordInDb));
            var historyRepo = new TelemetryHistoryRepository(dbContext);
            var metricRepo = new Repository<TelemetryMetric>(dbContext);

            var idempotencyService = new TelemetryIdempotencyService(_redisMock.Object);
            var ingestionService = new TelemetryIngestionService(
                _securityEventServiceMock.Object,
                idempotencyService,
                NullLogger<TelemetryIngestionService>.Instance);

            var handler = new IngestTelemetryCommandHandler(
                ingestionService,
                metricRepo,
                historyRepo,
                dbContext,
                _redisMock.Object);

            var wsId = Guid.NewGuid();
            var model = new TelemetryModel
            {
                Cpu = 42.0,
                Ram = 2048.0,
                Uptime = 12000.0,
                RunningGameName = "CS2.exe",
                RunningGamePid = 1337,
                RunningGameCpu = 35.0,
                RunningGameRam = 1024.0,
                RunningGameDuration = 1800.0,
                TotalLaunches = 10,
                TotalCrashes = 1,
                TotalRestarts = 2,
                Timestamp = DateTime.UtcNow
            };

            var command = new IngestTelemetryCommand(wsId, "PC-101", model);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);

            var records = await historyRepo.GetHistoryForWorkstationAsync(wsId);
            Assert.Single(records);

            var record = records[0];
            Assert.Equal(wsId, record.WorkstationId);
            Assert.Equal("PC-101", record.PcId);
            Assert.Equal(42.0, record.Cpu);
            Assert.Equal(2048.0, record.Ram);
            Assert.Equal("CS2.exe", record.RunningGameName);
            Assert.Equal(1337, record.RunningGamePid);
            Assert.Equal(10, record.TotalLaunches);
            Assert.Equal(1, record.TotalCrashes);
            Assert.Equal(2, record.TotalRestarts);
        }

        [Fact]
        public async Task HeartbeatIngestion_PersistsHeartbeatHistoryRecord()
        {
            // Arrange
            using var dbContext = CreateInMemoryDbContext(nameof(HeartbeatIngestion_PersistsHeartbeatHistoryRecord));
            var historyRepo = new TelemetryHistoryRepository(dbContext);

            var idempotencyService = new TelemetryIdempotencyService(_redisMock.Object);
            var ingestionService = new TelemetryIngestionService(
                _securityEventServiceMock.Object,
                idempotencyService,
                stateStore: null,
                historyRepository: historyRepo,
                unitOfWork: dbContext,
                logger: NullLogger<TelemetryIngestionService>.Instance);

            var wsId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var orgId = Guid.NewGuid();

            var context = new TelemetryConnectionContext("CONN-01", "PC-102", wsId, siteId, orgId);
            var heartbeatMsg = new HeartbeatMessage
            {
                PcId = "PC-102",
                Timestamp = DateTime.UtcNow
            };

            // Act
            var result = await ingestionService.IngestHeartbeatAsync(context, heartbeatMsg, CancellationToken.None);

            // Assert
            Assert.True(result.IsAccepted);

            var heartbeats = await historyRepo.GetHeartbeatsForWorkstationAsync(wsId);
            Assert.Single(heartbeats);

            var record = heartbeats[0];
            Assert.Equal(wsId, record.WorkstationId);
            Assert.Equal(orgId, record.OrganizationId);
            Assert.Equal(siteId, record.SiteId);
            Assert.Equal("PC-102", record.PcId);
            Assert.Equal("CONN-01", record.ConnectionId);
        }

        [Fact]
        public async Task SpoofedPayloadIdentity_DoesNotCreateHistoricalRecordForSpoofedWorkstation()
        {
            // Arrange
            using var dbContext = CreateInMemoryDbContext(nameof(SpoofedPayloadIdentity_DoesNotCreateHistoricalRecordForSpoofedWorkstation));
            var auditRepo = new Repository<AuditEvent>(dbContext);

            var idempotencyService = new TelemetryIdempotencyService(_redisMock.Object);
            var ingestionService = new TelemetryIngestionService(
                _securityEventServiceMock.Object,
                idempotencyService,
                NullLogger<TelemetryIngestionService>.Instance);

            var handler = new IngestClientEventCommandHandler(ingestionService, auditRepo, dbContext);

            // Authenticated connection is PC-001, but payload claims PC-999
            var evtDto = new ClientEventEnvelopeDto
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = ClientEventType.SecurityEvent,
                ClientId = "PC-999",
                WorkstationId = "PC-999",
                OccurredAt = DateTime.UtcNow,
                Payload = "{}"
            };

            var command = new IngestClientEventCommand("PC-001", evtDto);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);

            // Verify no audit event was saved under spoofed PC-999
            var auditEvents = dbContext.AuditEvents.ToList();
            Assert.Empty(auditEvents);
        }

        [Fact]
        public async Task TelemetryHistoryRepository_EnforcesTenantIsolationAndBoundedTimeRangeQueries()
        {
            // Arrange
            using var dbContext = CreateInMemoryDbContext(nameof(TelemetryHistoryRepository_EnforcesTenantIsolationAndBoundedTimeRangeQueries));
            var historyRepo = new TelemetryHistoryRepository(dbContext);

            var org1 = Guid.NewGuid();
            var org2 = Guid.NewGuid();
            var site1 = Guid.NewGuid();
            var site2 = Guid.NewGuid();
            var ws1 = Guid.NewGuid();
            var ws2 = Guid.NewGuid();

            var now = DateTime.UtcNow;

            var r1 = new TelemetryHistoryRecord
            {
                WorkstationId = ws1,
                OrganizationId = org1,
                SiteId = site1,
                PcId = "PC-1",
                Cpu = 10,
                Ram = 1024,
                Uptime = 100,
                TotalLaunches = 0,
                TotalCrashes = 0,
                TotalRestarts = 0,
                ClientTimestamp = now.AddMinutes(-10),
                ServerReceivedAt = now.AddMinutes(-10),
                ProcessedAt = now.AddMinutes(-10)
            };

            var r2 = new TelemetryHistoryRecord
            {
                WorkstationId = ws1,
                OrganizationId = org1,
                SiteId = site1,
                PcId = "PC-1",
                Cpu = 20,
                Ram = 1024,
                Uptime = 160,
                TotalLaunches = 0,
                TotalCrashes = 0,
                TotalRestarts = 0,
                ClientTimestamp = now.AddMinutes(-5),
                ServerReceivedAt = now.AddMinutes(-5),
                ProcessedAt = now.AddMinutes(-5)
            };

            var r3 = new TelemetryHistoryRecord
            {
                WorkstationId = ws2,
                OrganizationId = org2,
                SiteId = site2,
                PcId = "PC-2",
                Cpu = 30,
                Ram = 2048,
                Uptime = 300,
                TotalLaunches = 1,
                TotalCrashes = 0,
                TotalRestarts = 0,
                ClientTimestamp = now.AddMinutes(-2),
                ServerReceivedAt = now.AddMinutes(-2),
                ProcessedAt = now.AddMinutes(-2)
            };

            dbContext.TelemetryHistoryRecords.AddRange(r1, r2, r3);
            await dbContext.SaveChangesAsync();

            // Act & Assert 1: Query by Workstation 1 with time filter
            var ws1Results = await historyRepo.GetHistoryForWorkstationAsync(ws1, from: now.AddMinutes(-7), to: now);
            Assert.Single(ws1Results);
            Assert.Equal(20, ws1Results[0].Cpu);

            // Act & Assert 2: Query by Site 1
            var site1Results = await historyRepo.GetHistoryForSiteAsync(site1);
            Assert.Equal(2, site1Results.Count);

            // Act & Assert 3: Query by Organization 2
            var org2Results = await historyRepo.GetHistoryForOrganizationAsync(org2);
            Assert.Single(org2Results);
            Assert.Equal(ws2, org2Results[0].WorkstationId);

            // Act & Assert 4: Query with limit capping
            var cappedResults = await historyRepo.GetHistoryForSiteAsync(site1, limit: 1);
            Assert.Single(cappedResults);
            Assert.Equal(20, cappedResults[0].Cpu); // Latest first
        }

        [Fact]
        public async Task StaleTelemetrySnapshot_IsRejectedAndNotSavedToHistory()
        {
            // Arrange
            using var dbContext = CreateInMemoryDbContext(nameof(StaleTelemetrySnapshot_IsRejectedAndNotSavedToHistory));
            var historyRepo = new TelemetryHistoryRepository(dbContext);
            var metricRepo = new Repository<TelemetryMetric>(dbContext);

            var now = DateTime.UtcNow;

            // Setup Redis mock to report latest timestamp recorded as 1 minute ago (in ticks)
            _redisMock.Setup(r => r.GetStringAsync("v1:telemetry:PC-105:latest_ts", It.IsAny<CancellationToken>()))
                .ReturnsAsync(now.AddMinutes(-1).Ticks.ToString(CultureInfo.InvariantCulture));

            var idempotencyService = new TelemetryIdempotencyService(_redisMock.Object);
            var ingestionService = new TelemetryIngestionService(
                _securityEventServiceMock.Object,
                idempotencyService,
                NullLogger<TelemetryIngestionService>.Instance);

            var handler = new IngestTelemetryCommandHandler(
                ingestionService,
                metricRepo,
                historyRepo,
                dbContext,
                _redisMock.Object);

            var wsId = Guid.NewGuid();
            var olderModel = new TelemetryModel
            {
                Cpu = 15.0,
                Ram = 1024.0,
                Uptime = 1000.0,
                Timestamp = now.AddMinutes(-5) // 5 minutes old vs latest recorded 1 minute ago
            };

            var command = new IngestTelemetryCommand(wsId, "PC-105", olderModel);

            // Act
            var result = await handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);

            var history = await historyRepo.GetHistoryForWorkstationAsync(wsId);
            Assert.Empty(history);
        }
    }
}
