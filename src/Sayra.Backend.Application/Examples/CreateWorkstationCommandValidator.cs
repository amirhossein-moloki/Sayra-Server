using FluentValidation;

namespace Sayra.Backend.Application.Examples
{
    public class CreateWorkstationCommandValidator : AbstractValidator<CreateWorkstationCommand>
    {
        public CreateWorkstationCommandValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Workstation name is required.")
                .MaximumLength(100).WithMessage("Workstation name cannot exceed 100 characters.");

            RuleFor(x => x.IpAddress)
                .NotEmpty().WithMessage("IP address is required.")
                .Matches(@"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$").WithMessage("Invalid IP address format.");
        }
    }
}
