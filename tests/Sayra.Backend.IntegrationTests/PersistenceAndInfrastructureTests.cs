using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Sayra.Backend.Api;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Infrastructure.Persistence;

namespace Sayra.Backend.IntegrationTests
{
    public class PersistenceAndInfrastructureTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PersistenceAndInfrastructureTests(WebApplicationFactory<Program> factory)
        {
            EnvLoader.Load();
            _factory = factory;
        }

        [Fact]
        public async Task DbContext_Should_Persist_Workstation_Successfully()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workstation = new Workstation
            {
                PcId = $"PC-UNIT-{Guid.NewGuid():N}",
                Name = "PC-UNIT-01",
                SiteId = "SITE-ALPHA",
                Hostname = "DESKTOP-UNIT-01",
                IpAddress = "192.168.1.1",
                MacAddress = $"00:1A:2B:3C:4D:{Random.Shared.Next(10, 99)}",
                Status = "Online",
                LastSeen = DateTime.UtcNow
            };

            await dbContext.Workstations.AddAsync(workstation);
            await dbContext.SaveChangesAsync();

            var saved = await dbContext.Workstations.FindAsync(workstation.Id);
            Assert.NotNull(saved);
            Assert.Equal("PC-UNIT-01", saved.Name);
            Assert.Equal("Online", saved.Status);
        }

        [Fact]
        public async Task DbContext_Should_Persist_WorkstationSession_With_Precise_Decimal_Types()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var session = new WorkstationSession
            {
                WorkstationId = Guid.NewGuid(),
                GamerId = Guid.NewGuid(),
                StartTime = DateTime.UtcNow,
                RatePerHour = 12.34567m, // Rounds according to model precision configurations
                CurrentCost = 5.00m,
                RemainingCredits = 100.50m,
                BillingAmount = 15.00m,
                Currency = "SAY",
                SessionState = "Active"
            };

            await dbContext.WorkstationSessions.AddAsync(session);
            await dbContext.SaveChangesAsync();

            var saved = await dbContext.WorkstationSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.Id);
            Assert.NotNull(saved);
            Assert.Equal(Math.Round(12.34567m, 4), saved.RatePerHour); // Configuration specifies (18, 4)
            Assert.Equal(5.00m, saved.CurrentCost);
            Assert.Equal(100.50m, saved.RemainingCredits);
            Assert.Equal(15.00m, saved.BillingAmount);
            Assert.Equal("SAY", saved.Currency);
        }

        [Fact]
        public async Task DbContext_Should_Persist_AuditEvent_And_Enforce_EventId_Uniqueness()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var eventId = Guid.NewGuid();
            var auditEvent1 = new AuditEvent
            {
                EventId = eventId,
                EventType = "PROCESS_LAUNCHED",
                EventVersion = 1,
                Payload = "{\"processName\":\"Steam.exe\"}",
                Timestamp = DateTime.UtcNow
            };

            await dbContext.AuditEvents.AddAsync(auditEvent1);
            await dbContext.SaveChangesAsync();

            var saved = await dbContext.AuditEvents.AsNoTracking().FirstOrDefaultAsync(a => a.EventId == eventId);
            Assert.NotNull(saved);
            Assert.Equal("PROCESS_LAUNCHED", saved.EventType);

            // Attempt to insert another event with same EventId (duplicate)
            var auditEvent2 = new AuditEvent
            {
                EventId = eventId,
                EventType = "PROCESS_LAUNCHED",
                EventVersion = 1,
                Payload = "{\"processName\":\"Steam.exe\"}",
                Timestamp = DateTime.UtcNow
            };

            await dbContext.AuditEvents.AddAsync(auditEvent2);

            // Unique index "IX_AuditEvents_EventId" should trigger Unique Constraint Violation on DbUpdateException
            var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                await dbContext.SaveChangesAsync();
            });

            Assert.NotNull(exception);
        }

        [Fact]
        public async Task Transaction_Should_Rollback_State_On_Failure()
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workstation = new Workstation
            {
                PcId = $"PC-TX-{Guid.NewGuid():N}",
                Name = "PC-TX-TEST",
                SiteId = "SITE-BETA",
                Hostname = "DESKTOP-TX-01",
                IpAddress = "10.0.0.1",
                MacAddress = $"AA:BB:CC:DD:EE:{Random.Shared.Next(10, 99)}",
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            var strategy = dbContext.Database.CreateExecutionStrategy();
            Guid workstationId = Guid.Empty;

            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await dbContext.Database.BeginTransactionAsync();

                await dbContext.Workstations.AddAsync(workstation);
                await dbContext.SaveChangesAsync();

                workstationId = workstation.Id;

                await transaction.RollbackAsync();
            });

            // Clear the EF tracker so it is forced to query the actual PostgreSQL database
            dbContext.ChangeTracker.Clear();

            var saved = await dbContext.Workstations.FindAsync(workstationId);
            Assert.Null(saved); // Should not have persisted due to rollback
        }

        [Fact]
        public async Task RedisService_Should_Store_And_Retrieve_Ephemeral_State_Determinstically()
        {
            using var scope = _factory.Services.CreateScope();
            var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

            var workstationId = Guid.NewGuid();
            var cacheKey = RedisKeyGenerator.WorkstationStateKey(workstationId);

            var state = new CacheTestState
            {
                Status = "InUse",
                GamerName = "JohnDoe",
                LastPing = DateTime.UtcNow
            };

            await redisService.SetAsync(cacheKey, state, TimeSpan.FromMinutes(5));

            var retrieved = await redisService.GetAsync<CacheTestState>(cacheKey);
            Assert.NotNull(retrieved);
            Assert.Equal("InUse", retrieved.Status);
            Assert.Equal("JohnDoe", retrieved.GamerName);

            // Clean up
            await redisService.RemoveAsync(cacheKey);
            var deleted = await redisService.GetAsync<CacheTestState>(cacheKey);
            Assert.Null(deleted);
        }

        [Fact]
        public async Task RedisService_Should_Gracefully_Degrade_On_Ping()
        {
            using var scope = _factory.Services.CreateScope();
            var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();

            var pingResult = await redisService.PingAsync();
            // Under normal healthy test context, ping should be true
            Assert.True(pingResult);
        }

        private class CacheTestState
        {
            public string Status { get; set; } = string.Empty;
            public string GamerName { get; set; } = string.Empty;
            public DateTime LastPing { get; set; }
        }
    }
}
