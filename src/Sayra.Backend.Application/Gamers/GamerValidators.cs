using FluentValidation;

namespace Sayra.Backend.Application.Gamers
{
    public class CreateGamerCommandValidator : AbstractValidator<CreateGamerCommand>
    {
        public CreateGamerCommandValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("Username is required.")
                .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
                .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("A valid email address is required.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters.");
        }
    }

    public class UpdateGamerProfileCommandValidator : AbstractValidator<UpdateGamerProfileCommand>
    {
        public UpdateGamerProfileCommandValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty().WithMessage("GamerEntityId is required.");
        }
    }

    public class DeactivateGamerCommandValidator : AbstractValidator<DeactivateGamerCommand>
    {
        public DeactivateGamerCommandValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty().WithMessage("GamerEntityId is required.");
        }
    }

    public class ChangeGamerPasswordCommandValidator : AbstractValidator<ChangeGamerPasswordCommand>
    {
        public ChangeGamerPasswordCommandValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty().WithMessage("GamerEntityId is required.");

            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Current password is required.");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("New password is required.")
                .MinimumLength(6).WithMessage("New password must be at least 6 characters.");
        }
    }

    public class AuthenticateGamerCommandValidator : AbstractValidator<AuthenticateGamerCommand>
    {
        public AuthenticateGamerCommandValidator()
        {
            RuleFor(x => x.UsernameOrEmail)
                .NotEmpty().WithMessage("Username or email is required.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.");
        }
    }

    public class GetGamerQueryValidator : AbstractValidator<GetGamerQuery>
    {
        public GetGamerQueryValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty().WithMessage("GamerEntityId is required.");
        }
    }

    public class GetGamerAccountQueryValidator : AbstractValidator<GetGamerAccountQuery>
    {
        public GetGamerAccountQueryValidator()
        {
            RuleFor(x => x.GamerEntityId)
                .NotEmpty().WithMessage("GamerEntityId is required.");
        }
    }
}
