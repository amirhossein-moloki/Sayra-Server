using System;

namespace Sayra.Backend.Contracts
{
    public class StartSessionRequestDto
    {
        public Guid GamerId { get; set; }
        public Guid WorkstationId { get; set; }
        public Guid? ReservationId { get; set; }
    }

    public class TerminateSessionRequestDto
    {
        public string? Reason { get; set; }
    }

    public class SessionResponseDto
    {
        public Guid SessionId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SiteId { get; set; }
        public Guid WorkstationId { get; set; }
        public Guid GamerId { get; set; }
        public Guid? ReservationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
