using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Gamer : BaseEntity
    {
        public string GamerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Status { get; set; } = "Active"; // Active, Inactive, Suspended, Banned

        public Guid? OrganizationEntityId { get; set; }
        public Guid? SiteEntityId { get; set; }

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                throw new InvalidDomainException("INVALID_USERNAME", "Username is required.");
            }
            Username = Username.Trim();

            if (string.IsNullOrWhiteSpace(Email))
            {
                throw new InvalidDomainException("INVALID_EMAIL", "Email is required.");
            }
            Email = Email.Trim().ToLowerInvariant();

            if (!Email.Contains("@") || !Email.Contains("."))
            {
                throw new InvalidDomainException("INVALID_EMAIL", "Email format is invalid.");
            }

            if (string.IsNullOrWhiteSpace(GamerId))
            {
                GamerId = $"GMR-{Id.ToString("N").Substring(0, 8).ToUpperInvariant()}";
            }
            GamerId = GamerId.Trim().ToUpperInvariant();

            PhoneNumber = string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim();
            FirstName = string.IsNullOrWhiteSpace(FirstName) ? null : FirstName.Trim();
            LastName = string.IsNullOrWhiteSpace(LastName) ? null : LastName.Trim();

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) Status = "Inactive";
            else if (statusTrimmed.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) Status = "Suspended";
            else if (statusTrimmed.Equals("Banned", StringComparison.OrdinalIgnoreCase)) Status = "Banned";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Gamer status: {Status}");
            }
        }

        public bool CanOperate()
        {
            return Status != null && Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
        }

        public void Deactivate()
        {
            Status = "Inactive";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Suspend()
        {
            Status = "Suspended";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Ban()
        {
            Status = "Banned";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Activate()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
