using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public record FleetHealthSummary(
        int TotalTrackedWorkstations,
        int HealthyCount,
        int WarningCount,
        int DegradedCount,
        int CriticalCount,
        int UnknownCount,
        int OfflineCount,
        double AverageHealthScore,
        DateTime EvaluatedAtUtc);

    public interface IWorkstationHealthStore
    {
        Task SaveHealthResultAsync(
            WorkstationHealthEvaluationResult result,
            CancellationToken cancellationToken = default);

        Task<WorkstationHealthEvaluationResult?> GetHealthResultAsync(
            string pcId,
            CancellationToken cancellationToken = default);

        Task<WorkstationHealthEvaluationResult?> GetHealthResultByWorkstationIdAsync(
            Guid workstationId,
            CancellationToken cancellationToken = default);
    }

    public interface IWorkstationHealthReader
    {
        Task<IReadOnlyList<WorkstationHealthEvaluationResult>> GetFleetHealthResultsAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default);

        Task<FleetHealthSummary> GetFleetHealthSummaryAsync(
            Guid? siteId = null,
            Guid? organizationId = null,
            CancellationToken cancellationToken = default);
    }
}
