using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Telemetry
{
    public interface ITelemetryIngestionService
    {
        Task<TelemetryIngestionResult> IngestTelemetrySnapshotAsync(
            TelemetryConnectionContext connectionContext,
            TelemetryModel model,
            CancellationToken cancellationToken = default);

        Task<TelemetryIngestionResult> IngestOperationalEventAsync(
            TelemetryConnectionContext connectionContext,
            ClientEventEnvelopeDto eventDto,
            CancellationToken cancellationToken = default);

        Task<TelemetryIngestionResult> IngestHeartbeatAsync(
            TelemetryConnectionContext connectionContext,
            HeartbeatMessage heartbeat,
            CancellationToken cancellationToken = default);
    }
}
