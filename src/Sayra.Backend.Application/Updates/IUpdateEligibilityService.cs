using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public interface IUpdateEligibilityService
    {
        Task<UpdateEligibilityResult> EvaluateEligibilityAsync(
            ClientEvaluationContext context,
            CancellationToken cancellationToken = default);

        Task<UpdateEligibilityResult> EvaluateWorkstationEligibilityAsync(
            Guid workstationId,
            string? reportedVersion = null,
            string? osVersion = null,
            string? architecture = null,
            CancellationToken cancellationToken = default);

        int CalculateRolloutBucket(string pcId, Guid releaseId);
    }
}
