using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Infrastructure.Persistence;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class TelemetryAggregatePersistenceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task SaveAggregatesBatchAsync_PersistsAndUpdatesAggregateRecordsCorrectly()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new TelemetryAggregateRepository(dbContext);

            var workstationId = Guid.NewGuid();
            var winStart = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            var winEnd = winStart.AddMinutes(1);

            var record = new TelemetryAggregateRecord
            {
                WorkstationId = workstationId,
                PcId = "PC-PERSIST-01",
                Granularity = "1m",
                WindowStart = winStart,
                WindowEnd = winEnd,
                SampleCount = 2,
                IsPartial = false,
                CpuMin = 10,
                CpuMax = 50,
                CpuAvg = 30,
                RamMin = 1000,
                RamMax = 2000,
                RamAvg = 1500
            };

            await repo.SaveAggregatesBatchAsync(new[] { record });

            var fetched = await repo.GetAggregatesForWorkstationAsync(workstationId, "1m");
            Assert.Single(fetched);
            Assert.Equal(30, fetched[0].CpuAvg);

            // Merge additional raw telemetry sample batch into existing aggregate record window
            var newRecord = new TelemetryAggregateRecord
            {
                WorkstationId = workstationId,
                PcId = "PC-PERSIST-01",
                Granularity = "1m",
                WindowStart = winStart,
                WindowEnd = winEnd,
                SampleCount = 1,
                IsPartial = false,
                CpuMin = 45,
                CpuMax = 45,
                CpuAvg = 45,
                RamMin = 1500,
                RamMax = 1500,
                RamAvg = 1500
            };

            await repo.SaveAggregatesBatchAsync(new[] { newRecord });

            var updated = await repo.GetAggregatesForWorkstationAsync(workstationId, "1m");
            Assert.Single(updated);
            // Merged CpuAvg: (30 * 2 + 45 * 1) / 3 = 35
            Assert.Equal(35, updated[0].CpuAvg);
            Assert.Equal(3, updated[0].SampleCount);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task CheckpointOperations_SavesAndRetrievesCheckpointsCorrectly()
        {
            using var dbContext = CreateInMemoryDbContext();
            var repo = new TelemetryAggregateRepository(dbContext);

            var lastWinEnd = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            var lastServerTs = new DateTime(2026, 9, 7, 11, 59, 58, DateTimeKind.Utc);

            var checkpoint = new TelemetryAggregationCheckpoint
            {
                Granularity = "1m",
                LastProcessedWindowEnd = lastWinEnd,
                LastProcessedServerTimestamp = lastServerTs,
                RecordsProcessed = 150
            };

            await repo.SaveCheckpointAsync(checkpoint);

            var fetched = await repo.GetCheckpointAsync("1m");
            Assert.NotNull(fetched);
            Assert.Equal("1m", fetched!.Granularity);
            Assert.Equal(lastWinEnd, fetched.LastProcessedWindowEnd);
            Assert.Equal(150, fetched.RecordsProcessed);
        }
    }
}
