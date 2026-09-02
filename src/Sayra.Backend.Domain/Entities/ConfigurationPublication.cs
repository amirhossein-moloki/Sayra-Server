using System;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Domain
{
    public class ConfigurationPublication : BaseEntity
    {
        public Guid ConfigurationPackageId { get; private set; }
        public long VersionNumber { get; private set; }
        public string Version { get; private set; } = string.Empty;
        public Guid ConfigurationTargetId { get; private set; }
        public Guid OrganizationId { get; private set; }

        public ConfigurationLifecycleState Status { get; private set; } = ConfigurationLifecycleState.Draft;

        public string IssuedBy { get; private set; } = "system";
        public DateTime? PublishedAt { get; private set; }
        public DateTime? ActivatedAt { get; private set; }
        public DateTime? SupersededAt { get; private set; }
        public Guid? SupersededByPublicationId { get; private set; }

        public DateTime? RevokedAt { get; private set; }
        public string? RevokedBy { get; private set; }
        public string? RevocationReason { get; private set; }

        public string? CorrelationId { get; private set; }
        public string? Notes { get; private set; }
        public string? IdempotencyKey { get; private set; }

        // Rollback Metadata
        public bool IsRollback { get; private set; }
        public long? SourceVersionNumber { get; private set; }
        public long? FailedVersionNumber { get; private set; }
        public Guid? SourcePublicationId { get; private set; }

        // Cryptographic Metadata Snapshot
        public string ConfigurationHash { get; private set; } = string.Empty;
        public string Signature { get; private set; } = string.Empty;
        public string SignatureAlgorithm { get; private set; } = "RSA-SHA256";
        public string SigningKeyId { get; private set; } = string.Empty;

        // Concurrency Token
        public uint RowVersion { get; set; }

        // EF Core Constructor
        public ConfigurationPublication()
        {
        }

        public static ConfigurationPublication Create(
            Guid packageId,
            long versionNumber,
            string version,
            Guid targetId,
            Guid organizationId,
            string hash,
            string signature,
            string keyId,
            string algorithm = "RSA-SHA256",
            string issuedBy = "system",
            string? notes = null,
            string? correlationId = null,
            string? idempotencyKey = null,
            bool isRollback = false,
            long? sourceVersionNumber = null,
            long? failedVersionNumber = null,
            Guid? sourcePublicationId = null)
        {
            if (packageId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_PACKAGE_ID", "ConfigurationPackageId is required for publication.");
            }

            if (targetId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_TARGET_ID", "ConfigurationTargetId is required for publication.");
            }

            if (organizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for publication.");
            }

            if (versionNumber <= 0)
            {
                throw new InvalidDomainException("INVALID_VERSION_NUMBER", "VersionNumber must be greater than 0.");
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new InvalidDomainException("INVALID_CRYPTOGRAPHIC_HASH", "ConfigurationHash cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new InvalidDomainException("INVALID_SIGNATURE", "Signature cannot be empty for publication.");
            }

            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new InvalidDomainException("INVALID_KEY_ID", "SigningKeyId cannot be empty.");
            }

            var pub = new ConfigurationPublication
            {
                Id = Guid.NewGuid(),
                ConfigurationPackageId = packageId,
                VersionNumber = versionNumber,
                Version = string.IsNullOrWhiteSpace(version) ? $"v{versionNumber}" : version.Trim(),
                ConfigurationTargetId = targetId,
                OrganizationId = organizationId,
                Status = ConfigurationLifecycleState.Signed, // Created as validated & signed from package
                IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "system" : issuedBy.Trim(),
                ConfigurationHash = hash.Trim().ToLowerInvariant(),
                Signature = signature.Trim(),
                SignatureAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? "RSA-SHA256" : algorithm.Trim(),
                SigningKeyId = keyId.Trim(),
                Notes = notes?.Trim(),
                CorrelationId = correlationId?.Trim(),
                IdempotencyKey = idempotencyKey?.Trim(),
                IsRollback = isRollback,
                SourceVersionNumber = sourceVersionNumber,
                FailedVersionNumber = failedVersionNumber,
                SourcePublicationId = sourcePublicationId,
                CreatedAt = DateTime.UtcNow
            };

            return pub;
        }

        public void Validate()
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Validated);
            Status = ConfigurationLifecycleState.Validated;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Sign(string hash, string signature, string keyId, string algorithm = "RSA-SHA256")
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Signed);

            if (string.IsNullOrWhiteSpace(hash)) throw new InvalidDomainException("INVALID_HASH", "Hash required.");
            if (string.IsNullOrWhiteSpace(signature)) throw new InvalidDomainException("INVALID_SIGNATURE", "Signature required.");
            if (string.IsNullOrWhiteSpace(keyId)) throw new InvalidDomainException("INVALID_KEY_ID", "SigningKeyId required.");

            ConfigurationHash = hash.Trim().ToLowerInvariant();
            Signature = signature.Trim();
            SignatureAlgorithm = algorithm.Trim();
            SigningKeyId = keyId.Trim();

            Status = ConfigurationLifecycleState.Signed;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Publish(string actor = "system")
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Published);

            if (string.IsNullOrWhiteSpace(Signature) || string.IsNullOrWhiteSpace(ConfigurationHash))
            {
                throw new InvalidDomainException("UNSIGNED_PUBLICATION_REJECTED", "Unsigned configuration cannot be published.");
            }

            Status = ConfigurationLifecycleState.Published;
            PublishedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(actor))
            {
                IssuedBy = actor.Trim();
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate(string actor = "system")
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Active);

            Status = ConfigurationLifecycleState.Active;
            ActivatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(actor))
            {
                IssuedBy = actor.Trim();
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void Supersede(Guid supersededByPublicationId)
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Superseded);

            if (supersededByPublicationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SUPERSEDING_ID", "SupersededByPublicationId is required.");
            }

            Status = ConfigurationLifecycleState.Superseded;
            SupersededByPublicationId = supersededByPublicationId;
            SupersededAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Revoke(string actor, string reason)
        {
            ConfigurationLifecycleValidator.ValidateTransition(Status, ConfigurationLifecycleState.Revoked);

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidDomainException("REVOCATION_REASON_REQUIRED", "Revocation reason is required.");
            }

            Status = ConfigurationLifecycleState.Revoked;
            RevokedAt = DateTime.UtcNow;
            RevokedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
            RevocationReason = reason.Trim();
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
