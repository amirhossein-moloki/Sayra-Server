using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class ConfigurationAssignment : BaseEntity
    {
        public Guid ConfigurationPackageId { get; set; }
        public Guid ConfigurationTargetId { get; set; }
        public bool IsActive { get; set; } = true;
        public string AssignedBy { get; set; } = "system";

        public uint RowVersion { get; set; }

        public ConfigurationAssignment()
        {
        }

        public static ConfigurationAssignment Create(Guid configurationPackageId, Guid configurationTargetId, string assignedBy = "system")
        {
            if (configurationPackageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "ConfigurationPackageId is required.");
            }

            if (configurationTargetId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_TARGET_ID", "ConfigurationTargetId is required.");
            }

            return new ConfigurationAssignment
            {
                Id = Guid.NewGuid(),
                ConfigurationPackageId = configurationPackageId,
                ConfigurationTargetId = configurationTargetId,
                IsActive = true,
                AssignedBy = string.IsNullOrWhiteSpace(assignedBy) ? "system" : assignedBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };
        }

        public void Unassign()
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Reassign(string assignedBy = "system")
        {
            IsActive = true;
            AssignedBy = string.IsNullOrWhiteSpace(assignedBy) ? "system" : assignedBy.Trim();
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
