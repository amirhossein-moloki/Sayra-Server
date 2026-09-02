using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class WorkstationGroup : BaseEntity
    {
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Inactive, Disabled

        public uint RowVersion { get; set; }

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for WorkstationGroup.");
            }

            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new InvalidDomainException("INVALID_GROUP_CODE", "WorkstationGroup Code is required.");
            }
            Code = Code.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_GROUP_NAME", "WorkstationGroup Name is required.");
            }
            Name = Name.Trim();

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) Status = "Inactive";
            else if (statusTrimmed.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) Status = "Disabled";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid WorkstationGroup status: {Status}");
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

        public void Activate()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Disable()
        {
            Status = "Disabled";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
