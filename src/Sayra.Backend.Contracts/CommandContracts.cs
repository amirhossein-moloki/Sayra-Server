using System;

namespace Sayra.Backend.Contracts
{
    public class CommandMessage<TPayload>
    {
        public string CommandId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public TPayload? Payload { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class StartSessionPayload
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public decimal RatePerHour { get; set; }
    }

    public class RunAppPayload
    {
        public string ExecutablePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }

    public class KillAppPayload
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
    }

    public class SessionCommandPayload
    {
        public string Action { get; set; } = string.Empty; // START, PAUSE, RESUME, EXTEND, STOP
        public Guid SessionId { get; set; }
        public Guid GamerId { get; set; }
        public Guid WorkstationId { get; set; }
        public Guid? ReservationId { get; set; }
        public int? AdditionalMinutes { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class SessionStateUpdateMessage
    {
        public string MessageType { get; set; } = "SESSION_STATE_UPDATE";
        public Guid SessionId { get; set; }
        public Guid GamerId { get; set; }
        public Guid WorkstationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public TimeSpan ConsumedDuration { get; set; }
        public TimeSpan? RemainingDuration { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
