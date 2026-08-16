using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class Session : BaseEntity
    {
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid WorkstationId { get; set; }
        public Guid GamerId { get; set; }
        public Guid? ReservationId { get; set; }
        public Guid? PricingPlanId { get; set; }

        public string Status { get; set; } = "IDLE";

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PausedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public uint RowVersion { get; set; }

        public Guid SessionId
        {
            get => Id;
            set => Id = value;
        }

        public void TransitionTo(string newStatus)
        {
            var target = (newStatus ?? string.Empty).Trim().ToUpperInvariant();
            var current = (Status ?? string.Empty).Trim().ToUpperInvariant();

            if (target == current) return;

            if (target != "IDLE" && target != "STARTING" && target != "ACTIVE" &&
                target != "PAUSED" && target != "ENDING" && target != "ENDED" &&
                target != "EXPIRED" && target != "CANCELLED" && target != "TERMINATED")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid session status: {newStatus}");
            }

            bool isValid = false;

            switch (current)
            {
                case "IDLE":
                    isValid = (target == "STARTING" || target == "ACTIVE" || target == "CANCELLED");
                    break;
                case "STARTING":
                    isValid = (target == "ACTIVE" || target == "CANCELLED" || target == "TERMINATED");
                    break;
                case "ACTIVE":
                    isValid = (target == "PAUSED" || target == "ENDING" || target == "ENDED" ||
                               target == "EXPIRED" || target == "CANCELLED" || target == "TERMINATED");
                    break;
                case "PAUSED":
                    isValid = (target == "ACTIVE" || target == "ENDING" || target == "ENDED" ||
                               target == "CANCELLED" || target == "TERMINATED");
                    break;
                case "ENDING":
                    isValid = (target == "ENDED" || target == "CANCELLED" || target == "TERMINATED");
                    break;
                default:
                    // ENDED, EXPIRED, CANCELLED, TERMINATED are terminal states
                    isValid = false;
                    break;
            }

            if (!isValid)
            {
                throw new InvalidDomainException("INVALID_TRANSITION", $"Cannot transition directly from {current} to {target}.");
            }

            if (target == "PAUSED")
            {
                PausedAt = DateTime.UtcNow;
            }
            else if (target == "ACTIVE" && current == "PAUSED")
            {
                PausedAt = null;
            }

            if (target == "ENDED" || target == "EXPIRED" || target == "CANCELLED" || target == "TERMINATED")
            {
                EndedAt = DateTime.UtcNow;
            }

            Status = target;
            UpdatedAt = DateTime.UtcNow;
        }

        public void NormalizeAndValidate()
        {
            if (OrganizationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_ORGANIZATION_ID", "OrganizationId is required for Session.");
            }

            if (SiteId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_SITE_ID", "SiteId is required for Session.");
            }

            if (WorkstationId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_WORKSTATION_ID", "WorkstationId is required for Session.");
            }

            if (GamerId == Guid.Empty)
            {
                throw new InvalidDomainException("INVALID_GAMER_ID", "GamerId is required for Session.");
            }

            StartedAt = StartedAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(StartedAt, DateTimeKind.Utc) : StartedAt.ToUniversalTime();

            if (PausedAt.HasValue)
            {
                PausedAt = PausedAt.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(PausedAt.Value, DateTimeKind.Utc) : PausedAt.Value.ToUniversalTime();
            }

            if (EndedAt.HasValue)
            {
                EndedAt = EndedAt.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(EndedAt.Value, DateTimeKind.Utc) : EndedAt.Value.ToUniversalTime();
            }

            var statusTrimmed = (Status ?? string.Empty).Trim().ToUpperInvariant();
            if (statusTrimmed != "IDLE" && statusTrimmed != "STARTING" && statusTrimmed != "ACTIVE" &&
                statusTrimmed != "PAUSED" && statusTrimmed != "ENDING" && statusTrimmed != "ENDED" &&
                statusTrimmed != "EXPIRED" && statusTrimmed != "CANCELLED" && statusTrimmed != "TERMINATED")
            {
                throw new InvalidDomainException("INVALID_STATUS", $"Invalid Session status: {Status}");
            }

            Status = statusTrimmed;
        }

        public bool IsActive()
        {
            return Status == "STARTING" || Status == "ACTIVE" || Status == "PAUSED" || Status == "ENDING";
        }
    }
}
