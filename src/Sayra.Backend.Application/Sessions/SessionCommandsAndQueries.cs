using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Application.Sessions
{
    public record StartSessionCommand(
        Guid GamerId,
        Guid WorkstationId,
        Guid? ReservationId = null) : ICommand<SessionResponseDto>;

    public record PauseSessionCommand(
        Guid SessionId) : ICommand<SessionResponseDto>;

    public record ResumeSessionCommand(
        Guid SessionId) : ICommand<SessionResponseDto>;

    public record StopSessionCommand(
        Guid SessionId) : ICommand<SessionResponseDto>;

    public record CancelSessionCommand(
        Guid SessionId) : ICommand<SessionResponseDto>;

    public record TerminateSessionCommand(
        Guid SessionId,
        string? Reason = null) : ICommand<SessionResponseDto>;

    public record GetSessionQuery(
        Guid SessionId) : IQuery<SessionResponseDto>;

    public record GetActiveSessionByWorkstationQuery(
        Guid WorkstationId) : IQuery<SessionResponseDto?>;

    public record GetActiveSessionByGamerQuery(
        Guid GamerId) : IQuery<SessionResponseDto?>;
}
