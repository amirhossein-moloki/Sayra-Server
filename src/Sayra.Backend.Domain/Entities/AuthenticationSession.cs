using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain.Entities
{
    public class AuthenticationSession : BaseEntity
    {
        public const string StatusActive = "ACTIVE";
        public const string StatusExpired = "EXPIRED";
        public const string StatusRevoked = "REVOKED";

        public string SessionToken { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public Guid? GamerId { get; set; }
        public string? PcId { get; set; }
        public Guid? DeviceId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevocationReason { get; set; }
        public string Status { get; set; } = StatusActive;
        public string? CreatedBy { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public bool IsActive(DateTime utcNow)
        {
            if (RevokedAt.HasValue || string.Equals(Status, StatusRevoked, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (utcNow >= ExpiresAt || string.Equals(Status, StatusExpired, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(Status, StatusActive, StringComparison.OrdinalIgnoreCase);
        }

        public void Touch(DateTime utcNow)
        {
            if (!IsActive(utcNow))
            {
                return;
            }

            LastActivityAt = utcNow;
            UpdatedAt = utcNow;
        }

        public void Revoke(string reason, DateTime utcNow)
        {
            if (string.Equals(Status, StatusRevoked, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Status = StatusRevoked;
            RevokedAt = utcNow;
            RevocationReason = reason;
            UpdatedAt = utcNow;
        }

        public void Expire(DateTime utcNow)
        {
            if (utcNow < ExpiresAt || string.Equals(Status, StatusRevoked, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Status = StatusExpired;
            UpdatedAt = utcNow;
        }

        public void NormalizeAndValidate()
        {
            SessionToken = SessionToken?.Trim() ?? string.Empty;
            PcId = string.IsNullOrWhiteSpace(PcId) ? null : PcId.Trim();
            CreatedBy = string.IsNullOrWhiteSpace(CreatedBy) ? null : CreatedBy.Trim();
            IpAddress = string.IsNullOrWhiteSpace(IpAddress) ? null : IpAddress.Trim();
            UserAgent = string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent.Trim();

            if (string.IsNullOrWhiteSpace(SessionToken))
            {
                throw new InvalidDomainException("INVALID_SESSION_TOKEN", "Authentication session token cannot be empty.");
            }

            if (!UserId.HasValue && !GamerId.HasValue)
            {
                throw new InvalidDomainException("INVALID_PRINCIPAL", "Authentication session must be associated with a User or Gamer.");
            }

            if (ExpiresAt <= CreatedAt)
            {
                throw new InvalidDomainException("INVALID_EXPIRATION", "Authentication session expiration must be greater than creation time.");
            }
        }
    }
}
