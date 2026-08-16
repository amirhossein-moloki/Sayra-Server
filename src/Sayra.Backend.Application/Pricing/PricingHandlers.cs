using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Pricing
{
    public class CreatePricingPlanCommandHandler : ICommandHandler<CreatePricingPlanCommand, PricingPlanResponseDto>
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<Site> _siteRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreatePricingPlanCommandHandler(
            IRepository<PricingPlan> planRepository,
            IRepository<Site> siteRepository,
            IUnitOfWork unitOfWork)
        {
            _planRepository = planRepository;
            _siteRepository = siteRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<PricingPlanResponseDto>> HandleAsync(CreatePricingPlanCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                if (command.SiteId == Guid.Empty)
                {
                    return Result.Failure<PricingPlanResponseDto>("INVALID_SITE_ID", "SiteId is required.");
                }

                var site = await _siteRepository.GetByIdAsync(command.SiteId, track: false, cancellationToken: cancellationToken);
                if (site == null)
                {
                    return Result.Failure<PricingPlanResponseDto>("SITE_NOT_FOUND", $"Site with ID {command.SiteId} not found.");
                }

                var nameTrimmed = (command.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nameTrimmed))
                {
                    return Result.Failure<PricingPlanResponseDto>("INVALID_PLAN_NAME", "Pricing plan name is required.");
                }

                var existingPlans = await _planRepository.FindAsync(
                    p => p.SiteId == command.SiteId && p.Name.ToLower() == nameTrimmed.ToLower(),
                    track: false,
                    cancellationToken: cancellationToken);

                if (existingPlans.Any())
                {
                    return Result.Failure<PricingPlanResponseDto>("DUPLICATE_PRICING_PLAN", $"A pricing plan with name '{nameTrimmed}' already exists for site {command.SiteId}.");
                }

                var plan = new PricingPlan
                {
                    SiteId = command.SiteId,
                    Name = nameTrimmed,
                    Currency = string.IsNullOrWhiteSpace(command.Currency) ? "SAY" : command.Currency.Trim().ToUpperInvariant(),
                    Status = "Inactive",
                    CreatedAt = DateTime.UtcNow
                };

                plan.NormalizeAndValidate();

                await _planRepository.AddAsync(plan, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(MapToDto(plan, new List<PricingRule>()));
            }
            catch (InvalidDomainException ex)
            {
                return Result.Failure<PricingPlanResponseDto>(ex.ErrorCode, ex.Message);
            }
        }

        internal static PricingPlanResponseDto MapToDto(PricingPlan plan, IEnumerable<PricingRule> rules)
        {
            return new PricingPlanResponseDto
            {
                PricingPlanId = plan.PricingPlanId,
                SiteId = plan.SiteId,
                Name = plan.Name,
                Status = plan.Status,
                Currency = plan.Currency,
                CreatedAt = plan.CreatedAt,
                UpdatedAt = plan.UpdatedAt,
                Rules = rules.OrderBy(r => r.Priority).Select(MapRuleToDto).ToList()
            };
        }

        internal static PricingRuleResponseDto MapRuleToDto(PricingRule rule)
        {
            return new PricingRuleResponseDto
            {
                PricingRuleId = rule.PricingRuleId,
                PricingPlanId = rule.PricingPlanId,
                Name = rule.Name,
                RateAmount = rule.RateAmount,
                Currency = rule.Currency,
                Priority = rule.Priority,
                WorkstationId = rule.WorkstationId,
                ZoneId = rule.ZoneId,
                GamerType = rule.GamerType,
                DayOfWeek = rule.DayOfWeek,
                StartTime = rule.StartTime,
                EndTime = rule.EndTime,
                IsPeak = rule.IsPeak,
                CreatedAt = rule.CreatedAt,
                UpdatedAt = rule.UpdatedAt
            };
        }
    }

    public class CreatePricingRuleCommandHandler : ICommandHandler<CreatePricingRuleCommand, PricingRuleResponseDto>
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<PricingRule> _ruleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreatePricingRuleCommandHandler(
            IRepository<PricingPlan> planRepository,
            IRepository<PricingRule> ruleRepository,
            IUnitOfWork unitOfWork)
        {
            _planRepository = planRepository;
            _ruleRepository = ruleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<PricingRuleResponseDto>> HandleAsync(CreatePricingRuleCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var plan = await _planRepository.GetByIdAsync(command.PricingPlanId, track: false, cancellationToken: cancellationToken);
                if (plan == null)
                {
                    return Result.Failure<PricingRuleResponseDto>("PRICING_PLAN_NOT_FOUND", $"Pricing plan with ID {command.PricingPlanId} not found.");
                }

                var existingRules = await _ruleRepository.FindAsync(
                    r => r.PricingPlanId == command.PricingPlanId && r.Priority == command.Priority,
                    track: false,
                    cancellationToken: cancellationToken);

                if (existingRules.Any())
                {
                    return Result.Failure<PricingRuleResponseDto>("DUPLICATE_PRIORITY", $"A rule with priority {command.Priority} already exists in plan {command.PricingPlanId}.");
                }

                var rule = new PricingRule
                {
                    PricingPlanId = command.PricingPlanId,
                    Name = (command.Name ?? string.Empty).Trim(),
                    RateAmount = command.RateAmount,
                    Currency = string.IsNullOrWhiteSpace(command.Currency) ? plan.Currency : command.Currency.Trim().ToUpperInvariant(),
                    Priority = command.Priority,
                    WorkstationId = command.WorkstationId,
                    ZoneId = command.ZoneId,
                    GamerType = string.IsNullOrWhiteSpace(command.GamerType) ? null : command.GamerType.Trim().ToUpperInvariant(),
                    DayOfWeek = command.DayOfWeek,
                    StartTime = command.StartTime,
                    EndTime = command.EndTime,
                    IsPeak = command.IsPeak,
                    CreatedAt = DateTime.UtcNow
                };

                rule.NormalizeAndValidate();

                await _ruleRepository.AddAsync(rule, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(CreatePricingPlanCommandHandler.MapRuleToDto(rule));
            }
            catch (InvalidDomainException ex)
            {
                return Result.Failure<PricingRuleResponseDto>(ex.ErrorCode, ex.Message);
            }
        }
    }

    public class ActivatePricingPlanCommandHandler : ICommandHandler<ActivatePricingPlanCommand, PricingPlanResponseDto>
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<PricingRule> _ruleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ActivatePricingPlanCommandHandler(
            IRepository<PricingPlan> planRepository,
            IRepository<PricingRule> ruleRepository,
            IUnitOfWork unitOfWork)
        {
            _planRepository = planRepository;
            _ruleRepository = ruleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<PricingPlanResponseDto>> HandleAsync(ActivatePricingPlanCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var plan = await _planRepository.GetByIdAsync(command.PricingPlanId, track: true, cancellationToken: cancellationToken);
                if (plan == null)
                {
                    return Result.Failure<PricingPlanResponseDto>("PRICING_PLAN_NOT_FOUND", $"Pricing plan with ID {command.PricingPlanId} not found.");
                }

                plan.Activate();
                _planRepository.Update(plan);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var rules = await _ruleRepository.FindAsync(r => r.PricingPlanId == plan.PricingPlanId, track: false, cancellationToken: cancellationToken);
                return Result.Success(CreatePricingPlanCommandHandler.MapToDto(plan, rules));
            }
            catch (InvalidDomainException ex)
            {
                return Result.Failure<PricingPlanResponseDto>(ex.ErrorCode, ex.Message);
            }
        }
    }

    public class DeactivatePricingPlanCommandHandler : ICommandHandler<DeactivatePricingPlanCommand, PricingPlanResponseDto>
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<PricingRule> _ruleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DeactivatePricingPlanCommandHandler(
            IRepository<PricingPlan> planRepository,
            IRepository<PricingRule> ruleRepository,
            IUnitOfWork unitOfWork)
        {
            _planRepository = planRepository;
            _ruleRepository = ruleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<PricingPlanResponseDto>> HandleAsync(DeactivatePricingPlanCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var plan = await _planRepository.GetByIdAsync(command.PricingPlanId, track: true, cancellationToken: cancellationToken);
                if (plan == null)
                {
                    return Result.Failure<PricingPlanResponseDto>("PRICING_PLAN_NOT_FOUND", $"Pricing plan with ID {command.PricingPlanId} not found.");
                }

                plan.Deactivate();
                _planRepository.Update(plan);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var rules = await _ruleRepository.FindAsync(r => r.PricingPlanId == plan.PricingPlanId, track: false, cancellationToken: cancellationToken);
                return Result.Success(CreatePricingPlanCommandHandler.MapToDto(plan, rules));
            }
            catch (InvalidDomainException ex)
            {
                return Result.Failure<PricingPlanResponseDto>(ex.ErrorCode, ex.Message);
            }
        }
    }

    public class GetPricingPlanQueryHandler : IQueryHandler<GetPricingPlanQuery, PricingPlanResponseDto>
    {
        private readonly IRepository<PricingPlan> _planRepository;
        private readonly IRepository<PricingRule> _ruleRepository;

        public GetPricingPlanQueryHandler(
            IRepository<PricingPlan> planRepository,
            IRepository<PricingRule> ruleRepository)
        {
            _planRepository = planRepository;
            _ruleRepository = ruleRepository;
        }

        public async Task<Result<PricingPlanResponseDto>> HandleAsync(GetPricingPlanQuery query, CancellationToken cancellationToken = default)
        {
            var plan = await _planRepository.GetByIdAsync(query.PricingPlanId, track: false, cancellationToken: cancellationToken);
            if (plan == null)
            {
                return Result.Failure<PricingPlanResponseDto>("PRICING_PLAN_NOT_FOUND", $"Pricing plan with ID {query.PricingPlanId} not found.");
            }

            var rules = await _ruleRepository.FindAsync(r => r.PricingPlanId == plan.PricingPlanId, track: false, cancellationToken: cancellationToken);
            return Result.Success(CreatePricingPlanCommandHandler.MapToDto(plan, rules));
        }
    }

    public class GetPricingRulesQueryHandler : IQueryHandler<GetPricingRulesQuery, List<PricingRuleResponseDto>>
    {
        private readonly IRepository<PricingRule> _ruleRepository;

        public GetPricingRulesQueryHandler(IRepository<PricingRule> ruleRepository)
        {
            _ruleRepository = ruleRepository;
        }

        public async Task<Result<List<PricingRuleResponseDto>>> HandleAsync(GetPricingRulesQuery query, CancellationToken cancellationToken = default)
        {
            var rules = await _ruleRepository.FindAsync(r => r.PricingPlanId == query.PricingPlanId, track: false, cancellationToken: cancellationToken);
            var resultList = rules.OrderBy(r => r.Priority)
                                  .Select(CreatePricingPlanCommandHandler.MapRuleToDto)
                                  .ToList();
            return Result.Success(resultList);
        }
    }

    public class ResolveRateQueryHandler : IQueryHandler<ResolveRateQuery, ResolvedRateResponseDto>
    {
        private readonly IRateResolver _rateResolver;

        public ResolveRateQueryHandler(IRateResolver rateResolver)
        {
            _rateResolver = rateResolver;
        }

        public async Task<Result<ResolvedRateResponseDto>> HandleAsync(ResolveRateQuery query, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new ResolveRateRequestDto
                {
                    SiteId = query.SiteId,
                    ZoneId = query.ZoneId,
                    WorkstationId = query.WorkstationId,
                    GamerId = query.GamerId,
                    GamerType = query.GamerType,
                    Timestamp = query.Timestamp
                };

                var resolved = await _rateResolver.ResolveRateAsync(request, cancellationToken);
                return Result.Success(resolved);
            }
            catch (InvalidDomainException ex)
            {
                return Result.Failure<ResolvedRateResponseDto>(ex.ErrorCode, ex.Message);
            }
        }
    }
}
