using System;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class ConfigurationPackage : BaseEntity
    {
        public string PackageId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ConfigurationVersion Version { get; set; } = new ConfigurationVersion(1, 0, 0);
        public ConfigurationVersion? BaseVersion { get; set; }
        public ConfigurationPayloadType PayloadType { get; set; } = ConfigurationPayloadType.FULL;
        public ConfigurationStatus Status { get; set; } = ConfigurationStatus.DRAFT;
        public string Content { get; set; } = "{}";
        public string? ContentHash { get; set; }
        public string? Signature { get; set; }
        public string? SignerIdentity { get; set; }
        public DateTime? SignedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public string? PublishedBy { get; set; }

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public static ConfigurationPackage CreateFull(
            string packageId,
            string name,
            ConfigurationVersion version,
            string content,
            string createdBy)
        {
            ValidateBasicInfo(packageId, name, createdBy, content);

            if (version is null)
            {
                throw new InvalidDomainException("INVALID_VERSION", "Configuration version is required.");
            }

            return new ConfigurationPackage
            {
                PackageId = packageId.Trim().ToUpperInvariant(),
                Name = name.Trim(),
                Version = version,
                BaseVersion = null,
                PayloadType = ConfigurationPayloadType.FULL,
                Status = ConfigurationStatus.DRAFT,
                Content = content,
                CreatedBy = createdBy.Trim()
            };
        }

        public static ConfigurationPackage CreateDelta(
            string packageId,
            string name,
            ConfigurationVersion version,
            ConfigurationVersion baseVersion,
            string content,
            string createdBy)
        {
            ValidateBasicInfo(packageId, name, createdBy, content);

            if (version is null)
            {
                throw new InvalidDomainException("INVALID_VERSION", "Configuration version is required.");
            }

            if (baseVersion is null)
            {
                throw new InvalidDomainException("INVALID_BASE_VERSION", "Delta configuration package requires a base version.");
            }

            if (baseVersion >= version)
            {
                throw new InvalidDomainException("INVALID_BASE_VERSION", $"Delta BaseVersion ({baseVersion}) must be strictly less than Version ({version}).");
            }

            return new ConfigurationPackage
            {
                PackageId = packageId.Trim().ToUpperInvariant(),
                Name = name.Trim(),
                Version = version,
                BaseVersion = baseVersion,
                PayloadType = ConfigurationPayloadType.DELTA,
                Status = ConfigurationStatus.DRAFT,
                Content = content,
                CreatedBy = createdBy.Trim()
            };
        }

        private static void ValidateBasicInfo(string packageId, string name, string createdBy, string content)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "Package ID is required.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDomainException("INVALID_PACKAGE_NAME", "Package name is required.");
            }

            if (string.IsNullOrWhiteSpace(createdBy))
            {
                throw new InvalidDomainException("INVALID_CREATED_BY", "CreatedBy identity is required.");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidDomainException("INVALID_CONTENT", "Configuration payload content cannot be empty.");
            }
        }

        public void SetContent(string newContent)
        {
            EnsureNotImmutable();

            if (string.IsNullOrWhiteSpace(newContent))
            {
                throw new InvalidDomainException("INVALID_CONTENT", "Configuration content cannot be empty.");
            }

            Content = newContent;
            UpdatedAt = DateTime.UtcNow;
        }

        public void ValidatePackage()
        {
            EnsureNotImmutable();
            Status = ConfigurationStatus.VALIDATED;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Sign(string signature, string signerIdentity)
        {
            EnsureNotImmutable();

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new InvalidDomainException("INVALID_SIGNATURE", "Signature cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(signerIdentity))
            {
                throw new InvalidDomainException("INVALID_SIGNER", "Signer identity cannot be empty.");
            }

            Signature = signature.Trim();
            SignerIdentity = signerIdentity.Trim();
            SignedAt = DateTime.UtcNow;
            Status = ConfigurationStatus.SIGNED;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Publish(string publishedBy)
        {
            if (string.IsNullOrWhiteSpace(publishedBy))
            {
                throw new InvalidDomainException("INVALID_PUBLISHED_BY", "PublishedBy identity is required.");
            }

            Status = ConfigurationStatus.PUBLISHED;
            PublishedAt = DateTime.UtcNow;
            PublishedBy = publishedBy.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate()
        {
            Status = ConfigurationStatus.ACTIVE;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Supersede()
        {
            Status = ConfigurationStatus.SUPERSEDED;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Revoke()
        {
            Status = ConfigurationStatus.REVOKED;
            UpdatedAt = DateTime.UtcNow;
        }

        public void EnsureNotImmutable()
        {
            if (Status.IsImmutable())
            {
                throw new InvalidDomainException("PACKAGE_IMMUTABLE", $"Configuration package version {Version} is immutable in state '{Status}' and cannot be modified.");
            }
        }
    }
}
