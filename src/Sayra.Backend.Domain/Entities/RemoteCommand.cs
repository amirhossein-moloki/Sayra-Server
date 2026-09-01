using System;
using Sayra.Backend.Domain.Events;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public class RemoteCommand : BaseEntity
    {
        public string CommandId { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
        public Guid TargetWorkstationId { get; set; }
        public string TargetPcId { get; set; } = string.Empty;
        public string? TargetConnectionId { get; set; }
        public Guid? TargetSessionId { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ExecutingAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = "CREATED";
        public int Priority { get; set; } = 0;
        public string CorrelationId { get; set; } = string.Empty;
        public string? Payload { get; set; }
        public string? ResultPayload { get; set; }
        public string? ErrorCode { get; set; }
        public string? FailureReason { get; set; }
        public bool IsIdempotent { get; set; } = true;
        public uint RowVersion { get; set; }

        public RemoteCommand()
        {
        }

        public static RemoteCommand Create(
            string commandId,
            string commandType,
            Guid targetWorkstationId,
            string targetPcId,
            string requestedBy,
            string? payload = null,
            TimeSpan? ttl = null,
            int priority = 0,
            string? correlationId = null,
            bool isIdempotent = true)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new InvalidDomainException("INVALID_COMMAND_ID", "CommandId is required.");

            if (string.IsNullOrWhiteSpace(commandType))
                throw new InvalidDomainException("INVALID_COMMAND_TYPE", "CommandType is required.");

            if (string.IsNullOrWhiteSpace(targetPcId))
                throw new InvalidDomainException("INVALID_TARGET_PC_ID", "TargetPcId is required.");

            if (string.IsNullOrWhiteSpace(requestedBy))
                throw new InvalidDomainException("INVALID_REQUESTED_BY", "RequestedBy is required.");

            var command = new RemoteCommand
            {
                Id = Guid.NewGuid(),
                CommandId = commandId.Trim(),
                CommandType = commandType.Trim().ToUpperInvariant(),
                TargetWorkstationId = targetWorkstationId,
                TargetPcId = targetPcId.Trim().ToUpperInvariant(),
                RequestedBy = requestedBy.Trim(),
                Payload = payload,
                Status = "CREATED",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.UtcNow.AddMinutes(5),
                Priority = priority,
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId.Trim(),
                IsIdempotent = isIdempotent
            };

            command.AddDomainEvent(new RemoteCommandCreatedEvent(
                command.CommandId,
                command.CommandType,
                command.TargetWorkstationId,
                command.TargetPcId,
                command.RequestedBy,
                command.CreatedAt,
                command.CorrelationId));

            return command;
        }

        public bool IsTerminal => IsTerminalState(Status);

        public static bool IsTerminalState(string status)
        {
            var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
            return normalized is "SUCCEEDED" or "FAILED" or "EXPIRED" or "CANCELLED" or "DELIVERY_TIMEOUT" or "EXECUTION_TIMEOUT" or "REJECTED";
        }

        public void TransitionTo(string newStatus, string? detail = null, string? errorCode = null, string? resultPayload = null)
        {
            var target = (newStatus ?? string.Empty).Trim().ToUpperInvariant();
            var current = (Status ?? string.Empty).Trim().ToUpperInvariant();

            if (target == current) return;

            if (IsTerminal)
            {
                throw new InvalidDomainException("INVALID_TRANSITION", $"Cannot transition remote command {CommandId} from terminal state {current} to {target}.");
            }

            ValidateStateTransition(current, target);

            Status = target;
            UpdatedAt = DateTime.UtcNow;

            switch (target)
            {
                case "QUEUED":
                    break;
                case "SENDING":
                    break;
                case "DELIVERED":
                    DeliveredAt = DateTime.UtcNow;
                    break;
                case "ACKNOWLEDGED":
                    AcknowledgedAt = DateTime.UtcNow;
                    break;
                case "EXECUTING":
                    ExecutingAt = DateTime.UtcNow;
                    break;
                case "SUCCEEDED":
                    CompletedAt = DateTime.UtcNow;
                    ResultPayload = resultPayload ?? ResultPayload;
                    break;
                case "FAILED":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? ErrorCode ?? "EXECUTION_FAILED";
                    FailureReason = detail ?? FailureReason;
                    ResultPayload = resultPayload ?? ResultPayload;
                    break;
                case "EXPIRED":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? "COMMAND_EXPIRED";
                    FailureReason = detail ?? "Command expired before completion.";
                    break;
                case "CANCELLED":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? "COMMAND_CANCELLED";
                    FailureReason = detail ?? "Command was cancelled.";
                    break;
                case "DELIVERY_TIMEOUT":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? "DELIVERY_TIMEOUT";
                    FailureReason = detail ?? "Command delivery timed out without acknowledgement.";
                    break;
                case "EXECUTION_TIMEOUT":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? "EXECUTION_TIMEOUT";
                    FailureReason = detail ?? "Command execution timed out without result.";
                    break;
                case "REJECTED":
                    CompletedAt = DateTime.UtcNow;
                    ErrorCode = errorCode ?? "COMMAND_REJECTED";
                    FailureReason = detail ?? "Command was rejected by target client or backend validation.";
                    break;
            }

            AddDomainEvent(new RemoteCommandStateChangedEvent(
                CommandId,
                CommandType,
                TargetWorkstationId,
                TargetPcId,
                current,
                target,
                DateTime.UtcNow,
                CorrelationId));

            if (IsTerminalState(target))
            {
                AddDomainEvent(new RemoteCommandCompletedEvent(
                    CommandId,
                    CommandType,
                    TargetWorkstationId,
                    TargetPcId,
                    target,
                    ErrorCode,
                    FailureReason,
                    DateTime.UtcNow,
                    CorrelationId));
            }
        }

        private static void ValidateStateTransition(string current, string target)
        {
            bool valid = current switch
            {
                "CREATED" => target is "QUEUED" or "SENDING" or "CANCELLED" or "EXPIRED" or "REJECTED",
                "QUEUED" => target is "SENDING" or "CANCELLED" or "EXPIRED" or "REJECTED" or "DELIVERY_TIMEOUT",
                "SENDING" => target is "DELIVERED" or "ACKNOWLEDGED" or "REJECTED" or "DELIVERY_TIMEOUT" or "EXECUTION_TIMEOUT" or "FAILED" or "CANCELLED",
                "DELIVERED" => target is "ACKNOWLEDGED" or "EXECUTING" or "SUCCEEDED" or "FAILED" or "EXECUTION_TIMEOUT" or "REJECTED",
                "ACKNOWLEDGED" => target is "EXECUTING" or "SUCCEEDED" or "FAILED" or "EXECUTION_TIMEOUT" or "REJECTED",
                "EXECUTING" => target is "SUCCEEDED" or "FAILED" or "EXECUTION_TIMEOUT" or "REJECTED",
                _ => false
            };

            if (!valid)
            {
                throw new InvalidDomainException("INVALID_TRANSITION", $"Invalid state transition for command from {current} to {target}.");
            }
        }
    }
}
