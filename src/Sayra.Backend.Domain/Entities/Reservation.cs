using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Reservation : BaseEntity
    {
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid GamerId { get; set; }
        public Guid? WorkstationId { get; set; }
        public Guid? ZoneId { get; set; }

        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }

        public string Status { get; set; } = "PENDING";
        public decimal ReservedAmount { get; set; }

        public Guid ReservationId
        {
            get => Id;
            set => Id = value;
        }

        public void TransitionTo(string newStatus)
        {
            var target = (newStatus ?? string.Empty).Trim().ToUpperInvariant();
            var current = (Status ?? string.Empty).Trim().ToUpperInvariant();

            if (target == current) return;

            if (target != "PENDING" && target != "CONFIRMED" && target != "ACTIVE" &&
                target != "COMPLETED" && target != "CANCELLED" && target != "EXPIRED" && target != "NO_SHOW")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid status: {newStatus}");
            }

            bool isValid = false;

            switch (current)
            {
                case "PENDING":
                    isValid = (target == "CONFIRMED" || target == "CANCELLED" || target == "EXPIRED" || target == "NO_SHOW");
                    break;
                case "CONFIRMED":
                    isValid = (target == "ACTIVE" || target == "CANCELLED" || target == "EXPIRED" || target == "NO_SHOW");
                    break;
                case "ACTIVE":
                    isValid = (target == "COMPLETED" || target == "CANCELLED");
                    break;
                default:
                    // COMPLETED, CANCELLED, EXPIRED, NO_SHOW cannot transition to ACTIVE or any other state
                    isValid = false;
                    break;
            }

            if (!isValid)
            {
                throw new InvalidDomainException("INVALID_TRANSITION", $"Cannot transition directly from {current} to {target}.");
            }

            Status = target;
            UpdatedAt = DateTime.UtcNow;
        }

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Reservation.");
            }

            if (SiteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for Reservation.");
            }

            if (GamerId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GAMER_ID", "GamerId is required for Reservation.");
            }

            if (ReservedAmount < 0)
            {
                throw new InvalidDomainException("INVALID_RESERVED_AMOUNT", "ReservedAmount cannot be negative.");
            }

            StartTimeUtc = StartTimeUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(StartTimeUtc, DateTimeKind.Utc) : StartTimeUtc.ToUniversalTime();
            EndTimeUtc = EndTimeUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(EndTimeUtc, DateTimeKind.Utc) : EndTimeUtc.ToUniversalTime();

            if (EndTimeUtc <= StartTimeUtc)
            {
                throw new InvalidDomainException("INVALID_TIME_RANGE", "EndTimeUtc must be after StartTimeUtc.");
            }

            var statusTrimmed = (Status ?? string.Empty).Trim().ToUpperInvariant();
            if (statusTrimmed != "PENDING" && statusTrimmed != "CONFIRMED" && statusTrimmed != "ACTIVE" &&
                statusTrimmed != "COMPLETED" && statusTrimmed != "CANCELLED" && statusTrimmed != "EXPIRED" && statusTrimmed != "NO_SHOW")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Reservation status: {Status}");
            }

            Status = statusTrimmed;
        }

        public bool IsActiveOrConfirmed()
        {
            return Status == "PENDING" || Status == "CONFIRMED" || Status == "ACTIVE";
        }
    }
}
