using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Communication
{
    public class EstablishConnectionCommandHandler : ICommandHandler<EstablishConnectionCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public EstablishConnectionCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(EstablishConnectionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var existing = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (existing != null)
                {
                    return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(existing));
                }

                var session = CommunicationSession.Create(command.ConnectionId, command.RemoteIpAddress, command.Hostname);
                await _repository.AddAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("ESTABLISH_CONNECTION_FAILED", ex.Message);
            }
        }
    }

    public class AuthenticateConnectionCommandHandler : ICommandHandler<AuthenticateConnectionCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public AuthenticateConnectionCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(AuthenticateConnectionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (session == null)
                {
                    return Result<CommunicationSessionDto>.Failure("SESSION_NOT_FOUND", $"Communication session with connection id '{command.ConnectionId}' was not found.");
                }

                session.Authenticate(command.PcId, command.WorkstationId);
                await _repository.UpdateAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (InvalidDomainException ex)
            {
                return Result<CommunicationSessionDto>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("AUTHENTICATE_CONNECTION_FAILED", ex.Message);
            }
        }
    }

    public class ActivateConnectionCommandHandler : ICommandHandler<ActivateConnectionCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public ActivateConnectionCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(ActivateConnectionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (session == null)
                {
                    return Result<CommunicationSessionDto>.Failure("SESSION_NOT_FOUND", $"Communication session with connection id '{command.ConnectionId}' was not found.");
                }

                session.Activate();
                await _repository.UpdateAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (InvalidDomainException ex)
            {
                return Result<CommunicationSessionDto>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("ACTIVATE_CONNECTION_FAILED", ex.Message);
            }
        }
    }

    public class ProcessHeartbeatCommandHandler : ICommandHandler<ProcessHeartbeatCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public ProcessHeartbeatCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(ProcessHeartbeatCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (session == null)
                {
                    return Result<CommunicationSessionDto>.Failure("SESSION_NOT_FOUND", $"Communication session with connection id '{command.ConnectionId}' was not found.");
                }

                session.RecordHeartbeat(command.Timestamp);
                await _repository.UpdateAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("PROCESS_HEARTBEAT_FAILED", ex.Message);
            }
        }
    }

    public class DisconnectConnectionCommandHandler : ICommandHandler<DisconnectConnectionCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public DisconnectConnectionCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(DisconnectConnectionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (session == null)
                {
                    return Result<CommunicationSessionDto>.Failure("SESSION_NOT_FOUND", $"Communication session with connection id '{command.ConnectionId}' was not found.");
                }

                session.Disconnect(command.Reason);
                await _repository.UpdateAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (InvalidDomainException ex)
            {
                return Result<CommunicationSessionDto>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("DISCONNECT_CONNECTION_FAILED", ex.Message);
            }
        }
    }

    public class TerminateCommunicationSessionCommandHandler : ICommandHandler<TerminateCommunicationSessionCommand, CommunicationSessionDto>
    {
        private readonly ICommunicationSessionRepository _repository;

        public TerminateCommunicationSessionCommandHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto>> HandleAsync(TerminateCommunicationSessionCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _repository.GetByConnectionIdAsync(command.ConnectionId, cancellationToken);
                if (session == null)
                {
                    return Result<CommunicationSessionDto>.Failure("SESSION_NOT_FOUND", $"Communication session with connection id '{command.ConnectionId}' was not found.");
                }

                session.Terminate(command.Reason);
                await _repository.UpdateAsync(session, cancellationToken);
                return Result<CommunicationSessionDto>.Success(CommunicationSessionDto.FromEntity(session));
            }
            catch (InvalidDomainException ex)
            {
                return Result<CommunicationSessionDto>.Failure(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                return Result<CommunicationSessionDto>.Failure("TERMINATE_SESSION_FAILED", ex.Message);
            }
        }
    }

    public class GetCommunicationSessionByIdQueryHandler : IQueryHandler<GetCommunicationSessionByIdQuery, CommunicationSessionDto?>
    {
        private readonly ICommunicationSessionRepository _repository;

        public GetCommunicationSessionByIdQueryHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto?>> HandleAsync(GetCommunicationSessionByIdQuery query, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByIdAsync(query.SessionId, cancellationToken);
            return Result<CommunicationSessionDto?>.Success(session != null ? CommunicationSessionDto.FromEntity(session) : null);
        }
    }

    public class GetCommunicationSessionByConnectionIdQueryHandler : IQueryHandler<GetCommunicationSessionByConnectionIdQuery, CommunicationSessionDto?>
    {
        private readonly ICommunicationSessionRepository _repository;

        public GetCommunicationSessionByConnectionIdQueryHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto?>> HandleAsync(GetCommunicationSessionByConnectionIdQuery query, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByConnectionIdAsync(query.ConnectionId, cancellationToken);
            return Result<CommunicationSessionDto?>.Success(session != null ? CommunicationSessionDto.FromEntity(session) : null);
        }
    }

    public class GetCommunicationSessionByPcIdQueryHandler : IQueryHandler<GetCommunicationSessionByPcIdQuery, CommunicationSessionDto?>
    {
        private readonly ICommunicationSessionRepository _repository;

        public GetCommunicationSessionByPcIdQueryHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<CommunicationSessionDto?>> HandleAsync(GetCommunicationSessionByPcIdQuery query, CancellationToken cancellationToken = default)
        {
            var session = await _repository.GetByPcIdAsync(query.PcId, cancellationToken);
            return Result<CommunicationSessionDto?>.Success(session != null ? CommunicationSessionDto.FromEntity(session) : null);
        }
    }

    public class GetActiveCommunicationSessionsQueryHandler : IQueryHandler<GetActiveCommunicationSessionsQuery, IReadOnlyList<CommunicationSessionDto>>
    {
        private readonly ICommunicationSessionRepository _repository;

        public GetActiveCommunicationSessionsQueryHandler(ICommunicationSessionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<IReadOnlyList<CommunicationSessionDto>>> HandleAsync(GetActiveCommunicationSessionsQuery query, CancellationToken cancellationToken = default)
        {
            var sessions = await _repository.GetActiveSessionsAsync(cancellationToken);
            var dtos = sessions.Select(CommunicationSessionDto.FromEntity).ToList();
            return Result<IReadOnlyList<CommunicationSessionDto>>.Success(dtos);
        }
    }
}
