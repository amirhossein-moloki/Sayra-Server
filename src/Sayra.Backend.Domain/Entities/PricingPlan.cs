using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class PricingPlan : BaseEntity
    {
        public Guid PricingPlanId
        {
            get => Id;
            set => Id = value;
        }

        public Guid SiteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = "Inactive";
        public string Currency { get; set; } = "SAY";

        public void Activate()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deactivate()
        {
            Status = "Inactive";
            UpdatedAt = DateTime.UtcNow;
        }

        public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

        public void NormalizeAndValidate()
        {
            if (SiteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for PricingPlan.");
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDomainException("INVALID_PLAN_NAME", "Pricing plan name is required.");
            }

            Name = Name.Trim();

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            else
            {
                Currency = Currency.Trim().ToUpperInvariant();
            }

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (string.Equals(statusTrimmed, "Active", StringComparison.OrdinalIgnoreCase))
            {
                Status = "Active";
            }
            else if (string.Equals(statusTrimmed, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                Status = "Inactive";
            }
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid PricingPlan status: {Status}");
            }
        }
    }
}
