using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Financial;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/accounts")]
    public class AccountsController : ControllerBase
    {
        private readonly IQueryHandler<GetAccountBalanceQuery, AccountBalanceResponseDto> _getBalanceHandler;
        private readonly IQueryHandler<GetAccountLedgerQuery, IReadOnlyList<LedgerEntryResponseDto>> _getLedgerHandler;

        public AccountsController(
            IQueryHandler<GetAccountBalanceQuery, AccountBalanceResponseDto> getBalanceHandler,
            IQueryHandler<GetAccountLedgerQuery, IReadOnlyList<LedgerEntryResponseDto>> getLedgerHandler)
        {
            _getBalanceHandler = getBalanceHandler ?? throw new ArgumentNullException(nameof(getBalanceHandler));
            _getLedgerHandler = getLedgerHandler ?? throw new ArgumentNullException(nameof(getLedgerHandler));
        }

        [HttpGet("{gamerId:guid}/balance")]
        public async Task<IActionResult> GetBalanceAsync(Guid gamerId, CancellationToken cancellationToken)
        {
            var query = new GetAccountBalanceQuery { GamerEntityId = gamerId };
            var result = await _getBalanceHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_BALANCE_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("{gamerId:guid}/ledger")]
        public async Task<IActionResult> GetLedgerAsync(Guid gamerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        {
            var query = new GetAccountLedgerQuery
            {
                GamerEntityId = gamerId,
                Page = page,
                PageSize = pageSize
            };

            var result = await _getLedgerHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_LEDGER_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }
    }
}
