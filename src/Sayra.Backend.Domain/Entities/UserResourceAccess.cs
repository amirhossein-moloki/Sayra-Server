using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class UserResourceAccess : BaseEntity
    {
        public Guid? UserEntityId { get; set; }
        public Guid? RoleId { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public Guid? ResourceId { get; set; }
        public bool IsGranted { get; set; } = true;
        public string Status { get; set; } = "Active";

        public void NormalizeAndValidate()
        {
            if (!UserEntityId.HasValue && !RoleId.HasValue)
            {
                throw new InvalidDomainException("INVALID_RESOURCE_ACCESS", "UserResourceAccess must specify either UserEntityId or RoleId.");
            }

            if (string.IsNullOrWhiteSpace(ResourceType))
            {
                throw new InvalidDomainException("INVALID_RESOURCE_TYPE", "ResourceType is required.");
            }

            ResourceType = ResourceType.Trim();

            Status = (Status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(Status))
            {
                Status = "Active";
            }

            if (!string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Status, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid status '{Status}' for UserResourceAccess.");
            }
        }

        public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

        public void Disable()
        {
            Status = "Disabled";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Enable()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
