using FluentValidation;

namespace Sayra.Backend.Application.Workstations
{
    public class RegisterWorkstationCommandValidator : AbstractValidator<RegisterWorkstationCommand>
    {
        public RegisterWorkstationCommandValidator()
        {
            RuleFor(x => x.PcId)
                .NotEmpty().WithMessage("PcId is required.");

            RuleFor(x => x.SiteId)
                .NotEmpty().WithMessage("SiteId is required.");

            RuleFor(x => x.Hostname)
                .NotEmpty().WithMessage("Hostname is required.");

            RuleFor(x => x.MacAddress)
                .NotEmpty().WithMessage("MAC Address is required.")
                .Matches(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$").WithMessage("MAC Address format is invalid.");

            RuleFor(x => x.IpAddress)
                .NotEmpty().WithMessage("IP address is required.")
                .Must(ip => System.Net.IPAddress.TryParse(ip, out _)).WithMessage("IP Address format is invalid.");
        }
    }
}
