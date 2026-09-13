using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sayra.Backend.Api.Controllers;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.OfflineQueue;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Infrastructure.Diagnostics;
using Sayra.Backend.Infrastructure.OfflineQueue;
using Sayra.Backend.Infrastructure.Persistence;
using Sayra.Backend.Shared;
using Xunit;

namespace Sayra.Backend.UnitTests
{
    public class OfflineMonitoringAndObservabilityTests
    {
        private ApplicationDbContext CreateInMemoryAppDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new ApplicationDbContext(options);
        }

        private SqliteOfflineQueueDbContext CreateInMemorySqliteDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<SqliteOfflineQueueDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new SqliteOfflineQueueDbContext(options);
        }

        [Fact]
        public void OfflineMetrics_RecordsAllOperationalInstruments_WithoutThrowing()
        {
            var metrics = new OfflineMetrics();

            // Queue
            metrics.RecordQueueEnqueue("START_SESSION", "CRITICAL");
            metrics.RecordQueueDequeue("START_SESSION", "CRITICAL");
            metrics.RecordQueueExpiration("AUDIT_LOG", "NORMAL");
            metrics.RecordQueueOverflow("AUDIT_LOG", "NORMAL");
            metrics.RecordQueueState(10, 2048, 45.5);

            // Sync
            metrics.RecordSyncBatchStarted();
            metrics.RecordSyncBatchCompleted(1.25, 5);
            metrics.RecordSyncBatchFailed("TRANSPORT_FAILURE", 0.50);
            metrics.RecordSyncEventSubmitted("START_SESSION", "CRITICAL");
            metrics.RecordSyncEventAccepted("START_SESSION", "CRITICAL");
            metrics.RecordSyncEventRejected("START_SESSION", "IDENTITY_MISMATCH");
            metrics.RecordSyncEventDuplicated("START_SESSION");
            metrics.RecordSyncEventConflicted("START_SESSION", "PAYLOAD_HASH_CONFLICT");
            metrics.RecordSyncEventDeferred("START_SESSION");
            metrics.RecordSyncEventExpired("AUDIT_LOG");

            // Retry & DLQ
            metrics.RecordRetryAttempt("DATABASE_BUSY", false);
            metrics.RecordEventMovedToDlq("PAYLOAD_HASH_CONFLICT", "START_SESSION");
            metrics.RecordDlqProcessingAttempt("RETRY", "SUCCESS");

            // Ordering & Reconciliation
            metrics.RecordSequenceGapDetected("START_SESSION");
            metrics.RecordReconciliationAttempt("START_SESSION");
            metrics.RecordReconciliationSuccess("START_SESSION", 0.12);
            metrics.RecordReconciliationConflict("START_SESSION", "BUSINESS_RULE_VIOLATION");
            metrics.RecordReconciliationFailure("START_SESSION", "WORKSTATION_NOT_FOUND", 0.05);

            // Idempotency & Security & Infrastructure
            metrics.RecordIdempotentDuplicate("START_SESSION");
            metrics.RecordIdempotencyConflict("START_SESSION", "PAYLOAD_HASH_CONFLICT");
            metrics.RecordSecurityRejection("IDENTITY_MISMATCH");
            metrics.RecordInfrastructureFailure("POSTGRES", "SaveChanges");
        }

        [Fact]
        public void OfflineMetrics_LabelSanitization_BoundsHighCardinalityInput()
        {
            var metrics = new OfflineMetrics();

            // Raw high cardinality event input (e.g., raw EventId or dynamic payload)
            string rawUntrustedInput = "EVENT-" + Guid.NewGuid().ToString() + "-SOME_UNBOUNDED_EXTRA_TEXT_EXCEEDING_SIXTY_FOUR_CHARACTERS_LONG_PAYLOAD";

            // Should safely sanitize without throwing exceptions
            metrics.RecordQueueEnqueue(rawUntrustedInput, "CRITICAL");
            metrics.RecordSyncEventRejected(rawUntrustedInput, rawUntrustedInput);
            metrics.RecordInfrastructureFailure(rawUntrustedInput, "Query");
        }

        [Fact]
        public async Task OfflineQueueHealthCheck_HealthyState_ReturnsHealthy()
        {
            string appDbName = Guid.NewGuid().ToString();
            string sqliteDbName = Guid.NewGuid().ToString();

            using var appDb = CreateInMemoryAppDbContext(appDbName);
            using var sqliteDb = CreateInMemorySqliteDbContext(sqliteDbName);

            var healthCheck = new OfflineQueueHealthCheck(sqliteDb, appDb);
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.True(result.Data.ContainsKey("PendingQueueItems"));
            Assert.True(result.Data.ContainsKey("ActiveDlqEvents"));
        }

        [Fact]
        public async Task OfflineQueueHealthCheck_HighDlqBacklog_ReturnsDegraded()
        {
            string appDbName = Guid.NewGuid().ToString();
            string sqliteDbName = Guid.NewGuid().ToString();

            using var appDb = CreateInMemoryAppDbContext(appDbName);
            using var sqliteDb = CreateInMemorySqliteDbContext(sqliteDbName);

            // Seed 1001 active DLQ events
            for (int i = 0; i < 1001; i++)
            {
                appDb.DeadLetterEvents.Add(new DeadLetterEvent
                {
                    EventId = Guid.NewGuid(),
                    BatchId = "batch-" + i,
                    ClientId = "PC-001",
                    EventType = "TEST_EVENT",
                    FailureCode = "TEST_FAILURE",
                    ProcessingStatus = DeadLetterStatus.DeadLetter
                });
            }
            await appDb.SaveChangesAsync();

            var healthCheck = new OfflineQueueHealthCheck(sqliteDb, appDb);
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("Active DLQ backlog is high", result.Description);
        }

        [Fact]
        public async Task GetOfflineOperationalSummary_AuthorizedUser_ReturnsCorrectSummary()
        {
            string appDbName = Guid.NewGuid().ToString();
            using var appDb = CreateInMemoryAppDbContext(appDbName);

            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();

            // Seed ProcessedEvents and DLQ
            appDb.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = Guid.NewGuid(),
                BatchId = "batch-1",
                ClientId = "PC-001",
                EventType = "START_SESSION",
                ProcessingStatus = "ACCEPTED"
            });
            appDb.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = Guid.NewGuid(),
                BatchId = "batch-1",
                ClientId = "PC-001",
                EventType = "START_SESSION",
                ProcessingStatus = "DUPLICATE"
            });

            appDb.DeadLetterEvents.Add(new DeadLetterEvent
            {
                EventId = Guid.NewGuid(),
                BatchId = "batch-1",
                ClientId = "PC-001",
                OrganizationId = orgId,
                SiteId = siteId.ToString(),
                EventType = "START_SESSION",
                FailureCode = "IDENTITY_MISMATCH",
                ProcessingStatus = DeadLetterStatus.DeadLetter
            });

            await appDb.SaveChangesAsync();

            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), PermissionCatalog.ViewWorkstations, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(AuthorizationResult.Allowed());

            var wsId = Guid.NewGuid();
            var mockWorkstationRepo = new Mock<IRepository<Workstation>>();
            var dummyWs = new Sayra.Backend.Domain.Workstation { PcId = "PC-001", OrganizationEntityId = orgId, SiteEntityId = siteId };
            mockWorkstationRepo.Setup(w => w.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Sayra.Backend.Domain.Workstation, bool>>>(), false, It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new List<Sayra.Backend.Domain.Workstation> { dummyWs });

            var mockStateReader = new Mock<IWorkstationStateReader>();
            var mockHealthReader = new Mock<IWorkstationHealthReader>();
            var mockHealthStore = new Mock<IWorkstationHealthStore>();
            var mockIncidentRepo = new Mock<IIncidentRepository>();
            var mockHistRepo = new Mock<ITelemetryHistoryRepository>();
            var mockAggRepo = new Mock<ITelemetryAggregateRepository>();
            var mockAuditRepo = new Mock<IRepository<AuditEvent>>();

            var processedEventRepo = new ProcessedEventRepository(appDb);
            var streamStateRepo = new WorkstationStreamStateRepository(appDb);
            var dlqRepo = new DeadLetterEventRepository(appDb);

            var queryService = new MonitoringQueryService(
                mockAuthService.Object,
                mockWorkstationRepo.Object,
                mockStateReader.Object,
                mockHealthReader.Object,
                mockHealthStore.Object,
                mockIncidentRepo.Object,
                mockHistRepo.Object,
                mockAggRepo.Object,
                mockAuditRepo.Object,
                processedEventRepo,
                streamStateRepo,
                dlqRepo);

            var principal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                OrganizationId = orgId,
                Roles = new List<string> { RoleCatalog.Administrator }
            };

            var result = await queryService.GetOfflineOperationalSummaryAsync(principal, siteId, orgId);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.TotalProcessedEvents);
            Assert.Equal(1, result.Value.AcceptedCount);
            Assert.Equal(1, result.Value.DuplicateCount);
            Assert.Equal(1, result.Value.TotalDlqEvents);
            Assert.Equal(1, result.Value.ActiveDeadLetterCount);
            Assert.True(result.Value.DlqFailureCodeBreakdown.ContainsKey("IDENTITY_MISMATCH"));
        }

        [Fact]
        public async Task GetOfflineOperationalSummary_CrossOrganizationAccess_IsDenied()
        {
            string appDbName = Guid.NewGuid().ToString();
            using var appDb = CreateInMemoryAppDbContext(appDbName);

            var userOrgId = Guid.NewGuid();
            var targetOrgId = Guid.NewGuid();

            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(a => a.AuthorizeAsync(It.IsAny<UserPrincipal>(), PermissionCatalog.ViewWorkstations, It.IsAny<object>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(AuthorizationResult.Allowed());

            var mockWorkstationRepo = new Mock<IRepository<Workstation>>();
            var mockStateReader = new Mock<IWorkstationStateReader>();
            var mockHealthReader = new Mock<IWorkstationHealthReader>();
            var mockHealthStore = new Mock<IWorkstationHealthStore>();
            var mockIncidentRepo = new Mock<IIncidentRepository>();
            var mockHistRepo = new Mock<ITelemetryHistoryRepository>();
            var mockAggRepo = new Mock<ITelemetryAggregateRepository>();
            var mockAuditRepo = new Mock<IRepository<AuditEvent>>();

            var queryService = new MonitoringQueryService(
                mockAuthService.Object,
                mockWorkstationRepo.Object,
                mockStateReader.Object,
                mockHealthReader.Object,
                mockHealthStore.Object,
                mockIncidentRepo.Object,
                mockHistRepo.Object,
                mockAggRepo.Object,
                mockAuditRepo.Object);

            var principal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active,
                OrganizationId = userOrgId,
                Roles = new List<string> { "Manager" } // Non-admin user
            };

            var result = await queryService.GetOfflineOperationalSummaryAsync(principal, null, targetOrgId);

            Assert.False(result.IsSuccess);
            Assert.Equal("CROSS_ORGANIZATION_ACCESS_DENIED", result.ErrorCode);
        }

        [Fact]
        public async Task MonitoringController_GetOfflineOperationalSummary_ReturnsOkForAuthorizedUser()
        {
            var mockQueryService = new Mock<IMonitoringQueryService>();
            var controller = new MonitoringController(mockQueryService.Object);

            var principal = new UserPrincipal
            {
                UserId = Guid.NewGuid(),
                IsAuthenticated = true,
                AccountStatus = UserAccountState.Active
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Items["UserPrincipal"] = principal;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var summaryDto = new OfflineOperationalSummaryDto
            {
                TotalProcessedEvents = 10,
                AcceptedCount = 8,
                DuplicateCount = 2
            };

            mockQueryService.Setup(q => q.GetOfflineOperationalSummaryAsync(principal, null, null, default))
                            .ReturnsAsync(Result<OfflineOperationalSummaryDto>.Success(summaryDto));

            var response = await controller.GetOfflineOperationalSummaryAsync(null, null);

            var okResult = Assert.IsType<OkObjectResult>(response);
            Assert.Equal(summaryDto, okResult.Value);
        }
    }
}
