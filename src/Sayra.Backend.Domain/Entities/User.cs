using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class User : BaseEntity
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ExternalId { get; set; }

        public UserRole Role { get; set; } = UserRole.Gamer;
        public UserAccountState Status { get; set; } = UserAccountState.Pending;

        public DateTime? LastLoginAt { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }

        public Guid? GamerEntityId { get; set; }
        public Guid? OrganizationEntityId { get; set; }
        public Guid? SiteEntityId { get; set; }
        public uint RowVersion { get; set; }

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                throw new InvalidDomainException("INVALID_USERNAME", "Username is required.");
            }
            Username = Username.Trim();

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = Username;
            }
            else
            {
                DisplayName = DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Email))
            {
                Email = Email.Trim().ToLowerInvariant();
                if (!Email.Contains("@") || !Email.Contains("."))
                {
                    throw new InvalidDomainException("INVALID_EMAIL", "Email format is invalid.");
                }
            }
            else
            {
                Email = null;
            }

            PhoneNumber = string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim();
            ExternalId = string.IsNullOrWhiteSpace(ExternalId) ? null : ExternalId.Trim();

            if (string.IsNullOrWhiteSpace(UserId))
            {
                UserId = $"USR-{Id.ToString("N").Substring(0, 8).ToUpperInvariant()}";
            }
            else
            {
                UserId = UserId.Trim().ToUpperInvariant();
            }
        }

        public void TransitionTo(UserAccountState newState)
        {
            if (Status == newState) return;

            // Reject transitions out of Deleted state
            if (Status == UserAccountState.Deleted)
            {
                throw new InvalidDomainException("INVALID_ACCOUNT_STATE_TRANSITION", $"Cannot transition account from {Status} to {newState}. Deleted accounts cannot be reactivated.");
            }

            bool isValidTransition = (Status, newState) switch
            {
                (UserAccountState.Pending, UserAccountState.Active) => true,
                (UserAccountState.Active, UserAccountState.Suspended) => true,
                (UserAccountState.Active, UserAccountState.Locked) => true,
                (UserAccountState.Active, UserAccountState.Disabled) => true,
                (UserAccountState.Active, UserAccountState.Deleted) => true,
                (UserAccountState.Suspended, UserAccountState.Active) => true,
                (UserAccountState.Suspended, UserAccountState.Deleted) => true,
                (UserAccountState.Locked, UserAccountState.Active) => true,
                (UserAccountState.Locked, UserAccountState.Deleted) => true,
                (UserAccountState.Disabled, UserAccountState.Active) => true,
                (UserAccountState.Disabled, UserAccountState.Deleted) => true,
                _ => false
            };

            if (!isValidTransition)
            {
                throw new InvalidDomainException("INVALID_ACCOUNT_STATE_TRANSITION", $"Invalid account state transition from {Status} to {newState}.");
            }

            Status = newState;
            if (newState == UserAccountState.Active)
            {
                LockedUntil = null;
                FailedLoginAttempts = 0;
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public bool IsCurrentlyLockedOut()
        {
            if (Status != UserAccountState.Locked && LockedUntil == null) return false;

            if (LockedUntil.HasValue && LockedUntil.Value <= DateTime.UtcNow)
            {
                Unlock();
                return false;
            }

            return Status == UserAccountState.Locked || (LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow);
        }

        public void RecordFailedLoginAttempt(int maxAttempts = 5, TimeSpan? lockoutDuration = null)
        {
            FailedLoginAttempts++;
            if (FailedLoginAttempts >= maxAttempts)
            {
                Status = UserAccountState.Locked;
                LockedUntil = DateTime.UtcNow.Add(lockoutDuration ?? TimeSpan.FromMinutes(15));
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void ResetFailedLoginAttempts()
        {
            FailedLoginAttempts = 0;
            LockedUntil = null;
            if (Status == UserAccountState.Locked)
            {
                Status = UserAccountState.Active;
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unlock()
        {
            LockedUntil = null;
            FailedLoginAttempts = 0;
            if (Status == UserAccountState.Locked)
            {
                Status = UserAccountState.Active;
            }
            UpdatedAt = DateTime.UtcNow;
        }

        public void RecordSuccessfulLogin()
        {
            ResetFailedLoginAttempts();
            LastLoginAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
