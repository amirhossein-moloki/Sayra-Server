using System;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Domain
{
    public class UpdateTarget : BaseEntity
    {
        public Guid OrganizationId { get; set; }
        public Guid ReleaseId { get; set; }
        public ConfigurationTargetType TargetType { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }
        public int RolloutPercentage { get; set; } = 100;
        public bool IsEnabled { get; set; } = true;
        public string? MinimumSupportedVersion { get; set; }
        public bool IsMandatoryOverride { get; set; }
        public string CreatedBy { get; set; } = "system";

        // Navigation reference
        public UpdateRelease? Release { get; set; }

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public UpdateTarget()
        {
        }

        public static UpdateTarget CreateGlobal(
            Guid organizationId,
            Guid releaseId,
            int rolloutPercentage = 100,
            string? minimumSupportedVersion = null,
            bool isMandatoryOverride = false,
            string createdBy = "system")
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Global update target.");
            }

            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "ReleaseId is required for update target.");
            }

            var target = new UpdateTarget
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReleaseId = releaseId,
                TargetType = ConfigurationTargetType.Global,
                SiteId = null,
                GroupId = null,
                WorkstationId = null,
                RolloutPercentage = ValidateRolloutPercentage(rolloutPercentage),
                IsEnabled = true,
                MinimumSupportedVersion = NormalizeVersionString(minimumSupportedVersion),
                IsMandatoryOverride = isMandatoryOverride,
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static UpdateTarget CreateSite(
            Guid organizationId,
            Guid releaseId,
            Guid siteId,
            int rolloutPercentage = 100,
            string? minimumSupportedVersion = null,
            bool isMandatoryOverride = false,
            string createdBy = "system")
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Site update target.");
            }

            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "ReleaseId is required for update target.");
            }

            if (siteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for Site update target.");
            }

            var target = new UpdateTarget
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReleaseId = releaseId,
                TargetType = ConfigurationTargetType.Site,
                SiteId = siteId,
                GroupId = null,
                WorkstationId = null,
                RolloutPercentage = ValidateRolloutPercentage(rolloutPercentage),
                IsEnabled = true,
                MinimumSupportedVersion = NormalizeVersionString(minimumSupportedVersion),
                IsMandatoryOverride = isMandatoryOverride,
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static UpdateTarget CreateGroup(
            Guid organizationId,
            Guid releaseId,
            Guid groupId,
            Guid? siteId = null,
            int rolloutPercentage = 100,
            string? minimumSupportedVersion = null,
            bool isMandatoryOverride = false,
            string createdBy = "system")
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Group update target.");
            }

            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "ReleaseId is required for update target.");
            }

            if (groupId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GROUP_ID", "GroupId is required for Group update target.");
            }

            var target = new UpdateTarget
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReleaseId = releaseId,
                TargetType = ConfigurationTargetType.Group,
                SiteId = siteId,
                GroupId = groupId,
                WorkstationId = null,
                RolloutPercentage = ValidateRolloutPercentage(rolloutPercentage),
                IsEnabled = true,
                MinimumSupportedVersion = NormalizeVersionString(minimumSupportedVersion),
                IsMandatoryOverride = isMandatoryOverride,
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static UpdateTarget CreateWorkstation(
            Guid organizationId,
            Guid releaseId,
            Guid workstationId,
            Guid? siteId = null,
            Guid? groupId = null,
            int rolloutPercentage = 100,
            string? minimumSupportedVersion = null,
            bool isMandatoryOverride = false,
            string createdBy = "system")
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Workstation update target.");
            }

            if (releaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "ReleaseId is required for update target.");
            }

            if (workstationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_WORKSTATION_ID", "WorkstationId is required for Workstation update target.");
            }

            var target = new UpdateTarget
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReleaseId = releaseId,
                TargetType = ConfigurationTargetType.Workstation,
                SiteId = siteId,
                GroupId = groupId,
                WorkstationId = workstationId,
                RolloutPercentage = ValidateRolloutPercentage(rolloutPercentage),
                IsEnabled = true,
                MinimumSupportedVersion = NormalizeVersionString(minimumSupportedVersion),
                IsMandatoryOverride = isMandatoryOverride,
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public void SetRolloutPercentage(int percentage)
        {
            RolloutPercentage = ValidateRolloutPercentage(percentage);
            UpdatedAt = DateTime.UtcNow;
        }

        public void SetMinimumSupportedVersion(string? minimumVersion)
        {
            MinimumSupportedVersion = NormalizeVersionString(minimumVersion);
            UpdatedAt = DateTime.UtcNow;
        }

        public void Enable()
        {
            IsEnabled = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Disable()
        {
            IsEnabled = false;
            UpdatedAt = DateTime.UtcNow;
        }

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for UpdateTarget.");
            }

            if (ReleaseId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_RELEASE_ID", "ReleaseId is required for UpdateTarget.");
            }

            RolloutPercentage = ValidateRolloutPercentage(RolloutPercentage);

            switch (TargetType)
            {
                case ConfigurationTargetType.Global:
                    if (SiteId.HasValue || GroupId.HasValue || WorkstationId.HasValue)
                    {
                        throw new InvalidDomainException("INVALID_TARGET_SCOPES", "Global target cannot specify SiteId, GroupId, or WorkstationId.");
                    }
                    break;

                case ConfigurationTargetType.Site:
                    if (!SiteId.HasValue || SiteId.Value == Guid.Empty)
                    {
                        throw new InvalidDomainException("INVALID_SITE_ID", "Site target requires a valid SiteId.");
                    }
                    if (GroupId.HasValue || WorkstationId.HasValue)
                    {
                        throw new InvalidDomainException("INVALID_TARGET_SCOPES", "Site target cannot specify GroupId or WorkstationId.");
                    }
                    break;

                case ConfigurationTargetType.Group:
                    if (!GroupId.HasValue || GroupId.Value == Guid.Empty)
                    {
                        throw new InvalidDomainException("INVALID_GROUP_ID", "Group target requires a valid GroupId.");
                    }
                    if (WorkstationId.HasValue)
                    {
                        throw new InvalidDomainException("INVALID_TARGET_SCOPES", "Group target cannot specify WorkstationId.");
                    }
                    break;

                case ConfigurationTargetType.Workstation:
                    if (!WorkstationId.HasValue || WorkstationId.Value == Guid.Empty)
                    {
                        throw new InvalidDomainException("INVALID_WORKSTATION_ID", "Workstation target requires a valid WorkstationId.");
                    }
                    break;

                default:
                    throw new InvalidDomainException("INVALID_TARGET_TYPE", $"Unsupported TargetType: {TargetType}");
            }
        }

        private static int ValidateRolloutPercentage(int percentage)
        {
            if (percentage < 0 || percentage > 100)
            {
                throw new InvalidDomainException("INVALID_ROLLOUT_PERCENTAGE", $"Rollout percentage must be between 0 and 100 inclusive. Provided value: {percentage}");
            }
            return percentage;
        }

        private static string? NormalizeVersionString(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }
            return version.Trim();
        }
    }
}
