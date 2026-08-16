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
