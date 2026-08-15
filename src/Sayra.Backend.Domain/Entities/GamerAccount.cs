using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class GamerAccount : BaseEntity
    {
        public Guid GamerEntityId { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // Active, Frozen, Closed
        public string Currency { get; set; } = "SAY";
        public decimal Balance { get; set; } = 0.00m;
        public decimal BonusBalance { get; set; } = 0.00m;

        // Optimistic concurrency token
        public uint RowVersion { get; set; }

        public void NormalizeAndValidate()
        {
            if (GamerEntityId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GAMER_ID", "GamerEntityId is required for GamerAccount.");
            }

            if (string.IsNullOrWhiteSpace(AccountNumber))
            {
                AccountNumber = $"ACC-{GamerEntityId.ToString("N").Substring(0, 8).ToUpperInvariant()}";
            }
            AccountNumber = AccountNumber.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Currency))
            {
                Currency = "SAY";
            }
            Currency = Currency.Trim().ToUpperInvariant();

            var statusTrimmed = (Status ?? string.Empty).Trim();
            if (statusTrimmed.Equals("Active", StringComparison.OrdinalIgnoreCase)) Status = "Active";
            else if (statusTrimmed.Equals("Frozen", StringComparison.OrdinalIgnoreCase)) Status = "Frozen";
            else if (statusTrimmed.Equals("Closed", StringComparison.OrdinalIgnoreCase)) Status = "Closed";
            else
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid GamerAccount status: {Status}");
            }
        }

        public bool CanTransact()
        {
            return Status != null && Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
        }

        public void Freeze()
        {
            Status = "Frozen";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unfreeze()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Close()
        {
            Status = "Closed";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
