using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class UserCredential : BaseEntity
    {
        public Guid UserEntityId { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string HashAlgorithm { get; set; } = "Argon2id";
        public string HashParameters { get; set; } = string.Empty;
        public DateTime CredentialCreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastPasswordChangedAt { get; set; }

        public User? User { get; set; }

        public void SetPassword(string hash, string salt, string algorithm = "Argon2id", string? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new InvalidDomainException("INVALID_PASSWORD_HASH", "Password hash cannot be empty.");
            }
            if (string.IsNullOrWhiteSpace(salt))
            {
                throw new InvalidDomainException("INVALID_PASSWORD_SALT", "Password salt cannot be empty.");
            }

            PasswordHash = hash;
            PasswordSalt = salt;
            HashAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? "Argon2id" : algorithm.Trim();
            HashParameters = parameters?.Trim() ?? string.Empty;
            LastPasswordChangedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
