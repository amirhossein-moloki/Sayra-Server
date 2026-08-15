using System;
using FluentValidation;

namespace Sayra.Backend.Application.Workstations
{
    public class AssignWorkstationCommandValidator : AbstractValidator<AssignWorkstationCommand>
    {
        public AssignWorkstationCommandValidator()
        {
            RuleFor(x => x.WorkstationId)
                .NotEmpty().WithMessage("WorkstationId is required.");

            RuleFor(x => x.OrganizationId)
                .NotEmpty().WithMessage("OrganizationId is required.");

            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");

            RuleFor(x => x.ZoneId)
                .NotEmpty().WithMessage("ZoneId is required.");
        }
    }

    public class GetWorkstationAssignmentQueryValidator : AbstractValidator<GetWorkstationAssignmentQuery>
    {
        public GetWorkstationAssignmentQueryValidator()
        {
            RuleFor(x => x.WorkstationId)
                .NotEmpty().WithMessage("WorkstationId is required.");
        }
    }
}
