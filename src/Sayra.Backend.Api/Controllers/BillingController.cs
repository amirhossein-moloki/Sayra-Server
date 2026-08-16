using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Billing;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    public class BillingController : ControllerBase
    {
        private readonly ICommandHandler<CalculateSessionBillingCommand, BillingResultResponseDto> _calculateBillingHandler;
        private readonly IQueryHandler<GetBillingResultQuery, BillingResultResponseDto> _getBillingResultHandler;
        private readonly IQueryHandler<GetSessionBillingHistoryQuery, List<BillingResultResponseDto>> _getHistoryHandler;
        private readonly IQueryHandler<GetLatestSessionBillingQuery, BillingResultResponseDto> _getLatestHandler;

        public BillingController(
            ICommandHandler<CalculateSessionBillingCommand, BillingResultResponseDto> calculateBillingHandler,
            IQueryHandler<GetBillingResultQuery, BillingResultResponseDto> getBillingResultHandler,
            IQueryHandler<GetSessionBillingHistoryQuery, List<BillingResultResponseDto>> getHistoryHandler,
            IQueryHandler<GetLatestSessionBillingQuery, BillingResultResponseDto> getLatestHandler)
        {
            _calculateBillingHandler = calculateBillingHandler ?? throw new ArgumentNullException(nameof(calculateBillingHandler));
            _getBillingResultHandler = getBillingResultHandler ?? throw new ArgumentNullException(nameof(getBillingResultHandler));
            _getHistoryHandler = getHistoryHandler ?? throw new ArgumentNullException(nameof(getHistoryHandler));
            _getLatestHandler = getLatestHandler ?? throw new ArgumentNullException(nameof(getLatestHandler));
        }

        [HttpGet("api/billing/{id:guid}")]
        public async Task<IActionResult> GetBillingResultByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetBillingResultQuery(id);
            var result = await _getBillingResultHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("api/sessions/{id:guid}/billing/calculate")]
        public async Task<IActionResult> CalculateSessionBillingAsync(
            Guid id,
            [FromBody] CalculateSessionBillingRequestDto? request,
            CancellationToken cancellationToken)
        {
            var command = new CalculateSessionBillingCommand(
                id,
                request?.DiscountAmount,
                request?.AdjustmentAmount);

            var result = await _calculateBillingHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "SESSION_NOT_FOUND" || result.ErrorCode == "RATE_SNAPSHOT_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CALCULATE_BILLING_FAILED", message = result.ErrorMessage });
            }

            var response = result.Value!;
            return Created($"/api/billing/{response.BillingResultId}", response);
        }

        [HttpGet("api/sessions/{id:guid}/billing/history")]
        public async Task<IActionResult> GetSessionBillingHistoryAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetSessionBillingHistoryQuery(id);
            var result = await _getHistoryHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "GET_HISTORY_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("api/sessions/{id:guid}/billing")]
        public async Task<IActionResult> GetLatestSessionBillingAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetLatestSessionBillingQuery(id);
            var result = await _getLatestHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }
}
