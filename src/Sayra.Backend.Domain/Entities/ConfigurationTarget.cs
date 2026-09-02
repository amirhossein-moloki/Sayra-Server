using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class ConfigurationTarget : BaseEntity
    {
        public ConfigurationTargetType TargetType { get; set; } = ConfigurationTargetType.GLOBAL;
        public string TargetIdentifier { get; set; } = "GLOBAL";
        public Guid? SiteEntityId { get; set; }
        public Guid? WorkstationEntityId { get; set; }
        public string? Description { get; set; }

        public static ConfigurationTarget CreateGlobal(string? description = null)
        {
            return new ConfigurationTarget
            {
                TargetType = ConfigurationTargetType.GLOBAL,
                TargetIdentifier = "GLOBAL",
                Description = description
            };
        }

        public static ConfigurationTarget CreateSiteTarget(Guid siteEntityId, string siteIdentifier, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(siteIdentifier))
            {
                throw new InvalidDomainException("INVALID_TARGET_IDENTIFIER", "Site target identifier cannot be null or empty.");
            }

            return new ConfigurationTarget
            {
                TargetType = ConfigurationTargetType.SITE,
                TargetIdentifier = siteIdentifier.Trim().ToUpperInvariant(),
                SiteEntityId = siteEntityId,
                Description = description
            };
        }

        public static ConfigurationTarget CreateGroupTarget(string groupIdentifier, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                throw new InvalidDomainException("INVALID_TARGET_IDENTIFIER", "Group target identifier cannot be null or empty.");
            }

            return new ConfigurationTarget
            {
                TargetType = ConfigurationTargetType.GROUP,
                TargetIdentifier = groupIdentifier.Trim().ToUpperInvariant(),
                Description = description
            };
        }

        public static ConfigurationTarget CreateWorkstationTarget(Guid workstationEntityId, string pcId, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(pcId))
            {
                throw new InvalidDomainException("INVALID_TARGET_IDENTIFIER", "Workstation target identifier cannot be null or empty.");
            }

            return new ConfigurationTarget
            {
                TargetType = ConfigurationTargetType.WORKSTATION,
                TargetIdentifier = pcId.Trim().ToUpperInvariant(),
                WorkstationEntityId = workstationEntityId,
                Description = description
            };
        }

        public void NormalizeAndValidate()
        {
            if (TargetType == ConfigurationTargetType.GLOBAL)
            {
                TargetIdentifier = "GLOBAL";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TargetIdentifier))
                {
                    throw new InvalidDomainException("INVALID_TARGET_IDENTIFIER", $"Target identifier is required for target type {TargetType}.");
                }
                TargetIdentifier = TargetIdentifier.Trim().ToUpperInvariant();
            }

            if (TargetType == ConfigurationTargetType.SITE && !SiteEntityId.HasValue)
            {
                // Site target should have a site reference if known
            }

            if (TargetType == ConfigurationTargetType.WORKSTATION && !WorkstationEntityId.HasValue)
            {
                // Workstation target should have a workstation reference if known
            }
        }
    }
}
