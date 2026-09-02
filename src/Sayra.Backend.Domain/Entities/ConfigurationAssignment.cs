using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class ConfigurationAssignment : BaseEntity
    {
        public Guid ConfigurationPackageId { get; set; }
        public Guid ConfigurationTargetId { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 0;
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string AssignedBy { get; set; } = string.Empty;

        // Concurrency token
        public uint RowVersion { get; set; }

        // Navigation properties
        public ConfigurationPackage? Package { get; set; }
        public ConfigurationTarget? Target { get; set; }

        public static ConfigurationAssignment Create(
            Guid configurationPackageId,
            Guid configurationTargetId,
            string assignedBy,
            int priority = 0,
            DateTime? effectiveFrom = null,
            DateTime? effectiveTo = null)
        {
            if (configurationPackageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ASSIGNMENT", "Configuration package ID cannot be empty.");
            }

            if (configurationTargetId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ASSIGNMENT", "Configuration target ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(assignedBy))
            {
                throw new InvalidDomainException("INVALID_ASSIGNMENT", "AssignedBy identity is required.");
            }

            if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom.Value)
            {
                throw new InvalidDomainException("INVALID_ASSIGNMENT_DATES", "EffectiveTo must be after EffectiveFrom.");
            }

            return new ConfigurationAssignment
            {
                ConfigurationPackageId = configurationPackageId,
                ConfigurationTargetId = configurationTargetId,
                AssignedBy = assignedBy.Trim(),
                Priority = priority,
                IsActive = true,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo
            };
        }

        public void Activate()
        {
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deactivate()
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
        }

        public void SetPriority(int priority)
        {
            Priority = priority;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
