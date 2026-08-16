using System;
using FluentValidation;

namespace Sayra.Backend.Application.Reservations
{
    public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
    {
        public CreateReservationCommandValidator()
        {
            RuleFor(x => x.GamerId)
                .NotEmpty().WithMessage("GamerId is required.");

            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");

            RuleFor(x => x.StartTimeUtc)
                .NotEmpty().WithMessage("StartTimeUtc is required.");

            RuleFor(x => x.EndTimeUtc)
                .NotEmpty().WithMessage("EndTimeUtc is required.")
                .GreaterThan(x => x.StartTimeUtc).WithMessage("EndTimeUtc must be after StartTimeUtc.");

            RuleFor(x => x.ReservedAmount)
                .GreaterThanOrEqualTo(0).When(x => x.ReservedAmount.HasValue)
                .WithMessage("ReservedAmount cannot be negative.");
        }
    }

    public class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
    {
        public ConfirmReservationCommandValidator()
        {
            RuleFor(x => x.ReservationId)
                .NotEmpty().WithMessage("ReservationId is required.");
        }
    }

    public class CancelReservationCommandValidator : AbstractValidator<CancelReservationCommand>
    {
        public CancelReservationCommandValidator()
        {
            RuleFor(x => x.ReservationId)
                .NotEmpty().WithMessage("ReservationId is required.");
        }
    }

    public class ActivateReservationCommandValidator : AbstractValidator<ActivateReservationCommand>
    {
        public ActivateReservationCommandValidator()
        {
            RuleFor(x => x.ReservationId)
                .NotEmpty().WithMessage("ReservationId is required.");
        }
    }

    public class GetReservationQueryValidator : AbstractValidator<GetReservationQuery>
    {
        public GetReservationQueryValidator()
        {
            RuleFor(x => x.ReservationId)
                .NotEmpty().WithMessage("ReservationId is required.");
        }
    }

    public class ValidateReservationQueryValidator : AbstractValidator<ValidateReservationQuery>
    {
        public ValidateReservationQueryValidator()
        {
            RuleFor(x => x)
                .Must(x => (x.ReservationId.HasValue && x.ReservationId.Value != Guid.Empty) ||
                           (x.GamerId.HasValue && x.GamerId.Value != Guid.Empty) ||
                           (x.SiteId.HasValue && x.SiteId.Value != Guid.Empty) ||
                           (x.WorkstationId.HasValue && x.WorkstationId.Value != Guid.Empty))
                .WithMessage("At least one validation search parameter (ReservationId, GamerId, SiteId, or WorkstationId) must be provided.");
        }
    }
}
