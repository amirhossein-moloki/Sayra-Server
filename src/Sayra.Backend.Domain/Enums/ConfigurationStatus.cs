using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public enum ConfigurationStatus
    {
        DRAFT = 0,
        VALIDATED = 1,
        SIGNED = 2,
        PUBLISHED = 3,
        ACTIVE = 4,
        SUPERSEDED = 5,
        REVOKED = 6
    }

    public static class ConfigurationStatusExtensions
    {
        public static bool IsImmutable(this ConfigurationStatus status)
        {
            return status is ConfigurationStatus.PUBLISHED
                or ConfigurationStatus.ACTIVE
                or ConfigurationStatus.SUPERSEDED
                or ConfigurationStatus.REVOKED;
        }

        public static ConfigurationStatus ParseStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDomainException("INVALID_CONFIGURATION_STATUS", "Configuration status cannot be null or empty.");
            }

            var normalized = value.Trim().ToUpperInvariant();
            if (Enum.TryParse<ConfigurationStatus>(normalized, out var status))
            {
                return status;
            }

            throw new InvalidDomainException("INVALID_CONFIGURATION_STATUS", $"Invalid configuration status: '{value}'.");
        }
    }
}
