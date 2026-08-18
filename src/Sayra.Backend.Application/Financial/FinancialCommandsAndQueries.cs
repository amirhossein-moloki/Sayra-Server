using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public class CreditAccountCommand : ICommand<LedgerEntryResponseDto>
    {
        public Guid GamerAccountId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Reference { get; set; } = string.Empty;
        public string EntryType { get; set; } = "DEPOSIT";
        public string? CorrelationId { get; set; }
        public string? Actor { get; set; }
        public string? Description { get; set; }
    }

    public class DebitAccountCommand : ICommand<LedgerEntryResponseDto>
    {
        public Guid GamerAccountId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAY";
        public string Reference { get; set; } = string.Empty;
        public string EntryType { get; set; } = "WITHDRAWAL";
        public string? CorrelationId { get; set; }
        public string? Actor { get; set; }
        public string? Description { get; set; }
    }

    public class GetAccountBalanceQuery : IQuery<AccountBalanceResponseDto>
    {
        public Guid GamerEntityId { get; set; }
    }

    public class GetAccountLedgerQuery : IQuery<IReadOnlyList<LedgerEntryResponseDto>>
    {
        public Guid GamerEntityId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
