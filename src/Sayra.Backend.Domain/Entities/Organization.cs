using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Organization : BaseEntity
    {
        public string OrganizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Inactive, Suspended

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_CODE", "Organization Code is required.");
            }
            Code = Code.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_NAME", "Organization Name is required.");
            }
            Name = Name.Trim();

            if (string.IsNullOrWhiteSpace(OrganizationId))
            {
                OrganizationId = Id.ToString();
            }
            OrganizationId = OrganizationId.Trim();

            var statusUpper = (Status ?? string.Empty).Trim();
            if (statusUpper.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusUpper.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) Status = "Inactive";
            else if (statusUpper.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) Status = "Suspended";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Organization status: {Status}");
            }
        }

        public bool CanOperate()
        {
            return Status != null && Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
        }

        public void Deactivate()
        {
            Status = "Inactive";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Suspend()
        {
            Status = "Suspended";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
