using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Telemetry;
using Sayra.Backend.Domain.ValueObjects;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IWorkstationStateStore
    {
        Task<WorkstationRealTimeState?> GetStateAsync(string pcId, CancellationToken cancellationToken = default);

        Task<WorkstationRealTimeState?> GetStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default);

        Task SaveStateAsync(WorkstationRealTimeState state, CancellationToken cancellationToken = default);

        Task UpdateFromTelemetryAsync(
            WorkstationIdentity identity,
            TelemetrySnapshot snapshot,
            string? connectionId = null,
            CancellationToken cancellationToken = default);

        Task UpdateFromHeartbeatAsync(
            WorkstationIdentity identity,
            HeartbeatSignal heartbeat,
            string? connectionId = null,
            CancellationToken cancellationToken = default);

        Task UpdateFromOperationalEventAsync(
            WorkstationIdentity identity,
            OperationalEventSignal eventSignal,
            CancellationToken cancellationToken = default);

        Task UpdateConnectionStateAsync(
            WorkstationIdentity identity,
            string connectionId,
            bool isConnected,
            string? connectionState,
            CancellationToken cancellationToken = default);
    }
}
