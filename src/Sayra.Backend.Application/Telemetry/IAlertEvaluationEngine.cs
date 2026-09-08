using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IAlertEvaluationEngine
    {
        Task<IReadOnlyList<Incident>> EvaluateHealthResultAsync(WorkstationHealthEvaluationResult healthResult, CancellationToken cancellationToken = default);
        Task<Incident?> EvaluateOperationalEventAsync(OperationalEventSignal eventSignal, CancellationToken cancellationToken = default);
        string CalculateFingerprint(Guid organizationId, Guid? siteId, string pcId, string ruleCode, string? resource = null);
    }
}
