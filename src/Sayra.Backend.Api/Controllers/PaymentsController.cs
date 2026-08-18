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
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly ICommandHandler<CreatePaymentCommand, PaymentResponseDto> _createPaymentHandler;
        private readonly IQueryHandler<GetPaymentQuery, PaymentResponseDto> _getPaymentHandler;

        public PaymentsController(
            ICommandHandler<CreatePaymentCommand, PaymentResponseDto> createPaymentHandler,
            IQueryHandler<GetPaymentQuery, PaymentResponseDto> getPaymentHandler)
        {
            _createPaymentHandler = createPaymentHandler ?? throw new ArgumentNullException(nameof(createPaymentHandler));
            _getPaymentHandler = getPaymentHandler ?? throw new ArgumentNullException(nameof(getPaymentHandler));
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentAsync([FromBody] CreatePaymentRequestDto request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = "Payment request body cannot be null." });
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKeyHeader))
            {
                request.IdempotencyKey = idempotencyKeyHeader.Trim();
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return BadRequest(new { code = "INVALID_IDEMPOTENCY_KEY", message = "Idempotency-Key header or property is required for payments." });
            }

            var command = new CreatePaymentCommand { Request = request };
            var result = await _createPaymentHandler.HandleAsync(command, cancellationToken);

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

                return BadRequest(new { code = result.ErrorCode ?? "PAYMENT_FAILED", message = result.ErrorMessage });
            }

            return CreatedAtAction("GetPaymentById", new { id = result.Value!.Id }, result.Value!);
        }

        [HttpGet("{id:guid}", Name = "GetPaymentById")]
        public async Task<IActionResult> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var query = new GetPaymentQuery { PaymentId = id };
            var result = await _getPaymentHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_PAYMENT_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }
}
