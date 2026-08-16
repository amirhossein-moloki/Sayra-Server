using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Pricing;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/pricing")]
    public class PricingController : ControllerBase
    {
        private readonly ICommandHandler<CreatePricingPlanCommand, PricingPlanResponseDto> _createPlanHandler;
        private readonly ICommandHandler<CreatePricingRuleCommand, PricingRuleResponseDto> _createRuleHandler;
        private readonly ICommandHandler<ActivatePricingPlanCommand, PricingPlanResponseDto> _activatePlanHandler;
        private readonly ICommandHandler<DeactivatePricingPlanCommand, PricingPlanResponseDto> _deactivatePlanHandler;
        private readonly IQueryHandler<GetPricingPlanQuery, PricingPlanResponseDto> _getPlanHandler;
        private readonly IQueryHandler<GetPricingRulesQuery, List<PricingRuleResponseDto>> _getRulesHandler;
        private readonly IQueryHandler<ResolveRateQuery, ResolvedRateResponseDto> _resolveRateHandler;

        public PricingController(
            ICommandHandler<CreatePricingPlanCommand, PricingPlanResponseDto> createPlanHandler,
            ICommandHandler<CreatePricingRuleCommand, PricingRuleResponseDto> createRuleHandler,
            ICommandHandler<ActivatePricingPlanCommand, PricingPlanResponseDto> activatePlanHandler,
            ICommandHandler<DeactivatePricingPlanCommand, PricingPlanResponseDto> deactivatePlanHandler,
            IQueryHandler<GetPricingPlanQuery, PricingPlanResponseDto> getPlanHandler,
            IQueryHandler<GetPricingRulesQuery, List<PricingRuleResponseDto>> getRulesHandler,
            IQueryHandler<ResolveRateQuery, ResolvedRateResponseDto> resolveRateHandler)
        {
            _createPlanHandler = createPlanHandler ?? throw new ArgumentNullException(nameof(createPlanHandler));
            _createRuleHandler = createRuleHandler ?? throw new ArgumentNullException(nameof(createRuleHandler));
            _activatePlanHandler = activatePlanHandler ?? throw new ArgumentNullException(nameof(activatePlanHandler));
            _deactivatePlanHandler = deactivatePlanHandler ?? throw new ArgumentNullException(nameof(deactivatePlanHandler));
            _getPlanHandler = getPlanHandler ?? throw new ArgumentNullException(nameof(getPlanHandler));
            _getRulesHandler = getRulesHandler ?? throw new ArgumentNullException(nameof(getRulesHandler));
            _resolveRateHandler = resolveRateHandler ?? throw new ArgumentNullException(nameof(resolveRateHandler));
        }

        [HttpPost("plans")]
        public async Task<IActionResult> CreatePlanAsync([FromBody] CreatePricingPlanRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreatePricingPlanCommand(request.SiteId, request.Name, request.Currency);
            var result = await _createPlanHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_PRICING_PLAN")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_PLAN_FAILED", message = result.ErrorMessage });
            }

            var response = result.Value!;
            return Created($"/api/pricing/plans/{response.PricingPlanId}", response);
        }

        [HttpPost("plans/{id:guid}/rules")]
        public async Task<IActionResult> CreateRuleAsync(Guid id, [FromBody] CreatePricingRuleRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreatePricingRuleCommand(
                id,
                request.Name,
                request.RateAmount,
                request.Currency,
                request.Priority,
                request.WorkstationId,
                request.ZoneId,
                request.GamerType,
                request.DayOfWeek,
                request.StartTime,
                request.EndTime,
                request.IsPeak);

            var result = await _createRuleHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PRICING_PLAN_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "DUPLICATE_PRIORITY")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_RULE_FAILED", message = result.ErrorMessage });
            }

            var response = result.Value!;
            return Created($"/api/pricing/plans/{id}/rules", response);
        }

        [HttpPost("plans/{id:guid}/activate")]
        public async Task<IActionResult> ActivatePlanAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new ActivatePricingPlanCommand(id);
            var result = await _activatePlanHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PRICING_PLAN_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "ACTIVATE_PLAN_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("plans/{id:guid}/deactivate")]
        public async Task<IActionResult> DeactivatePlanAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new DeactivatePricingPlanCommand(id);
            var result = await _deactivatePlanHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PRICING_PLAN_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "DEACTIVATE_PLAN_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("plans/{id:guid}")]
        public async Task<IActionResult> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetPricingPlanQuery(id);
            var result = await _getPlanHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "PRICING_PLAN_NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("plans/{id:guid}/rules")]
        public async Task<IActionResult> GetRulesAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetPricingRulesQuery(id);
            var result = await _getRulesHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "PRICING_PLAN_NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("resolve")]
        public async Task<IActionResult> ResolveRateAsync(
            [FromQuery] Guid siteId,
            [FromQuery] Guid? zoneId,
            [FromQuery] Guid? workstationId,
            [FromQuery] Guid? gamerId,
            [FromQuery] string? gamerType,
            [FromQuery] DateTime? timestamp,
            CancellationToken cancellationToken)
        {
            var query = new ResolveRateQuery(siteId, zoneId, workstationId, gamerId, gamerType, timestamp);
            var result = await _resolveRateHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PRICING_PLAN_NOT_FOUND" || result.ErrorCode == "NO_MATCHING_RULE")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "RESOLVE_RATE_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }
}
