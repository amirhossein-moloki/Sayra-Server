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

            if (string.Equals(granularity, "1m", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
                return (start, start.AddMinutes(1));
            }

            if (string.Equals(granularity, "5m", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 5) * 5, 0, DateTimeKind.Utc);
                return (start, start.AddMinutes(5));
            }

            if (string.Equals(granularity, "1h", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
                return (start, start.AddHours(1));
            }

            if (string.Equals(granularity, "1d", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
                return (start, start.AddDays(1));
            }

            throw new ArgumentException($"Unsupported granularity '{granularity}'", nameof(granularity));
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
                    "1m" or "1M" => 2,
                    "5m" or "5M" => 10,
                    "1h" or "1H" => 120,
                    "1d" or "1D" => 2880,
                    _ => 1
                };

                bool isPartial = count < expectedSamples;

                var cpus = new List<double>(count);
                var rams = new List<double>(count);

                double uptimeDelta = 0;
                int launchesDelta = 0;
                int crashesDelta = 0;
                int restartsDelta = 0;

                Dictionary<string, int>? gameCounts = null;
                double gameCpuSum = 0;
                double? gameCpuMax = null;
                int gameCpuCount = 0;

                double gameRamSum = 0;
                double? gameRamMax = null;
                int gameRamCount = 0;

                double? gameDurationMax = null;
                int gameRecordCount = 0;

                // Single pass through ordered records in window
                for (int i = 0; i < count; i++)
                {
                    var r = recordsInWindow[i];
                    cpus.Add(r.Cpu);
                    rams.Add(r.Ram);

                    if (i > 0)
                    {
                        var prev = recordsInWindow[i - 1];
                        uptimeDelta += r.Uptime >= prev.Uptime ? (r.Uptime - prev.Uptime) : r.Uptime;
                        launchesDelta += r.TotalLaunches >= prev.TotalLaunches ? (r.TotalLaunches - prev.TotalLaunches) : r.TotalLaunches;
                        crashesDelta += r.TotalCrashes >= prev.TotalCrashes ? (r.TotalCrashes - prev.TotalCrashes) : r.TotalCrashes;
                        restartsDelta += r.TotalRestarts >= prev.TotalRestarts ? (r.TotalRestarts - prev.TotalRestarts) : r.TotalRestarts;
                    }

                    if (!string.IsNullOrWhiteSpace(r.RunningGameName))
                    {
                        gameRecordCount++;
                        gameCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        gameCounts[r.RunningGameName] = gameCounts.TryGetValue(r.RunningGameName, out int cnt) ? cnt + 1 : 1;

                        if (r.RunningGameCpu.HasValue)
                        {
                            gameCpuSum += r.RunningGameCpu.Value;
                            gameCpuCount++;
                            if (!gameCpuMax.HasValue || r.RunningGameCpu.Value > gameCpuMax.Value)
                                gameCpuMax = r.RunningGameCpu.Value;
                        }

                        if (r.RunningGameRam.HasValue)
                        {
                            gameRamSum += r.RunningGameRam.Value;
                            gameRamCount++;
                            if (!gameRamMax.HasValue || r.RunningGameRam.Value > gameRamMax.Value)
                                gameRamMax = r.RunningGameRam.Value;
                        }

                        if (r.RunningGameDuration.HasValue)
                        {
                            if (!gameDurationMax.HasValue || r.RunningGameDuration.Value > gameDurationMax.Value)
                                gameDurationMax = r.RunningGameDuration.Value;
                        }
                    }
                }

                cpus.Sort();
                rams.Sort();

                string? primaryGame = null;
                if (gameCounts != null && gameCounts.Count > 0)
                {
                    int maxCount = -1;
                    foreach (var kvp in gameCounts)
                    {
                        if (kvp.Value > maxCount)
                        {
                            maxCount = kvp.Value;
                            primaryGame = kvp.Key;
                        }
                    }
                }

                double? gameCpuAvg = gameCpuCount > 0 ? gameCpuSum / gameCpuCount : null;
                double? gameRamAvg = gameRamCount > 0 ? gameRamSum / gameRamCount : null;

                double cpuSum = 0;
                for (int i = 0; i < count; i++) cpuSum += cpus[i];
                double cpuAvg = cpuSum / count;

                double ramSum = 0;
                for (int i = 0; i < count; i++) ramSum += rams[i];
                double ramAvg = ramSum / count;

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

                    CpuMin = cpus[0],
                    CpuMax = cpus[count - 1],
                    CpuAvg = cpuAvg,
                    CpuP50 = CalculatePercentile(cpus, 0.50),
                    CpuP95 = CalculatePercentile(cpus, 0.95),
                    CpuP99 = CalculatePercentile(cpus, 0.99),

                    RamMin = rams[0],
                    RamMax = rams[count - 1],
                    RamAvg = ramAvg,
                    RamP50 = CalculatePercentile(rams, 0.50),
                    RamP95 = CalculatePercentile(rams, 0.95),
                    RamP99 = CalculatePercentile(rams, 0.99),

                    UptimeDelta = uptimeDelta,
                    LaunchesDelta = launchesDelta,
                    CrashesDelta = crashesDelta,
                    RestartsDelta = restartsDelta,

                    PrimaryGameName = primaryGame,
                    GameSampleCount = gameRecordCount,
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
                int itemCount = items.Count;
                if (itemCount == 0) continue;

                var first = items[0];
                var (winStart, winEnd) = (g.Key.start, g.Key.end);

                int totalSamples = 0;
                bool isPartial = false;

                double cpuMin = double.MaxValue;
                double cpuMax = double.MinValue;
                double cpuWeightedSum = 0;
                double cpuP50Sum = 0;
                double cpuP95Max = double.MinValue;
                double cpuP99Max = double.MinValue;

                double ramMin = double.MaxValue;
                double ramMax = double.MinValue;
                double ramWeightedSum = 0;
                double ramP50Sum = 0;
                double ramP95Max = double.MinValue;
                double ramP99Max = double.MinValue;

                double uptimeDelta = 0;
                int launchesDelta = 0;
                int crashesDelta = 0;
                int restartsDelta = 0;

                Dictionary<string, int>? gameCounts = null;
                int gameSampleCount = 0;

                double gameCpuSum = 0;
                int gameCpuCount = 0;
                double? gameCpuMax = null;

                double gameRamSum = 0;
                int gameRamCount = 0;
                double? gameRamMax = null;

                double? gameDurationMax = null;

                // Single pass through rollup items
                for (int i = 0; i < itemCount; i++)
                {
                    var item = items[i];
                    totalSamples += item.SampleCount;
                    if (item.IsPartial) isPartial = true;

                    if (item.CpuMin < cpuMin) cpuMin = item.CpuMin;
                    if (item.CpuMax > cpuMax) cpuMax = item.CpuMax;
                    cpuWeightedSum += item.CpuAvg * item.SampleCount;
                    cpuP50Sum += item.CpuP50;
                    if (item.CpuP95 > cpuP95Max) cpuP95Max = item.CpuP95;
                    if (item.CpuP99 > cpuP99Max) cpuP99Max = item.CpuP99;

                    if (item.RamMin < ramMin) ramMin = item.RamMin;
                    if (item.RamMax > ramMax) ramMax = item.RamMax;
                    ramWeightedSum += item.RamAvg * item.SampleCount;
                    ramP50Sum += item.RamP50;
                    if (item.RamP95 > ramP95Max) ramP95Max = item.RamP95;
                    if (item.RamP99 > ramP99Max) ramP99Max = item.RamP99;

                    uptimeDelta += item.UptimeDelta;
                    launchesDelta += item.LaunchesDelta;
                    crashesDelta += item.CrashesDelta;
                    restartsDelta += item.RestartsDelta;

                    gameSampleCount += item.GameSampleCount;

                    if (!string.IsNullOrWhiteSpace(item.PrimaryGameName))
                    {
                        gameCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        gameCounts[item.PrimaryGameName] = gameCounts.TryGetValue(item.PrimaryGameName, out int cnt)
                            ? cnt + item.GameSampleCount
                            : item.GameSampleCount;
                    }

                    if (item.GameCpuAvg.HasValue)
                    {
                        gameCpuSum += item.GameCpuAvg.Value;
                        gameCpuCount++;
                    }
                    if (item.GameCpuMax.HasValue)
                    {
                        if (!gameCpuMax.HasValue || item.GameCpuMax.Value > gameCpuMax.Value)
                            gameCpuMax = item.GameCpuMax.Value;
                    }

                    if (item.GameRamAvg.HasValue)
                    {
                        gameRamSum += item.GameRamAvg.Value;
                        gameRamCount++;
                    }
                    if (item.GameRamMax.HasValue)
                    {
                        if (!gameRamMax.HasValue || item.GameRamMax.Value > gameRamMax.Value)
                            gameRamMax = item.GameRamMax.Value;
                    }

                    if (item.GameDurationMax.HasValue)
                    {
                        if (!gameDurationMax.HasValue || item.GameDurationMax.Value > gameDurationMax.Value)
                            gameDurationMax = item.GameDurationMax.Value;
                    }
                }

                string? primaryGame = null;
                if (gameCounts != null && gameCounts.Count > 0)
                {
                    int maxCount = -1;
                    foreach (var kvp in gameCounts)
                    {
                        if (kvp.Value > maxCount)
                        {
                            maxCount = kvp.Value;
                            primaryGame = kvp.Key;
                        }
                    }
                }

                double cpuAvg = totalSamples > 0 ? cpuWeightedSum / totalSamples : 0;
                double cpuP50 = itemCount > 0 ? cpuP50Sum / itemCount : 0;

                double ramAvg = totalSamples > 0 ? ramWeightedSum / totalSamples : 0;
                double ramP50 = itemCount > 0 ? ramP50Sum / itemCount : 0;

                double? gameCpuAvg = gameCpuCount > 0 ? gameCpuSum / gameCpuCount : null;
                double? gameRamAvg = gameRamCount > 0 ? gameRamSum / gameRamCount : null;

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
                    CpuP95 = cpuP95Max,
                    CpuP99 = cpuP99Max,

                    RamMin = ramMin,
                    RamMax = ramMax,
                    RamAvg = ramAvg,
                    RamP50 = ramP50,
                    RamP95 = ramP95Max,
                    RamP99 = ramP99Max,

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
