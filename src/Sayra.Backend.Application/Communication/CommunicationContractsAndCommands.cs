using System;
using System.Collections.Generic;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Communication
{
    public record CommunicationSessionDto(
        Guid Id,
        string ConnectionId,
        string? PcId,
        Guid? WorkstationId,
        string State,
        string HeartbeatStatus,
        DateTime ConnectedAt,
        DateTime? LastHeartbeatAt,
        DateTime LastActivityAt,
        DateTime? AuthenticatedAt,
        DateTime? ActivatedAt,
        DateTime? DisconnectedAt,
        DateTime? TerminatedAt,
        string? DisconnectReason,
        string? RemoteIpAddress,
        string? Hostname)
    {
        public static CommunicationSessionDto FromEntity(CommunicationSession session)
        {
            return new CommunicationSessionDto(
                session.Id,
                session.ConnectionId,
                session.PcId,
                session.WorkstationId,
                session.State.ToString(),
                session.HeartbeatStatus.ToString(),
                session.ConnectedAt,
                session.LastHeartbeatAt,
                session.LastActivityAt,
                session.AuthenticatedAt,
                session.ActivatedAt,
                session.DisconnectedAt,
                session.TerminatedAt,
                session.DisconnectReason,
                session.RemoteIpAddress,
                session.Hostname);
        }
    }

    public record EstablishConnectionCommand(
        string ConnectionId,
        string? RemoteIpAddress = null,
        string? Hostname = null) : ICommand<CommunicationSessionDto>;

    public record AuthenticateConnectionCommand(
        string ConnectionId,
        string PcId,
        Guid? WorkstationId = null) : ICommand<CommunicationSessionDto>;

    public record ActivateConnectionCommand(
        string ConnectionId) : ICommand<CommunicationSessionDto>;

    public record ProcessHeartbeatCommand(
        string ConnectionId,
        DateTime? Timestamp = null) : ICommand<CommunicationSessionDto>;

    public record DisconnectConnectionCommand(
        string ConnectionId,
        string Reason = "Normal Closure") : ICommand<CommunicationSessionDto>;

    public record TerminateCommunicationSessionCommand(
        string ConnectionId,
        string Reason = "Server Initiated") : ICommand<CommunicationSessionDto>;

    public record GetCommunicationSessionByIdQuery(
        Guid SessionId) : IQuery<CommunicationSessionDto?>;

    public record GetCommunicationSessionByConnectionIdQuery(
        string ConnectionId) : IQuery<CommunicationSessionDto?>;

    public record GetCommunicationSessionByPcIdQuery(
        string PcId) : IQuery<CommunicationSessionDto?>;

    public record GetActiveCommunicationSessionsQuery() : IQuery<IReadOnlyList<CommunicationSessionDto>>;
}
