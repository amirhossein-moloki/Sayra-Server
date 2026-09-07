using System;
using System.Collections.Generic;

using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Telemetry
{
    public interface ITelemetryAggregationService
    {
        (DateTime windowStart, DateTime windowEnd) GetWindowBoundaries(DateTime timestamp, string granularity);

        IReadOnlyList<TelemetryAggregateRecord> AggregateRawRecords(
            IEnumerable<TelemetryHistoryRecord> rawRecords,
            string granularity);

        IReadOnlyList<TelemetryAggregateRecord> RollupAggregates(
            IEnumerable<TelemetryAggregateRecord> sourceAggregates,
            string targetGranularity);
    }
}
