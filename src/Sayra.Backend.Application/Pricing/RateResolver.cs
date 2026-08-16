using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Application.Pricing
{
    public class RateResolver : IRateResolver
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<PricingRule> _ruleRepository;
        private readonly IRepository<Workstation> _workstationRepository;

        public RateResolver(
            IRepository<PricingPlan> planRepository,
            IRepository<PricingRule> ruleRepository,
            IRepository<Workstation> workstationRepository)
        {
            _planRepository = planRepository;
            _ruleRepository = ruleRepository;
            _workstationRepository = workstationRepository;
        }

        public async Task<ResolvedRateResponseDto> ResolveRateAsync(
            ResolveRateRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var siteId = request.SiteId;
            Guid? zoneId = request.ZoneId;
            Guid? workstationId = request.WorkstationId;

            // If SiteId is empty but WorkstationId is provided, infer Site/Zone from Workstation
            if (workstationId.HasValue && workstationId.Value != Guid.Empty)
            {
                var ws = await _workstationRepository.GetByIdAsync(workstationId.Value, track: false, cancellationToken: cancellationToken);
                if (ws != null)
                {
                    if (siteId == Guid.Empty && ws.SiteEntityId.HasValue)
                    {
                        siteId = ws.SiteEntityId.Value;
                    }
                    if (!zoneId.HasValue && ws.ZoneEntityId.HasValue)
                    {
                        zoneId = ws.ZoneEntityId.Value;
                    }
                }
            }

            if (siteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for rate resolution.");
            }

            // Get active pricing plan for site
            var activePlans = await _planRepository.FindAsync(
                p => p.SiteId == siteId && p.Status == "Active",
                track: false,
                cancellationToken: cancellationToken);

            var activePlan = activePlans.OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).FirstOrDefault();

            if (activePlan == null)
            {
                throw new InvalidDomainException("PRICING_PLAN_NOT_FOUND", $"No active pricing plan found for site {siteId}.");
            }

            // Get rules for active plan, ordered by Priority ascending
            var rules = await _ruleRepository.FindAsync(
                r => r.PricingPlanId == activePlan.PricingPlanId,
                track: false,
                cancellationToken: cancellationToken);

            var orderedRules = rules.OrderBy(r => r.Priority).ToList();

            var timestampUtc = request.Timestamp.HasValue
                ? (request.Timestamp.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(request.Timestamp.Value, DateTimeKind.Utc)
                    : request.Timestamp.Value.ToUniversalTime())
                : DateTime.UtcNow;

            var gamerType = request.GamerType;

            foreach (var rule in orderedRules)
            {
                if (rule.Matches(siteId, zoneId, workstationId, gamerType, timestampUtc))
                {
                    return new ResolvedRateResponseDto
                    {
                        PricingPlanId = activePlan.PricingPlanId,
                        PricingRuleId = rule.PricingRuleId,
                        RateAmount = rule.RateAmount,
                        Currency = rule.Currency,
                        Priority = rule.Priority,
                        RuleReference = rule.Name,
                        ResolvedAtUtc = timestampUtc
                    };
                }
            }

            throw new InvalidDomainException("NO_MATCHING_RULE", $"No matching pricing rule found in plan '{activePlan.Name}' for site {siteId}.");
        }
    }
}
