using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Site : BaseEntity
    {
        public string SiteId { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Inactive, Suspended
        public string Timezone { get; set; } = "UTC";

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Site.");
            }

            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new InvalidDomainException("INVALID_SITE_CODE", "Site Code is required.");
            }
            Code = Code.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_SITE_NAME", "Site Name is required.");
            }
            Name = Name.Trim();

            if (string.IsNullOrWhiteSpace(SiteId))
            {
                SiteId = Code;
            }
            SiteId = SiteId.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Timezone))
            {
                Timezone = "UTC";
            }
            else
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(Timezone.Trim());
                    Timezone = Timezone.Trim();
                }
                catch
                {
                    throw new InvalidDomainException("INVALID_TIMEZONE", $"Timezone '{Timezone}' is invalid.");
                }
            }

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) Status = "Inactive";
            else if (statusTrimmed.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) Status = "Suspended";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Site status: {Status}");
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
