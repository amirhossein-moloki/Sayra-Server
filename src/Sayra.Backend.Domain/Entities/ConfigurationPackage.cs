using System;

namespace Sayra.Backend.Domain
{
    public enum ConfigurationPayloadType
    {
        Full = 0,
        Delta = 1
    }

    public class ConfigurationPackage : BaseEntity
    {
        public string Name { get; private set; } = "default";
        public long VersionNumber { get; private set; }
        public string Version { get; private set; } = string.Empty;
        public long? BaseVersionNumber { get; private set; }
        public ConfigurationPayloadType PayloadType { get; private set; }
        public string SchemaVersion { get; private set; } = "1.0";
        public string Content { get; private set; } = "{}"; // Normalized JSON or Delta JSON
        public bool IsActive { get; set; }
        public string IssuedBy { get; private set; } = "system";

        // Optimistic Concurrency Token
        public uint RowVersion { get; set; }

        // EF Core requirement
        public ConfigurationPackage()
        {
        }

        public static ConfigurationPackage CreateFull(
            string name,
            long versionNumber,
            string content,
            string schemaVersion = "1.0",
            string issuedBy = "system")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Configuration scope name cannot be null or empty.", nameof(name));
            }

            if (versionNumber <= 0)
            {
                throw new ArgumentException("Version number must be strictly greater than 0.", nameof(versionNumber));
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Configuration content cannot be null or empty.", nameof(content));
            }

            return new ConfigurationPackage
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                VersionNumber = versionNumber,
                Version = $"v{versionNumber}",
                BaseVersionNumber = null,
                PayloadType = ConfigurationPayloadType.Full,
                SchemaVersion = string.IsNullOrWhiteSpace(schemaVersion) ? "1.0" : schemaVersion.Trim(),
                Content = content,
                IsActive = true,
                IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "system" : issuedBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };
        }

        public static ConfigurationPackage CreateDelta(
            string name,
            long versionNumber,
            long baseVersionNumber,
            string content,
            string schemaVersion = "1.0",
            string issuedBy = "system")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Configuration scope name cannot be null or empty.", nameof(name));
            }

            if (versionNumber <= 0)
            {
                throw new ArgumentException("Target version number must be strictly greater than 0.", nameof(versionNumber));
            }

            if (baseVersionNumber <= 0)
            {
                throw new ArgumentException("Base version number must be strictly greater than 0.", nameof(baseVersionNumber));
            }

            if (baseVersionNumber >= versionNumber)
            {
                throw new ArgumentException($"Base version ({baseVersionNumber}) must be strictly smaller than target version ({versionNumber}).", nameof(baseVersionNumber));
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Delta content cannot be null or empty.", nameof(content));
            }

            return new ConfigurationPackage
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                VersionNumber = versionNumber,
                Version = $"v{versionNumber}",
                BaseVersionNumber = baseVersionNumber,
                PayloadType = ConfigurationPayloadType.Delta,
                SchemaVersion = string.IsNullOrWhiteSpace(schemaVersion) ? "1.0" : schemaVersion.Trim(),
                Content = content,
                IsActive = true,
                IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "system" : issuedBy.Trim(),
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
