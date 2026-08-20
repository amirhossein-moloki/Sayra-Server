using FluentValidation;

namespace Sayra.Backend.Application.Sessions
{
    public class StartSessionCommandValidator : AbstractValidator<StartSessionCommand>
    {
        public StartSessionCommandValidator()
        {
            RuleFor(x => x.GamerId)
                .NotEmpty()
                .WithErrorCode("INVALID_GAMER_ID")
                .WithMessage("GamerId must not be empty.");

            RuleFor(x => x.WorkstationId)
                .NotEmpty()
                .WithErrorCode("INVALID_WORKSTATION_ID")
                .WithMessage("WorkstationId must not be empty.");
        }
    }

    public class PauseSessionCommandValidator : AbstractValidator<PauseSessionCommand>
    {
        public PauseSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class ExtendSessionCommandValidator : AbstractValidator<ExtendSessionCommand>
    {
        public ExtendSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");

            RuleFor(x => x.AdditionalMinutes)
                .GreaterThan(0)
                .WithErrorCode("INVALID_ADDITIONAL_MINUTES")
                .WithMessage("AdditionalMinutes must be greater than zero.");
        }
    }

    public class ResumeSessionCommandValidator : AbstractValidator<ResumeSessionCommand>
    {
        public ResumeSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class StopSessionCommandValidator : AbstractValidator<StopSessionCommand>
    {
        public StopSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class CancelSessionCommandValidator : AbstractValidator<CancelSessionCommand>
    {
        public CancelSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class TerminateSessionCommandValidator : AbstractValidator<TerminateSessionCommand>
    {
        public TerminateSessionCommandValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetSessionQueryValidator : AbstractValidator<GetSessionQuery>
    {
        public GetSessionQueryValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetSessionCurrentStateQueryValidator : AbstractValidator<GetSessionCurrentStateQuery>
    {
        public GetSessionCurrentStateQueryValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetSessionTimingQueryValidator : AbstractValidator<GetSessionTimingQuery>
    {
        public GetSessionTimingQueryValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetSessionDurationQueryValidator : AbstractValidator<GetSessionDurationQuery>
    {
        public GetSessionDurationQueryValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetSessionRemainingTimeQueryValidator : AbstractValidator<GetSessionRemainingTimeQuery>
    {
        public GetSessionRemainingTimeQueryValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithErrorCode("INVALID_SESSION_ID")
                .WithMessage("SessionId must not be empty.");
        }
    }

    public class GetActiveSessionByWorkstationQueryValidator : AbstractValidator<GetActiveSessionByWorkstationQuery>
    {
        public GetActiveSessionByWorkstationQueryValidator()
        {
            RuleFor(x => x.WorkstationId)
                .NotEmpty()
                .WithErrorCode("INVALID_WORKSTATION_ID")
                .WithMessage("WorkstationId must not be empty.");
        }
    }

    public class GetActiveSessionByGamerQueryValidator : AbstractValidator<GetActiveSessionByGamerQuery>
    {
        public GetActiveSessionByGamerQueryValidator()
        {
            RuleFor(x => x.GamerId)
                .NotEmpty()
                .WithErrorCode("INVALID_GAMER_ID")
                .WithMessage("GamerId must not be empty.");
        }
    }
}
