// PHASE 03 — STAGE 03-09: Payment / Financial Transaction Engine & Idempotency
using System;
using System.Collections.Generic;
using FluentValidation;

namespace Sayra.Backend.Application.Financial
{
    public class CreditAccountCommandValidator : AbstractValidator<CreditAccountCommand>
    {
        public CreditAccountCommandValidator()
        {
            RuleFor(x => x.GamerAccountId)
                .NotEmpty()
                .WithErrorCode("INVALID_ACCOUNT_ID")
                .WithMessage("GamerAccountId is required.");

            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithErrorCode("INVALID_AMOUNT")
                .WithMessage("Credit amount must be strictly greater than zero.");

            RuleFor(x => x.Currency)
                .NotEmpty()
                .WithErrorCode("INVALID_CURRENCY")
                .WithMessage("Currency is required.");

            RuleFor(x => x.Reference)
                .NotEmpty()
                .WithErrorCode("INVALID_REFERENCE")
                .WithMessage("Operation reference is required.");
        }
    }

    public class DebitAccountCommandValidator : AbstractValidator<DebitAccountCommand>
    {
        public DebitAccountCommandValidator()
        {
            RuleFor(x => x.GamerAccountId)
                .NotEmpty()
                .WithErrorCode("INVALID_ACCOUNT_ID")
                .WithMessage("GamerAccountId is required.");

            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithErrorCode("INVALID_AMOUNT")
                .WithMessage("Debit amount must be strictly greater than zero.");

            RuleFor(x => x.Currency)
                .NotEmpty()
                .WithErrorCode("INVALID_CURRENCY")
                .WithMessage("Currency is required.");

            RuleFor(x => x.Reference)
                .NotEmpty()
                .WithErrorCode("INVALID_REFERENCE")
                .WithMessage("Operation reference is required.");
        }
    }

    public class GetAccountBalanceQueryValidator : AbstractValidator<GetAccountBalanceQuery>
    {
        public GetAccountBalanceQueryValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty()
                .WithErrorCode("INVALID_GAMER_ID")
                .WithMessage("GamerEntityId is required.");
        }
    }

    public class GetAccountLedgerQueryValidator : AbstractValidator<GetAccountLedgerQuery>
    {
        public GetAccountLedgerQueryValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty()
                .WithErrorCode("INVALID_GAMER_ID")
                .WithMessage("GamerEntityId is required.");

            RuleFor(x => x.Page)
                .GreaterThanOrEqualTo(1)
                .WithErrorCode("INVALID_PAGE")
                .WithMessage("Page must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 200)
                .WithErrorCode("INVALID_PAGE_SIZE")
                .WithMessage("PageSize must be between 1 and 200.");
        }
    }

    public class ProcessFinancialTransactionCommandValidator : AbstractValidator<ProcessFinancialTransactionCommand>
    {
        public ProcessFinancialTransactionCommandValidator()
        {
            RuleFor(x => x.Request)
                .NotNull()
                .WithErrorCode("INVALID_REQUEST")
                .WithMessage("Request cannot be null.");

            When(x => x.Request != null, () =>
            {
                RuleFor(x => x.Request.GamerAccountId)
                    .NotEmpty()
                    .WithErrorCode("INVALID_ACCOUNT_ID")
                    .WithMessage("GamerAccountId is required.");

                RuleFor(x => x.Request.Amount)
                    .GreaterThan(0)
                    .WithErrorCode("INVALID_AMOUNT")
                    .WithMessage("Amount must be strictly greater than zero.");

                RuleFor(x => x.Request.IdempotencyKey)
                    .NotEmpty()
                    .WithErrorCode("INVALID_IDEMPOTENCY_KEY")
                    .WithMessage("IdempotencyKey is required for financial transaction processing.");
            });
        }
    }

    public class ReverseFinancialTransactionCommandValidator : AbstractValidator<ReverseFinancialTransactionCommand>
    {
        public ReverseFinancialTransactionCommandValidator()
        {
            RuleFor(x => x.Request)
                .NotNull()
                .WithErrorCode("INVALID_REQUEST")
                .WithMessage("Request cannot be null.");

            When(x => x.Request != null, () =>
            {
                RuleFor(x => x.Request.OriginalTransactionId)
                    .NotEmpty()
                    .WithErrorCode("INVALID_TRANSACTION_ID")
                    .WithMessage("OriginalTransactionId is required.");

                RuleFor(x => x.Request.IdempotencyKey)
                    .NotEmpty()
                    .WithErrorCode("INVALID_IDEMPOTENCY_KEY")
                    .WithMessage("IdempotencyKey is required for transaction reversal.");
            });
        }
    }

    public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
    {
        public CreatePaymentCommandValidator()
        {
            RuleFor(x => x.Request)
                .NotNull()
                .WithErrorCode("INVALID_REQUEST")
                .WithMessage("Request cannot be null.");

            When(x => x.Request != null, () =>
            {
                RuleFor(x => x.Request.GamerAccountId)
                    .NotEmpty()
                    .WithErrorCode("INVALID_ACCOUNT_ID")
                    .WithMessage("GamerAccountId is required.");

                RuleFor(x => x.Request.Amount)
                    .GreaterThan(0)
                    .WithErrorCode("INVALID_AMOUNT")
                    .WithMessage("Payment amount must be strictly greater than zero.");

                RuleFor(x => x.Request.IdempotencyKey)
                    .NotEmpty()
                    .WithErrorCode("INVALID_IDEMPOTENCY_KEY")
                    .WithMessage("IdempotencyKey is required for payment creation.");
            });
        }
    }

    public class GetFinancialTransactionQueryValidator : AbstractValidator<GetFinancialTransactionQuery>
    {
        public GetFinancialTransactionQueryValidator()
        {
            RuleFor(x => x.TransactionId)
                .NotEmpty()
                .WithErrorCode("INVALID_TRANSACTION_ID")
                .WithMessage("TransactionId is required.");
        }
    }

    public class GetPaymentQueryValidator : AbstractValidator<GetPaymentQuery>
    {
        public GetPaymentQueryValidator()
        {
            RuleFor(x => x.PaymentId)
                .NotEmpty()
                .WithErrorCode("INVALID_PAYMENT_ID")
                .WithMessage("PaymentId is required.");
        }
    }

    public class GetTransactionByIdempotencyKeyQueryValidator : AbstractValidator<GetTransactionByIdempotencyKeyQuery>
    {
        public GetTransactionByIdempotencyKeyQueryValidator()
        {
            RuleFor(x => x.IdempotencyKey)
                .NotEmpty()
                .WithErrorCode("INVALID_IDEMPOTENCY_KEY")
                .WithMessage("IdempotencyKey is required.");
        }
    }
}
