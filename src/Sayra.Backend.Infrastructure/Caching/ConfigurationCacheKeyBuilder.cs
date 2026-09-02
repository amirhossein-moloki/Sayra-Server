using System;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Infrastructure.Caching
{
    public static class ConfigurationCacheKeyBuilder
    {
        public const string DefaultPrefix = "sayra:config:v1:";

        public static string GetEffectiveConfigKey(string prefix, Guid organizationId, Guid workstationId)
        {
            return $"{NormalizePrefix(prefix)}effective:{organizationId}:{workstationId}";
        }

        public static string GetPublicationKey(string prefix, Guid organizationId, Guid targetId)
        {
            return $"{NormalizePrefix(prefix)}publication:{organizationId}:{targetId}";
        }

        public static string GetScopeRevisionKey(string prefix, Guid organizationId, ConfigurationTargetType targetType, Guid? targetId)
        {
            var targetIdString = targetId.HasValue && targetId.Value != Guid.Empty ? targetId.Value.ToString() : "global";
            return $"{NormalizePrefix(prefix)}rev:{organizationId}:{targetType}:{targetIdString}";
        }

        public static string GetScopeRevisionKey(string prefix, Guid organizationId, string scopeType, string scopeIdentifier)
        {
            var idString = string.IsNullOrWhiteSpace(scopeIdentifier) ? "global" : scopeIdentifier;
            return $"{NormalizePrefix(prefix)}rev:{organizationId}:{scopeType}:{idString}";
        }

        public static string GetStampedeLockKey(string prefix, Guid organizationId, Guid workstationId)
        {
            return $"{NormalizePrefix(prefix)}lock:{organizationId}:{workstationId}";
        }

        private static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return DefaultPrefix;
            return prefix.EndsWith(":") ? prefix : prefix + ":";
        }
    }
}
