using System;
using System.Collections.Generic;
using System.Linq;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Domain.Entities;
using Xunit;

namespace Sayra.Backend.UnitTests.Telemetry
{
    public class TelemetryAggregationTests
    {
        private readonly TelemetryAggregationService _service = new TelemetryAggregationService();

        [Theory]
        [InlineData("1m", "2026-09-07T14:23:45Z", "2026-09-07T14:23:00Z", "2026-09-07T14:24:00Z")]
        [InlineData("5m", "2026-09-07T14:23:45Z", "2026-09-07T14:20:00Z", "2026-09-07T14:25:00Z")]
        [InlineData("1h", "2026-09-07T14:23:45Z", "2026-09-07T14:00:00Z", "2026-09-07T15:00:00Z")]
        [InlineData("1d", "2026-09-07T14:23:45Z", "2026-09-07T00:00:00Z", "2026-09-08T00:00:00Z")]
        public void GetWindowBoundaries_ComputesUtcAlignedBoundaries(
            string granularity, string inputTs, string expectedStart, string expectedEnd)
        {
            var ts = DateTime.Parse(inputTs).ToUniversalTime();
            var expStart = DateTime.Parse(expectedStart).ToUniversalTime();
            var expEnd = DateTime.Parse(expectedEnd).ToUniversalTime();

            var (start, end) = _service.GetWindowBoundaries(ts, granularity);

            Assert.Equal(expStart, start);
            Assert.Equal(expEnd, end);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AggregateRawRecords_CalculatesGaugesAndPercentilesCorrectly()
        {
            var workstationId = Guid.NewGuid();
            var orgId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var winTime = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

            var rawRecords = new List<TelemetryHistoryRecord>
            {
                new TelemetryHistoryRecord
                {
                    WorkstationId = workstationId, OrganizationId = orgId, SiteId = siteId, PcId = "PC-01",
                    Cpu = 10.0, Ram = 4000.0, Uptime = 100, TotalLaunches = 5, TotalCrashes = 0, TotalRestarts = 1,
                    ServerReceivedAt = winTime.AddSeconds(10)
                },
                new TelemetryHistoryRecord
                {
                    WorkstationId = workstationId, OrganizationId = orgId, SiteId = siteId, PcId = "PC-01",
                    Cpu = 50.0, Ram = 6000.0, Uptime = 130, TotalLaunches = 6, TotalCrashes = 1, TotalRestarts = 1,
                    ServerReceivedAt = winTime.AddSeconds(40)
                }
            };

            var aggregates = _service.AggregateRawRecords(rawRecords, "1m");

            Assert.Single(aggregates);
            var agg = aggregates[0];

            Assert.Equal(workstationId, agg.WorkstationId);
            Assert.Equal(orgId, agg.OrganizationId);
            Assert.Equal(siteId, agg.SiteId);
            Assert.Equal("PC-01", agg.PcId);
            Assert.Equal("1m", agg.Granularity);
            Assert.Equal(2, agg.SampleCount);
            Assert.False(agg.IsPartial); // 2 samples matches expected 1m samples

            Assert.Equal(10.0, agg.CpuMin);
            Assert.Equal(50.0, agg.CpuMax);
            Assert.Equal(30.0, agg.CpuAvg);
            Assert.Equal(30.0, agg.CpuP50);

            Assert.Equal(4000.0, agg.RamMin);
            Assert.Equal(6000.0, agg.RamMax);
            Assert.Equal(5000.0, agg.RamAvg);

            Assert.Equal(30.0, agg.UptimeDelta);
            Assert.Equal(1, agg.LaunchesDelta);
            Assert.Equal(1, agg.CrashesDelta);
            Assert.Equal(0, agg.RestartsDelta);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AggregateRawRecords_HandlesCounterResetsGracefully()
        {
            var workstationId = Guid.NewGuid();
            var winTime = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

            var rawRecords = new List<TelemetryHistoryRecord>
            {
                new TelemetryHistoryRecord
                {
                    WorkstationId = workstationId, PcId = "PC-02",
                    Cpu = 20.0, Ram = 4000.0, Uptime = 3600, TotalLaunches = 50, TotalCrashes = 2, TotalRestarts = 5,
                    ServerReceivedAt = winTime.AddSeconds(10)
                },
                // Reboot occurs between sample 1 and sample 2 -> Uptime resets from 3600 to 15
                new TelemetryHistoryRecord
                {
                    WorkstationId = workstationId, PcId = "PC-02",
                    Cpu = 25.0, Ram = 4200.0, Uptime = 15, TotalLaunches = 51, TotalCrashes = 2, TotalRestarts = 6,
                    ServerReceivedAt = winTime.AddSeconds(40)
                }
            };

            var aggregates = _service.AggregateRawRecords(rawRecords, "1m");

            Assert.Single(aggregates);
            var agg = aggregates[0];

            // Reset handled: delta = 15 (reset value) rather than negative -3585
            Assert.Equal(15.0, agg.UptimeDelta);
            Assert.Equal(1, agg.LaunchesDelta);
            Assert.Equal(0, agg.CrashesDelta);
            Assert.Equal(1, agg.RestartsDelta);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RollupAggregates_Combines1mAggregatesInto5mRollupsCorrectly()
        {
            var workstationId = Guid.NewGuid();
            var winTime = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

            var sourceAggregates = new List<TelemetryAggregateRecord>
            {
                new TelemetryAggregateRecord
                {
                    WorkstationId = workstationId, PcId = "PC-03", Granularity = "1m",
                    WindowStart = winTime, WindowEnd = winTime.AddMinutes(1), SampleCount = 2, IsPartial = false,
                    CpuMin = 10, CpuMax = 30, CpuAvg = 20, CpuP50 = 20, CpuP95 = 28, CpuP99 = 29,
                    RamMin = 1000, RamMax = 2000, RamAvg = 1500, RamP50 = 1500, RamP95 = 1900, RamP99 = 1950,
                    UptimeDelta = 60, LaunchesDelta = 1, CrashesDelta = 0, RestartsDelta = 0
                },
                new TelemetryAggregateRecord
                {
                    WorkstationId = workstationId, PcId = "PC-03", Granularity = "1m",
                    WindowStart = winTime.AddMinutes(1), WindowEnd = winTime.AddMinutes(2), SampleCount = 2, IsPartial = false,
                    CpuMin = 20, CpuMax = 60, CpuAvg = 40, CpuP50 = 40, CpuP95 = 58, CpuP99 = 59,
                    RamMin = 2000, RamMax = 3000, RamAvg = 2500, RamP50 = 2500, RamP95 = 2900, RamP99 = 2950,
                    UptimeDelta = 60, LaunchesDelta = 0, CrashesDelta = 1, RestartsDelta = 0
                }
            };

            var rollups = _service.RollupAggregates(sourceAggregates, "5m");

            Assert.Single(rollups);
            var rollup = rollups[0];

            Assert.Equal("5m", rollup.Granularity);
            Assert.Equal(4, rollup.SampleCount);
            Assert.Equal(10, rollup.CpuMin);
            Assert.Equal(60, rollup.CpuMax);
            Assert.Equal(30, rollup.CpuAvg);

            Assert.Equal(120, rollup.UptimeDelta);
            Assert.Equal(1, rollup.LaunchesDelta);
            Assert.Equal(1, rollup.CrashesDelta);
            Assert.Equal(0, rollup.RestartsDelta);
        }
    }
}
