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
}
