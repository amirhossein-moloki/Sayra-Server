// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Financial;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/transactions")]
    public class TransactionsController : ControllerBase
    {
        private readonly ICommandHandler<ProcessFinancialTransactionCommand, FinancialTransactionResponseDto> _processTransactionHandler;
        private readonly ICommandHandler<ReverseFinancialTransactionCommand, FinancialTransactionResponseDto> _reverseTransactionHandler;
        private readonly IQueryHandler<GetFinancialTransactionQuery, FinancialTransactionResponseDto> _getTransactionHandler;
        private readonly IQueryHandler<GetTransactionByIdempotencyKeyQuery, FinancialTransactionResponseDto> _getByIdempotencyKeyHandler;

        public TransactionsController(
            ICommandHandler<ProcessFinancialTransactionCommand, FinancialTransactionResponseDto> processTransactionHandler,
            ICommandHandler<ReverseFinancialTransactionCommand, FinancialTransactionResponseDto> reverseTransactionHandler,
            IQueryHandler<GetFinancialTransactionQuery, FinancialTransactionResponseDto> getTransactionHandler,
            IQueryHandler<GetTransactionByIdempotencyKeyQuery, FinancialTransactionResponseDto> getByIdempotencyKeyHandler)
        {
            _processTransactionHandler = processTransactionHandler ?? throw new ArgumentNullException(nameof(processTransactionHandler));
            _reverseTransactionHandler = reverseTransactionHandler ?? throw new ArgumentNullException(nameof(reverseTransactionHandler));
            _getTransactionHandler = getTransactionHandler ?? throw new ArgumentNullException(nameof(getTransactionHandler));
            _getByIdempotencyKeyHandler = getByIdempotencyKeyHandler ?? throw new ArgumentNullException(nameof(getByIdempotencyKeyHandler));
        }

        [HttpPost]
        public async Task<IActionResult> ProcessTransactionAsync([FromBody] ProcessTransactionRequestDto request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Transaction request body cannot be null." });
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKeyHeader))
            {
                request.IdempotencyKey = idempotencyKeyHeader.Trim();
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return BadRequest(new { code = "INVALID_IDEMPOTENCY_KEY", message = "Idempotency-Key header or property is required." });
            }

            var command = new ProcessFinancialTransactionCommand { Request = request };
            var result = await _processTransactionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "IDEMPOTENCY_CONFLICT")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "INSUFFICIENT_BALANCE" || result.ErrorCode == "ACCOUNT_DISABLED")
                {
                    return UnprocessableEntity(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "TRANSACTION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetTransactionByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var query = new GetFinancialTransactionQuery { TransactionId = id };
            var result = await _getTransactionHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_TRANSACTION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("idempotency/{key}")]
        public async Task<IActionResult> GetTransactionByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            var query = new GetTransactionByIdempotencyKeyQuery { IdempotencyKey = key };
            var result = await _getByIdempotencyKeyHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_TRANSACTION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpPost("{id:guid}/reverse")]
        public async Task<IActionResult> ReverseTransactionAsync(Guid id, [FromBody] ReverseTransactionRequestDto request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                request = new ReverseTransactionRequestDto();
            }

            request.OriginalTransactionId = id;

            if (!string.IsNullOrWhiteSpace(idempotencyKeyHeader))
            {
                request.IdempotencyKey = idempotencyKeyHeader.Trim();
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return BadRequest(new { code = "INVALID_IDEMPOTENCY_KEY", message = "Idempotency-Key header or property is required for reversal." });
            }

            var command = new ReverseFinancialTransactionCommand { Request = request };
            var result = await _reverseTransactionHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_REVERSAL" || result.ErrorCode == "IDEMPOTENCY_CONFLICT")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "INVALID_STATE_TRANSITION")
                {
                    return UnprocessableEntity(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "REVERSAL_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }
}
