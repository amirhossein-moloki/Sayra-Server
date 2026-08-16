using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Pricing
{
    public interface IRateSnapshotService
    {
        Task<RateSnapshotResponseDto> CreateSnapshotAsync(
            Guid sessionId,
            Guid pricingPlanId,
            Guid? pricingRuleId,
            decimal rateAmount,
            string currency,
            string ruleReference,
            DateTime appliedAtUtc,
            CancellationToken cancellationToken = default);

        Task<RateSnapshotResponseDto?> GetSnapshotBySessionIdAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
    }
}
