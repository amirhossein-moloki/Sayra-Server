using FluentValidation;

namespace Sayra.Backend.Application.Organizations
{
    public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
    {
        public CreateOrganizationCommandValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Organization Code is required.")
                .MaximumLength(50).WithMessage("Organization Code cannot exceed 50 characters.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Organization Name is required.")
                .MaximumLength(100).WithMessage("Organization Name cannot exceed 100 characters.");
        }
    }

    public class DeactivateOrganizationCommandValidator : AbstractValidator<DeactivateOrganizationCommand>
    {
        public DeactivateOrganizationCommandValidator()
        {
            RuleFor(x => x.OrganizationId)
                .NotEmpty().WithMessage("OrganizationId is required.");
        }
    }

    public class GetOrganizationQueryValidator : AbstractValidator<GetOrganizationQuery>
    {
        public GetOrganizationQueryValidator()
        {
            RuleFor(x => x.OrganizationId)
                .NotEmpty().WithMessage("OrganizationId is required.");
        }
    }
}
