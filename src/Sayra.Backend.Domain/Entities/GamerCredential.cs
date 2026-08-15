using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class GamerCredential : BaseEntity
    {
        public Guid GamerEntityId { get; set; }
        public string CredentialType { get; set; } = "Password";
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string HashAlgorithm { get; set; } = "PBKDF2";
        public int FailedAttemptCount { get; set; } = 0;
        public bool IsLocked { get; set; } = false;
        public DateTime? LockoutEnd { get; set; }
        public DateTime? LastPasswordChangedAt { get; set; }

        public void SetPassword(string hash, string salt, string algorithm = "PBKDF2")
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
            HashAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? "PBKDF2" : algorithm.Trim();
            LastPasswordChangedAt = DateTime.UtcNow;
            ResetFailedAttempts();
            UpdatedAt = DateTime.UtcNow;
        }

        public bool IsCurrentlyLockedOut()
        {
            if (!IsLocked) return false;
            if (LockoutEnd.HasValue && LockoutEnd.Value <= DateTime.UtcNow)
            {
                Unlock();
                return false;
            }
            return true;
        }

        public void RecordFailedAttempt(int maxAttempts = 5, TimeSpan? lockoutDuration = null)
        {
            FailedAttemptCount++;
            if (FailedAttemptCount >= maxAttempts)
            {
                IsLocked = true;
                LockoutEnd = DateTime.UtcNow.Add(lockoutDuration ?? TimeSpan.FromMinutes(15));
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void ResetFailedAttempts()
        {
            FailedAttemptCount = 0;
            IsLocked = false;
            LockoutEnd = null;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unlock()
        {
            IsLocked = false;
            LockoutEnd = null;
            FailedAttemptCount = 0;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
