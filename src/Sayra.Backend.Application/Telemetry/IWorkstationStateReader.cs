using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public record FleetStateSummary(
        int TotalTrackedWorkstations,
        int ConnectedFresh,
        int ConnectedStale,
        int Disconnected,
        int Offline,
        int Unknown,
        int ActiveSessionsCount,
        DateTime EvaluatedAt);

    public interface IWorkstationStateReader
    {
        Task<WorkstationRealTimeState?> GetCurrentStateAsync(string pcId, CancellationToken cancellationToken = default);

        Task<WorkstationRealTimeState?> GetCurrentStateByWorkstationIdAsync(Guid workstationId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<WorkstationRealTimeState>> GetWorkstationStatesAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default);

        Task<FleetStateSummary> GetFleetSummaryAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default);
    }
}
