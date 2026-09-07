using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sayra.Backend.Application.Telemetry
{
    public interface ITelemetryIdempotencyService
    {
        Task<bool> IsDuplicateEventAsync(string eventId, CancellationToken cancellationToken = default);
        Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);
        Task<bool> IsStaleTelemetryAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default);
        Task RecordLatestTelemetryTimestampAsync(string pcId, DateTime clientTimestamp, CancellationToken cancellationToken = default);
    }
}
