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

    // Remote Command Infrastructure DTOs & Envelope Contracts
    public class CreateRemoteCommandRequestDto
    {
        public string CommandType { get; set; } = string.Empty;
        public Guid TargetWorkstationId { get; set; }
        public string TargetPcId { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public object? CallerPrincipal { get; set; }
        public string? Payload { get; set; }
        public int? TtlSeconds { get; set; }
        public int Priority { get; set; } = 0;
        public string? CorrelationId { get; set; }
        public bool IsIdempotent { get; set; } = true;
    }

    public class RemoteCommandResponseDto
    {
        public Guid Id { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
        public Guid TargetWorkstationId { get; set; }
        public string TargetPcId { get; set; } = string.Empty;
        public string? TargetConnectionId { get; set; }
        public Guid? TargetSessionId { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ExecutingAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public string? Payload { get; set; }
        public string? ResultPayload { get; set; }
        public string? ErrorCode { get; set; }
        public string? FailureReason { get; set; }
        public bool IsIdempotent { get; set; }
    }

    public class CommandAckMessage
    {
        public string MessageType { get; set; } = "COMMAND_ACK";
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = "ACKNOWLEDGED"; // ACKNOWLEDGED or REJECTED
        public string? FailureReason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
    }
}
