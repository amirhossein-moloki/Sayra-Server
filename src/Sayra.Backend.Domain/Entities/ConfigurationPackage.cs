using System;

#nullable enable

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

        // Cryptographic Integrity Metadata
        public string? ConfigurationHash { get; private set; }
        public string? Signature { get; private set; }
        public string? SignatureAlgorithm { get; private set; }
        public string? SigningKeyId { get; private set; }

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

        public void SetCryptographicSignature(string hash, string signature, string algorithm, string signingKeyId)
        {
            if (!string.IsNullOrWhiteSpace(Signature))
            {
                throw new InvalidOperationException($"Configuration package v{VersionNumber} is already signed and cannot be re-signed or modified.");
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new ArgumentException("Configuration hash cannot be null or empty.", nameof(hash));
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new ArgumentException("Signature cannot be null or empty.", nameof(signature));
            }

            if (string.IsNullOrWhiteSpace(algorithm))
            {
                throw new ArgumentException("Signature algorithm cannot be null or empty.", nameof(algorithm));
            }

            if (string.IsNullOrWhiteSpace(signingKeyId))
            {
                throw new ArgumentException("Signing key ID cannot be null or empty.", nameof(signingKeyId));
            }

            ConfigurationHash = hash.Trim().ToLowerInvariant();
            Signature = signature.Trim();
            SignatureAlgorithm = algorithm.Trim();
            SigningKeyId = signingKeyId.Trim();
        }
    }
}
