using System;
using FluentValidation;

namespace Sayra.Backend.Application.Locations
{
    public class CreateSiteCommandValidator : AbstractValidator<CreateSiteCommand>
    {
        public CreateSiteCommandValidator()
        {
            RuleFor(x => x.OrganizationId)
                .NotEmpty().WithMessage("OrganizationId is required.");

            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Site Code is required.")
                .MaximumLength(50).WithMessage("Site Code cannot exceed 50 characters.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Site Name is required.")
                .MaximumLength(100).WithMessage("Site Name cannot exceed 100 characters.");
        }
    }

    public class DeactivateSiteCommandValidator : AbstractValidator<DeactivateSiteCommand>
    {
        public DeactivateSiteCommandValidator()
        {
            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");
        }
    }

    public class GetSiteQueryValidator : AbstractValidator<GetSiteQuery>
    {
        public GetSiteQueryValidator()
        {
            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");
        }
    }

    public class CreateZoneCommandValidator : AbstractValidator<CreateZoneCommand>
    {
        public CreateZoneCommandValidator()
        {
            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");

            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Zone Code is required.")
                .MaximumLength(50).WithMessage("Zone Code cannot exceed 50 characters.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Zone Name is required.")
                .MaximumLength(100).WithMessage("Zone Name cannot exceed 100 characters.");
        }
    }

    public class DeactivateZoneCommandValidator : AbstractValidator<DeactivateZoneCommand>
    {
        public DeactivateZoneCommandValidator()
        {
            RuleFor(x => x.ZoneId)
                .NotEmpty().WithMessage("ZoneId is required.");
        }
    }

    public class GetZoneQueryValidator : AbstractValidator<GetZoneQuery>
    {
        public GetZoneQueryValidator()
        {
            RuleFor(x => x.ZoneId)
                .NotEmpty().WithMessage("ZoneId is required.");
        }
    }
}
