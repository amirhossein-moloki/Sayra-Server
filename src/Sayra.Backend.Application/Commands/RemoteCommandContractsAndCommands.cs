using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Commands
{
    public record CreateRemoteCommand(
        string CommandType,
        Guid TargetWorkstationId,
        string TargetPcId,
        string RequestedBy,
        Sayra.Backend.Application.Abstractions.Security.UserPrincipal? CallerPrincipal = null,
        string? Payload = null,
        int? TtlSeconds = null,
        int Priority = 0,
        string? CorrelationId = null,
        bool IsIdempotent = true) : ICommand<RemoteCommandResponseDto>;

    public record ProcessCommandAckCommand(
        string CommandId,
        string PcId,
        string Status,
        string? FailureReason = null) : ICommand<bool>;

    public record ProcessCommandResultCommand(
        string CommandId,
        string PcId,
        string Status,
        string? Message = null,
        string? ErrorCode = null,
        string? ResultPayload = null) : ICommand<bool>;

    public record CancelRemoteCommand(
        string CommandId,
        string RequestedBy,
        string? Reason = null) : ICommand<bool>;

    public record GetRemoteCommandByIdQuery(Guid Id) : IQuery<RemoteCommandResponseDto?>;

    public record GetRemoteCommandByCommandIdQuery(string CommandId) : IQuery<RemoteCommandResponseDto?>;

    public record GetRemoteCommandsByWorkstationQuery(Guid WorkstationId) : IQuery<IReadOnlyList<RemoteCommandResponseDto>>;
}
