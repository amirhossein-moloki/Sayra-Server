// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
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

    public class ProcessFinancialTransactionCommand : ICommand<FinancialTransactionResponseDto>
    {
        public ProcessTransactionRequestDto Request { get; set; } = null!;
    }

    public class ReverseFinancialTransactionCommand : ICommand<FinancialTransactionResponseDto>
    {
        public ReverseTransactionRequestDto Request { get; set; } = null!;
    }

    public class CreatePaymentCommand : ICommand<PaymentResponseDto>
    {
        public CreatePaymentRequestDto Request { get; set; } = null!;
    }

    public class GetFinancialTransactionQuery : IQuery<FinancialTransactionResponseDto>
    {
        public Guid TransactionId { get; set; }
    }

    public class GetPaymentQuery : IQuery<PaymentResponseDto>
    {
        public Guid PaymentId { get; set; }
    }

    public class GetTransactionByIdempotencyKeyQuery : IQuery<FinancialTransactionResponseDto>
    {
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
