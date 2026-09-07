using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain.Telemetry;

namespace Sayra.Backend.Application.Telemetry
{
    public interface IWorkstationHealthEvaluator
    {
        Task<WorkstationHealthEvaluationResult> EvaluateWorkstationHealthAsync(
            WorkstationRealTimeState? currentState,
            WorkstationHealthEvaluationResult? previousResult = null,
            WorkstationHealthPolicyOptions? customPolicy = null,
            CancellationToken cancellationToken = default);
    }
}
