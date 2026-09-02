using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public enum ConfigurationPayloadType
    {
        FULL = 0,
        DELTA = 1
    }

    public static class ConfigurationPayloadTypeExtensions
    {
        public static ConfigurationPayloadType ParsePayloadType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDomainException("INVALID_PAYLOAD_TYPE", "Configuration payload type cannot be null or empty.");
            }

            var normalized = value.Trim().ToUpperInvariant();
            if (Enum.TryParse<ConfigurationPayloadType>(normalized, out var payloadType))
            {
                return payloadType;
            }

            throw new InvalidDomainException("INVALID_PAYLOAD_TYPE", $"Invalid configuration payload type: '{value}'.");
        }
    }
}
