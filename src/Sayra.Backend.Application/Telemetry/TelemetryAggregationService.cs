using System;
using System.Collections.Generic;
using System.Linq;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Telemetry
{
    public class TelemetryAggregationService : ITelemetryAggregationService
    {
        public (DateTime windowStart, DateTime windowEnd) GetWindowBoundaries(DateTime timestamp, string granularity)
        {
            var utc = timestamp.Kind == DateTimeKind.Utc ? timestamp : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

            return granularity.ToLowerInvariant() switch
            {
                "1m" => (
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc),
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc).AddMinutes(1)
                ),
                "5m" => (
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 5) * 5, 0, DateTimeKind.Utc),
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 5) * 5, 0, DateTimeKind.Utc).AddMinutes(5)
                ),
                "1h" => (
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
                    new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1)
                ),
                "1d" => (
                    new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1)
                ),
                _ => throw new ArgumentException($"Unsupported granularity '{granularity}'", nameof(granularity))
            };
        }

        public IReadOnlyList<TelemetryAggregateRecord> AggregateRawRecords(
            IEnumerable<TelemetryHistoryRecord> rawRecords,
            string granularity)
        {
            if (rawRecords == null) return Array.Empty<TelemetryAggregateRecord>();

            var list = rawRecords.Where(r => r != null).ToList();
            if (list.Count == 0) return Array.Empty<TelemetryAggregateRecord>();

            var result = new List<TelemetryAggregateRecord>();

            // Group by WorkstationId and UTC Window
            var groups = list.GroupBy(r =>
            {
                var (start, end) = GetWindowBoundaries(r.ServerReceivedAt, granularity);
                return (r.WorkstationId, start, end);
            });

            foreach (var g in groups)
            {
                var recordsInWindow = g.OrderBy(r => r.ServerReceivedAt).ToList();
                int count = recordsInWindow.Count;
                if (count == 0) continue;

                var first = recordsInWindow[0];
                var (winStart, winEnd) = (g.Key.start, g.Key.end);

                // Expected sample count for full window based on default 30s interval
                int expectedSamples = granularity switch
                {
                    "1m" => 2,
                    "5m" => 10,
                    "1h" => 120,
                    "1d" => 2880,
                    _ => 1
                };

                bool isPartial = count < expectedSamples;

                // Gauges: CPU & RAM
                var cpus = recordsInWindow.Select(r => r.Cpu).OrderBy(v => v).ToList();
                var rams = recordsInWindow.Select(r => r.Ram).OrderBy(v => v).ToList();

                // Counter deltas with reset detection
                double uptimeDelta = CalculateDoubleCounterDelta(recordsInWindow.Select(r => r.Uptime).ToList());
                int launchesDelta = CalculateIntCounterDelta(recordsInWindow.Select(r => r.TotalLaunches).ToList());
                int crashesDelta = CalculateIntCounterDelta(recordsInWindow.Select(r => r.TotalCrashes).ToList());
                int restartsDelta = CalculateIntCounterDelta(recordsInWindow.Select(r => r.TotalRestarts).ToList());

                // Active Game Stats
                var gameRecords = recordsInWindow
                    .Where(r => !string.IsNullOrWhiteSpace(r.RunningGameName))
                    .ToList();

                string? primaryGame = gameRecords
                    .GroupBy(r => r.RunningGameName!)
                    .OrderByDescending(grp => grp.Count())
                    .Select(grp => grp.Key)
                    .FirstOrDefault();

                int gameCount = gameRecords.Count;
                double? gameCpuAvg = gameCount > 0 && gameRecords.Any(r => r.RunningGameCpu.HasValue)
                    ? gameRecords.Where(r => r.RunningGameCpu.HasValue).Average(r => r.RunningGameCpu!.Value)
                    : null;
                double? gameCpuMax = gameCount > 0 && gameRecords.Any(r => r.RunningGameCpu.HasValue)
                    ? gameRecords.Where(r => r.RunningGameCpu.HasValue).Max(r => r.RunningGameCpu!.Value)
                    : null;

                double? gameRamAvg = gameCount > 0 && gameRecords.Any(r => r.RunningGameRam.HasValue)
                    ? gameRecords.Where(r => r.RunningGameRam.HasValue).Average(r => r.RunningGameRam!.Value)
                    : null;
                double? gameRamMax = gameCount > 0 && gameRecords.Any(r => r.RunningGameRam.HasValue)
                    ? gameRecords.Where(r => r.RunningGameRam.HasValue).Max(r => r.RunningGameRam!.Value)
                    : null;

                double? gameDurationMax = gameCount > 0 && gameRecords.Any(r => r.RunningGameDuration.HasValue)
                    ? gameRecords.Where(r => r.RunningGameDuration.HasValue).Max(r => r.RunningGameDuration!.Value)
                    : null;

                var agg = new TelemetryAggregateRecord
                {
                    WorkstationId = first.WorkstationId,
                    OrganizationId = first.OrganizationId,
                    SiteId = first.SiteId,
                    PcId = first.PcId,
                    Granularity = granularity,
                    WindowStart = winStart,
                    WindowEnd = winEnd,
                    SampleCount = count,
                    IsPartial = isPartial,

                    CpuMin = cpus.First(),
                    CpuMax = cpus.Last(),
                    CpuAvg = cpus.Average(),
                    CpuP50 = CalculatePercentile(cpus, 0.50),
                    CpuP95 = CalculatePercentile(cpus, 0.95),
                    CpuP99 = CalculatePercentile(cpus, 0.99),

                    RamMin = rams.First(),
                    RamMax = rams.Last(),
                    RamAvg = rams.Average(),
                    RamP50 = CalculatePercentile(rams, 0.50),
                    RamP95 = CalculatePercentile(rams, 0.95),
                    RamP99 = CalculatePercentile(rams, 0.99),

                    UptimeDelta = uptimeDelta,
                    LaunchesDelta = launchesDelta,
                    CrashesDelta = crashesDelta,
                    RestartsDelta = restartsDelta,

                    PrimaryGameName = primaryGame,
                    GameSampleCount = gameCount,
                    GameCpuAvg = gameCpuAvg,
                    GameCpuMax = gameCpuMax,
                    GameRamAvg = gameRamAvg,
                    GameRamMax = gameRamMax,
                    GameDurationMax = gameDurationMax
                };

                result.Add(agg);
            }

            return result;
        }

        public IReadOnlyList<TelemetryAggregateRecord> RollupAggregates(
            IEnumerable<TelemetryAggregateRecord> sourceAggregates,
            string targetGranularity)
        {
            if (sourceAggregates == null) return Array.Empty<TelemetryAggregateRecord>();

            var list = sourceAggregates.Where(a => a != null).ToList();
            if (list.Count == 0) return Array.Empty<TelemetryAggregateRecord>();

            var result = new List<TelemetryAggregateRecord>();

            var groups = list.GroupBy(a =>
            {
                var (start, end) = GetWindowBoundaries(a.WindowStart, targetGranularity);
                return (a.WorkstationId, start, end);
            });

            foreach (var g in groups)
            {
                var items = g.OrderBy(a => a.WindowStart).ToList();
                if (items.Count == 0) continue;

                var first = items[0];
                var (winStart, winEnd) = (g.Key.start, g.Key.end);

                int totalSamples = items.Sum(i => i.SampleCount);
                bool isPartial = items.Any(i => i.IsPartial);

                double cpuMin = items.Min(i => i.CpuMin);
                double cpuMax = items.Max(i => i.CpuMax);
                double cpuAvg = totalSamples > 0 ? items.Sum(i => i.CpuAvg * i.SampleCount) / totalSamples : 0;
                double cpuP50 = items.Average(i => i.CpuP50);
                double cpuP95 = items.Max(i => i.CpuP95);
                double cpuP99 = items.Max(i => i.CpuP99);

                double ramMin = items.Min(i => i.RamMin);
                double ramMax = items.Max(i => i.RamMax);
                double ramAvg = totalSamples > 0 ? items.Sum(i => i.RamAvg * i.SampleCount) / totalSamples : 0;
                double ramP50 = items.Average(i => i.RamP50);
                double ramP95 = items.Max(i => i.RamP95);
                double ramP99 = items.Max(i => i.RamP99);

                double uptimeDelta = items.Sum(i => i.UptimeDelta);
                int launchesDelta = items.Sum(i => i.LaunchesDelta);
                int crashesDelta = items.Sum(i => i.CrashesDelta);
                int restartsDelta = items.Sum(i => i.RestartsDelta);

                var gameItems = items.Where(i => !string.IsNullOrWhiteSpace(i.PrimaryGameName)).ToList();
                string? primaryGame = gameItems
                    .GroupBy(i => i.PrimaryGameName!)
                    .OrderByDescending(grp => grp.Sum(i => i.GameSampleCount))
                    .Select(grp => grp.Key)
                    .FirstOrDefault();

                int gameSampleCount = items.Sum(i => i.GameSampleCount);
                double? gameCpuAvg = gameItems.Any(i => i.GameCpuAvg.HasValue)
                    ? gameItems.Where(i => i.GameCpuAvg.HasValue).Average(i => i.GameCpuAvg!.Value)
                    : null;
                double? gameCpuMax = gameItems.Any(i => i.GameCpuMax.HasValue)
                    ? gameItems.Where(i => i.GameCpuMax.HasValue).Max(i => i.GameCpuMax!.Value)
                    : null;

                double? gameRamAvg = gameItems.Any(i => i.GameRamAvg.HasValue)
                    ? gameItems.Where(i => i.GameRamAvg.HasValue).Average(i => i.GameRamAvg!.Value)
                    : null;
                double? gameRamMax = gameItems.Any(i => i.GameRamMax.HasValue)
                    ? gameItems.Where(i => i.GameRamMax.HasValue).Max(i => i.GameRamMax!.Value)
                    : null;

                double? gameDurationMax = gameItems.Any(i => i.GameDurationMax.HasValue)
                    ? gameItems.Where(i => i.GameDurationMax.HasValue).Max(i => i.GameDurationMax!.Value)
                    : null;

                var agg = new TelemetryAggregateRecord
                {
                    WorkstationId = first.WorkstationId,
                    OrganizationId = first.OrganizationId,
                    SiteId = first.SiteId,
                    PcId = first.PcId,
                    Granularity = targetGranularity,
                    WindowStart = winStart,
                    WindowEnd = winEnd,
                    SampleCount = totalSamples,
                    IsPartial = isPartial,

                    CpuMin = cpuMin,
                    CpuMax = cpuMax,
                    CpuAvg = cpuAvg,
                    CpuP50 = cpuP50,
                    CpuP95 = cpuP95,
                    CpuP99 = cpuP99,

                    RamMin = ramMin,
                    RamMax = ramMax,
                    RamAvg = ramAvg,
                    RamP50 = ramP50,
                    RamP95 = ramP95,
                    RamP99 = ramP99,

                    UptimeDelta = uptimeDelta,
                    LaunchesDelta = launchesDelta,
                    CrashesDelta = crashesDelta,
                    RestartsDelta = restartsDelta,

                    PrimaryGameName = primaryGame,
                    GameSampleCount = gameSampleCount,
                    GameCpuAvg = gameCpuAvg,
                    GameCpuMax = gameCpuMax,
                    GameRamAvg = gameRamAvg,
                    GameRamMax = gameRamMax,
                    GameDurationMax = gameDurationMax
                };

                result.Add(agg);
            }

            return result;
        }

        private static double CalculatePercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0) return 0;
            if (sortedValues.Count == 1) return sortedValues[0];

            double realIndex = percentile * (sortedValues.Count - 1);
            int index = (int)realIndex;
            double frac = realIndex - index;

            if (index + 1 < sortedValues.Count)
            {
                return sortedValues[index] + frac * (sortedValues[index + 1] - sortedValues[index]);
            }

            return sortedValues[index];
        }

        private static double CalculateDoubleCounterDelta(List<double> values)
        {
            if (values == null || values.Count < 2) return 0;

            double delta = 0;
            for (int i = 1; i < values.Count; i++)
            {
                double prev = values[i - 1];
                double curr = values[i];

                if (curr >= prev)
                {
                    delta += (curr - prev);
                }
                else
                {
                    // Reset occurred
                    delta += curr;
                }
            }

            return delta;
        }

        private static int CalculateIntCounterDelta(List<int> values)
        {
            if (values == null || values.Count < 2) return 0;

            int delta = 0;
            for (int i = 1; i < values.Count; i++)
            {
                int prev = values[i - 1];
                int curr = values[i];

                if (curr >= prev)
                {
                    delta += (curr - prev);
                }
                else
                {
                    // Reset occurred
                    delta += curr;
                }
            }

            return delta;
        }
    }
}
