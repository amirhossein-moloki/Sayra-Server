using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public enum ConfigurationTargetType
    {
        GLOBAL = 0,
        SITE = 1,
        GROUP = 2,
        WORKSTATION = 3
    }

    public static class ConfigurationTargetTypeExtensions
    {
        public static ConfigurationTargetType ParseTargetType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDomainException("INVALID_TARGET_TYPE", "Configuration target type cannot be null or empty.");
            }

            var normalized = value.Trim().ToUpperInvariant();
            if (Enum.TryParse<ConfigurationTargetType>(normalized, out var targetType))
            {
                return targetType;
            }

            throw new InvalidDomainException("INVALID_TARGET_TYPE", $"Invalid configuration target type: '{value}'.");
        }
    }
}
