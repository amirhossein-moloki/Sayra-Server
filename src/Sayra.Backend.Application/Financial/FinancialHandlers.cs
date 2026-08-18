// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Financial
{
    public class CreditAccountCommandHandler : ICommandHandler<CreditAccountCommand, LedgerEntryResponseDto>
    {
        private readonly IFinancialAccountService _financialAccountService;

        public CreditAccountCommandHandler(IFinancialAccountService financialAccountService)
        {
            _financialAccountService = financialAccountService ?? throw new ArgumentNullException(nameof(financialAccountService));
        }

        public async Task<Result<LedgerEntryResponseDto>> HandleAsync(CreditAccountCommand command, CancellationToken cancellationToken = default)
        {
            var validator = new CreditAccountCommandValidator();
            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<LedgerEntryResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var money = new Money(command.Amount, command.Currency);
            var result = await _financialAccountService.CreditAccountAsync(
                command.GamerAccountId,
                money,
                command.Reference,
                command.EntryType,
                command.CorrelationId,
                command.Actor,
                command.Description,
                cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<LedgerEntryResponseDto>.Failure(result.ErrorCode!, result.ErrorMessage!);
            }

            var entry = result.Value!;
            var dto = MapToLedgerEntryResponseDto(entry);
            return Result<LedgerEntryResponseDto>.Success(dto);
        }

        private static LedgerEntryResponseDto MapToLedgerEntryResponseDto(LedgerEntry entry)
        {
            return new LedgerEntryResponseDto
            {
                Id = entry.Id,
                GamerAccountId = entry.GamerAccountId,
                Amount = entry.Amount,
                Currency = entry.Currency,
                Direction = entry.Direction,
                EntryType = entry.EntryType,
                Reference = entry.Reference,
                CorrelationId = entry.CorrelationId,
                Actor = entry.Actor,
                Description = entry.Description,
                BalanceAfter = entry.BalanceAfter,
                CreatedAtUtc = entry.CreatedAtUtc
            };
        }
    }

    public class DebitAccountCommandHandler : ICommandHandler<DebitAccountCommand, LedgerEntryResponseDto>
    {
        private readonly IFinancialAccountService _financialAccountService;

        public DebitAccountCommandHandler(IFinancialAccountService financialAccountService)
        {
            _financialAccountService = financialAccountService ?? throw new ArgumentNullException(nameof(financialAccountService));
        }

        public async Task<Result<LedgerEntryResponseDto>> HandleAsync(DebitAccountCommand command, CancellationToken cancellationToken = default)
        {
            var validator = new DebitAccountCommandValidator();
            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<LedgerEntryResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var money = new Money(command.Amount, command.Currency);
            var result = await _financialAccountService.DebitAccountAsync(
                command.GamerAccountId,
                money,
                command.Reference,
                command.EntryType,
                command.CorrelationId,
                command.Actor,
                command.Description,
                cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<LedgerEntryResponseDto>.Failure(result.ErrorCode!, result.ErrorMessage!);
            }

            var entry = result.Value!;
            var dto = MapToLedgerEntryResponseDto(entry);
            return Result<LedgerEntryResponseDto>.Success(dto);
        }

        private static LedgerEntryResponseDto MapToLedgerEntryResponseDto(LedgerEntry entry)
        {
            return new LedgerEntryResponseDto
            {
                Id = entry.Id,
                GamerAccountId = entry.GamerAccountId,
                Amount = entry.Amount,
                Currency = entry.Currency,
                Direction = entry.Direction,
                EntryType = entry.EntryType,
                Reference = entry.Reference,
                CorrelationId = entry.CorrelationId,
                Actor = entry.Actor,
                Description = entry.Description,
                BalanceAfter = entry.BalanceAfter,
                CreatedAtUtc = entry.CreatedAtUtc
            };
        }
    }

    public class GetAccountBalanceQueryHandler : IQueryHandler<GetAccountBalanceQuery, AccountBalanceResponseDto>
    {
        private readonly IFinancialAccountService _financialAccountService;

        public GetAccountBalanceQueryHandler(IFinancialAccountService financialAccountService)
        {
            _financialAccountService = financialAccountService ?? throw new ArgumentNullException(nameof(financialAccountService));
        }

        public async Task<Result<AccountBalanceResponseDto>> HandleAsync(GetAccountBalanceQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetAccountBalanceQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<AccountBalanceResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var accountResult = await _financialAccountService.GetAccountByGamerIdAsync(query.GamerEntityId, cancellationToken);
            if (!accountResult.IsSuccess)
            {
                return Result<AccountBalanceResponseDto>.Failure(accountResult.ErrorCode!, accountResult.ErrorMessage!);
            }

            var account = accountResult.Value!;
            var response = new AccountBalanceResponseDto
            {
                GamerAccountId = account.Id,
                GamerEntityId = account.GamerEntityId,
                AccountNumber = account.AccountNumber,
                Status = account.Status,
                Currency = account.Currency,
                Balance = account.Balance,
                BonusBalance = account.BonusBalance,
                UpdatedAt = account.UpdatedAt ?? account.CreatedAt
            };

            return Result<AccountBalanceResponseDto>.Success(response);
        }
    }

    public class GetAccountLedgerQueryHandler : IQueryHandler<GetAccountLedgerQuery, IReadOnlyList<LedgerEntryResponseDto>>
    {
        private readonly IFinancialAccountService _financialAccountService;

        public GetAccountLedgerQueryHandler(IFinancialAccountService financialAccountService)
        {
            _financialAccountService = financialAccountService ?? throw new ArgumentNullException(nameof(financialAccountService));
        }

        public async Task<Result<IReadOnlyList<LedgerEntryResponseDto>>> HandleAsync(GetAccountLedgerQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetAccountLedgerQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<IReadOnlyList<LedgerEntryResponseDto>>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            var accountResult = await _financialAccountService.GetAccountByGamerIdAsync(query.GamerEntityId, cancellationToken);
            if (!accountResult.IsSuccess)
            {
                return Result<IReadOnlyList<LedgerEntryResponseDto>>.Failure(accountResult.ErrorCode!, accountResult.ErrorMessage!);
            }

            var account = accountResult.Value!;
            var ledgerResult = await _financialAccountService.GetLedgerAsync(account.Id, query.Page, query.PageSize, cancellationToken);
            if (!ledgerResult.IsSuccess)
            {
                return Result<IReadOnlyList<LedgerEntryResponseDto>>.Failure(ledgerResult.ErrorCode!, ledgerResult.ErrorMessage!);
            }

            var dtoList = ledgerResult.Value!
                .Select(e => new LedgerEntryResponseDto
                {
                    Id = e.Id,
                    GamerAccountId = e.GamerAccountId,
                    Amount = e.Amount,
                    Currency = e.Currency,
                    Direction = e.Direction,
                    EntryType = e.EntryType,
                    Reference = e.Reference,
                    CorrelationId = e.CorrelationId,
                    Actor = e.Actor,
                    Description = e.Description,
                    BalanceAfter = e.BalanceAfter,
                    CreatedAtUtc = e.CreatedAtUtc
                })
                .ToList();

            return Result<IReadOnlyList<LedgerEntryResponseDto>>.Success(dtoList.AsReadOnly());
        }
    }

    public class ProcessFinancialTransactionCommandHandler : ICommandHandler<ProcessFinancialTransactionCommand, FinancialTransactionResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public ProcessFinancialTransactionCommandHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<FinancialTransactionResponseDto>> HandleAsync(ProcessFinancialTransactionCommand command, CancellationToken cancellationToken = default)
        {
            var validator = new ProcessFinancialTransactionCommandValidator();
            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<FinancialTransactionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.ProcessTransactionAsync(command.Request, cancellationToken);
        }
    }

    public class ReverseFinancialTransactionCommandHandler : ICommandHandler<ReverseFinancialTransactionCommand, FinancialTransactionResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public ReverseFinancialTransactionCommandHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<FinancialTransactionResponseDto>> HandleAsync(ReverseFinancialTransactionCommand command, CancellationToken cancellationToken = default)
        {
            var validator = new ReverseFinancialTransactionCommandValidator();
            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<FinancialTransactionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.ReverseTransactionAsync(command.Request, cancellationToken);
        }
    }

    public class CreatePaymentCommandHandler : ICommandHandler<CreatePaymentCommand, PaymentResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public CreatePaymentCommandHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<PaymentResponseDto>> HandleAsync(CreatePaymentCommand command, CancellationToken cancellationToken = default)
        {
            var validator = new CreatePaymentCommandValidator();
            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<PaymentResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.CreatePaymentAsync(command.Request, cancellationToken);
        }
    }

    public class GetFinancialTransactionQueryHandler : IQueryHandler<GetFinancialTransactionQuery, FinancialTransactionResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public GetFinancialTransactionQueryHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<FinancialTransactionResponseDto>> HandleAsync(GetFinancialTransactionQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetFinancialTransactionQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<FinancialTransactionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.GetTransactionByIdAsync(query.TransactionId, cancellationToken);
        }
    }

    public class GetPaymentQueryHandler : IQueryHandler<GetPaymentQuery, PaymentResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public GetPaymentQueryHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<PaymentResponseDto>> HandleAsync(GetPaymentQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetPaymentQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<PaymentResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.GetPaymentByIdAsync(query.PaymentId, cancellationToken);
        }
    }

    public class GetTransactionByIdempotencyKeyQueryHandler : IQueryHandler<GetTransactionByIdempotencyKeyQuery, FinancialTransactionResponseDto>
    {
        private readonly IFinancialTransactionService _transactionService;

        public GetTransactionByIdempotencyKeyQueryHandler(IFinancialTransactionService transactionService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        }

        public async Task<Result<FinancialTransactionResponseDto>> HandleAsync(GetTransactionByIdempotencyKeyQuery query, CancellationToken cancellationToken = default)
        {
            var validator = new GetTransactionByIdempotencyKeyQueryValidator();
            var validationResult = await validator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors.First();
                return Result<FinancialTransactionResponseDto>.Failure(firstError.ErrorCode ?? "VALIDATION_FAILED", firstError.ErrorMessage);
            }

            return await _transactionService.GetTransactionByIdempotencyKeyAsync(query.IdempotencyKey, cancellationToken);
        }
    }
}
