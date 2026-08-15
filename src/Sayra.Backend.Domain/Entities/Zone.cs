using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Zone : BaseEntity
    {
        public string ZoneId { get; set; } = string.Empty;
        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Inactive, Disabled

        public void NormalizeAndValidate()
        {
            if (SiteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for Zone.");
            }

            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new InvalidDomainException("INVALID_ZONE_CODE", "Zone Code is required.");
            }
            Code = Code.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_ZONE_NAME", "Zone Name is required.");
            }
            Name = Name.Trim();

            if (string.IsNullOrWhiteSpace(ZoneId))
            {
                ZoneId = Code;
            }
            ZoneId = ZoneId.Trim().ToUpperInvariant();

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) Status = "Inactive";
            else if (statusTrimmed.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) Status = "Disabled";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Zone status: {Status}");
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
