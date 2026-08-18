using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public interface IFinancialAccountService
    {
        Task<Result<GamerAccount>> GetAccountByGamerIdAsync(Guid gamerEntityId, CancellationToken cancellationToken = default);
        Task<Result<GamerAccount>> GetAccountByIdAsync(Guid accountId, CancellationToken cancellationToken = default);
        Task<Result<Money>> GetBalanceAsync(Guid gamerEntityId, CancellationToken cancellationToken = default);
        Task<Result<LedgerEntry>> CreditAccountAsync(
            Guid accountId,
            Money amount,
            string reference,
            string entryType = "CREDIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null,
            CancellationToken cancellationToken = default);

        Task<Result<LedgerEntry>> DebitAccountAsync(
            Guid accountId,
            Money amount,
            string reference,
            string entryType = "DEBIT",
            string? correlationId = null,
            string? actor = null,
            string? description = null,
            CancellationToken cancellationToken = default);

        Task<Result<IReadOnlyList<LedgerEntry>>> GetLedgerAsync(
            Guid accountId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);
    }
}
