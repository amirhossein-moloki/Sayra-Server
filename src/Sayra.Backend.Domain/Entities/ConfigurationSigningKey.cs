using System;

#nullable enable

namespace Sayra.Backend.Domain
{
    public enum SigningKeyStatus
    {
        Active = 0,
        Retired = 1,
        Revoked = 2
    }

    public class ConfigurationSigningKey : BaseEntity
    {
        public string KeyId { get; private set; } = string.Empty;
        public string Algorithm { get; private set; } = "RSA-SHA256";
        public string PublicKeyPem { get; private set; } = string.Empty;
        public SigningKeyStatus Status { get; private set; } = SigningKeyStatus.Active;
        public DateTime ValidFrom { get; private set; } = DateTime.UtcNow;
        public DateTime? ValidTo { get; private set; }

        // EF Core requirement
        public ConfigurationSigningKey()
        {
        }

        public static ConfigurationSigningKey Create(
            string keyId,
            string publicKeyPem,
            string algorithm = "RSA-SHA256",
            SigningKeyStatus status = SigningKeyStatus.Active,
            DateTime? validFrom = null,
            DateTime? validTo = null)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                throw new ArgumentException("KeyId cannot be null or empty.", nameof(keyId));
            }

            if (string.IsNullOrWhiteSpace(publicKeyPem))
            {
                throw new ArgumentException("PublicKeyPem cannot be null or empty.", nameof(publicKeyPem));
            }

            return new ConfigurationSigningKey
            {
                Id = Guid.NewGuid(),
                KeyId = keyId.Trim(),
                Algorithm = string.IsNullOrWhiteSpace(algorithm) ? "RSA-SHA256" : algorithm.Trim(),
                PublicKeyPem = publicKeyPem.Trim(),
                Status = status,
                ValidFrom = validFrom ?? DateTime.UtcNow,
                ValidTo = validTo,
                CreatedAt = DateTime.UtcNow
            };
        }

        public void Activate()
        {
            if (Status == SigningKeyStatus.Revoked)
            {
                throw new InvalidOperationException($"Cannot activate revoked key '{KeyId}'.");
            }
            Status = SigningKeyStatus.Active;
        }

        public void Retire()
        {
            if (Status == SigningKeyStatus.Revoked)
            {
                throw new InvalidOperationException($"Cannot retire revoked key '{KeyId}'.");
            }
            Status = SigningKeyStatus.Retired;
        }

        public void Revoke()
        {
            Status = SigningKeyStatus.Revoked;
        }
    }
}
