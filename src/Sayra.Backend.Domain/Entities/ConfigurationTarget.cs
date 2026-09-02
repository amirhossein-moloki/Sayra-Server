using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class ConfigurationTarget : BaseEntity
    {
        public ConfigurationTargetType TargetType { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid? GroupId { get; set; }
        public Guid? WorkstationId { get; set; }

        public ConfigurationTarget()
        {
        }

        public static ConfigurationTarget CreateGlobal(Guid organizationId)
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Global target.");
            }

            var target = new ConfigurationTarget
            {
                Id = Guid.NewGuid(),
                TargetType = ConfigurationTargetType.Global,
                OrganizationId = organizationId,
                SiteId = null,
                GroupId = null,
                WorkstationId = null,
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static ConfigurationTarget CreateSite(Guid organizationId, Guid siteId)
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Site target.");
            }

            if (siteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for Site target.");
            }

            var target = new ConfigurationTarget
            {
                Id = Guid.NewGuid(),
                TargetType = ConfigurationTargetType.Site,
                OrganizationId = organizationId,
                SiteId = siteId,
                GroupId = null,
                WorkstationId = null,
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static ConfigurationTarget CreateGroup(Guid organizationId, Guid groupId, Guid? siteId = null)
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Group target.");
            }

            if (groupId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GROUP_ID", "GroupId is required for Group target.");
            }

            var target = new ConfigurationTarget
            {
                Id = Guid.NewGuid(),
                TargetType = ConfigurationTargetType.Group,
                OrganizationId = organizationId,
                SiteId = siteId,
                GroupId = groupId,
                WorkstationId = null,
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public static ConfigurationTarget CreateWorkstation(Guid organizationId, Guid workstationId, Guid? siteId = null, Guid? groupId = null)
        {
            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Workstation target.");
            }

            if (workstationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_WORKSTATION_ID", "WorkstationId is required for Workstation target.");
            }

            var target = new ConfigurationTarget
            {
                Id = Guid.NewGuid(),
                TargetType = ConfigurationTargetType.Workstation,
                OrganizationId = organizationId,
                SiteId = siteId,
                GroupId = groupId,
                WorkstationId = workstationId,
                CreatedAt = DateTime.UtcNow
            };

            target.NormalizeAndValidate();
            return target;
        }

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for ConfigurationTarget.");
            }

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
                    throw new InvalidDomainException("INVALID_TARGET_TYPE", $"Unsupported ConfigurationTargetType: {TargetType}");
            }
        }
    }
}
