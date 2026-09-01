using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Commands
{
    public class CreateRemoteCommandHandler : ICommandHandler<CreateRemoteCommand, RemoteCommandResponseDto>
    {
        private readonly IRemoteCommandManager _remoteCommandManager;

        public CreateRemoteCommandHandler(IRemoteCommandManager remoteCommandManager)
        {
            _remoteCommandManager = remoteCommandManager ?? throw new ArgumentNullException(nameof(remoteCommandManager));
        }

        public async Task<Result<RemoteCommandResponseDto>> HandleAsync(CreateRemoteCommand command, CancellationToken cancellationToken)
        {
            var request = new CreateRemoteCommandRequestDto
            {
                CommandType = command.CommandType,
                TargetWorkstationId = command.TargetWorkstationId,
                TargetPcId = command.TargetPcId,
                RequestedBy = command.RequestedBy,
                CallerPrincipal = command.CallerPrincipal,
                Payload = command.Payload,
                TtlSeconds = command.TtlSeconds,
                Priority = command.Priority,
                CorrelationId = command.CorrelationId,
                IsIdempotent = command.IsIdempotent
            };

            return await _remoteCommandManager.CreateAndDispatchCommandAsync(request, cancellationToken);
        }
    }

    public class ProcessCommandAckCommandHandler : ICommandHandler<ProcessCommandAckCommand, bool>
    {
        private readonly IRemoteCommandManager _remoteCommandManager;

        public ProcessCommandAckCommandHandler(IRemoteCommandManager remoteCommandManager)
        {
            _remoteCommandManager = remoteCommandManager ?? throw new ArgumentNullException(nameof(remoteCommandManager));
        }

        public async Task<Result<bool>> HandleAsync(ProcessCommandAckCommand command, CancellationToken cancellationToken)
        {
            return await _remoteCommandManager.ProcessCommandAckAsync(
                command.CommandId,
                command.PcId,
                command.Status,
                command.FailureReason,
                cancellationToken);
        }
    }

    public class ProcessCommandResultCommandHandler : ICommandHandler<ProcessCommandResultCommand, bool>
    {
        private readonly IRemoteCommandManager _remoteCommandManager;

        public ProcessCommandResultCommandHandler(IRemoteCommandManager remoteCommandManager)
        {
            _remoteCommandManager = remoteCommandManager ?? throw new ArgumentNullException(nameof(remoteCommandManager));
        }

        public async Task<Result<bool>> HandleAsync(ProcessCommandResultCommand command, CancellationToken cancellationToken)
        {
            return await _remoteCommandManager.ProcessCommandResultAsync(
                command.CommandId,
                command.PcId,
                command.Status,
                command.Message,
                command.ErrorCode,
                command.ResultPayload,
                cancellationToken);
        }
    }

    public class CancelRemoteCommandHandler : ICommandHandler<CancelRemoteCommand, bool>
    {
        private readonly IRemoteCommandManager _remoteCommandManager;

        public CancelRemoteCommandHandler(IRemoteCommandManager remoteCommandManager)
        {
            _remoteCommandManager = remoteCommandManager ?? throw new ArgumentNullException(nameof(remoteCommandManager));
        }

        public async Task<Result<bool>> HandleAsync(CancelRemoteCommand command, CancellationToken cancellationToken)
        {
            return await _remoteCommandManager.CancelCommandAsync(
                command.CommandId,
                command.RequestedBy,
                command.Reason,
                cancellationToken);
        }
    }

    public class GetRemoteCommandByIdQueryHandler : IQueryHandler<GetRemoteCommandByIdQuery, RemoteCommandResponseDto?>
    {
        private readonly IRemoteCommandRepository _repository;

        public GetRemoteCommandByIdQueryHandler(IRemoteCommandRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<RemoteCommandResponseDto?>> HandleAsync(GetRemoteCommandByIdQuery query, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetByIdAsync(query.Id, cancellationToken);
            if (entity == null) return Result<RemoteCommandResponseDto?>.Success(null);

            return Result<RemoteCommandResponseDto?>.Success(MapToDto(entity));
        }

        private static RemoteCommandResponseDto MapToDto(RemoteCommand entity) => new()
        {
            Id = entity.Id,
            CommandId = entity.CommandId,
            CommandType = entity.CommandType,
            TargetWorkstationId = entity.TargetWorkstationId,
            TargetPcId = entity.TargetPcId,
            TargetConnectionId = entity.TargetConnectionId,
            TargetSessionId = entity.TargetSessionId,
            RequestedBy = entity.RequestedBy,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            DeliveredAt = entity.DeliveredAt,
            AcknowledgedAt = entity.AcknowledgedAt,
            ExecutingAt = entity.ExecutingAt,
            CompletedAt = entity.CompletedAt,
            Status = entity.Status,
            Priority = entity.Priority,
            CorrelationId = entity.CorrelationId,
            Payload = entity.Payload,
            ResultPayload = entity.ResultPayload,
            ErrorCode = entity.ErrorCode,
            FailureReason = entity.FailureReason,
            IsIdempotent = entity.IsIdempotent
        };
    }

    public class GetRemoteCommandByCommandIdQueryHandler : IQueryHandler<GetRemoteCommandByCommandIdQuery, RemoteCommandResponseDto?>
    {
        private readonly IRemoteCommandRepository _repository;

        public GetRemoteCommandByCommandIdQueryHandler(IRemoteCommandRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<RemoteCommandResponseDto?>> HandleAsync(GetRemoteCommandByCommandIdQuery query, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetByCommandIdAsync(query.CommandId, cancellationToken);
            if (entity == null) return Result<RemoteCommandResponseDto?>.Success(null);

            return Result<RemoteCommandResponseDto?>.Success(MapToDto(entity));
        }

        private static RemoteCommandResponseDto MapToDto(RemoteCommand entity) => new()
        {
            Id = entity.Id,
            CommandId = entity.CommandId,
            CommandType = entity.CommandType,
            TargetWorkstationId = entity.TargetWorkstationId,
            TargetPcId = entity.TargetPcId,
            TargetConnectionId = entity.TargetConnectionId,
            TargetSessionId = entity.TargetSessionId,
            RequestedBy = entity.RequestedBy,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            DeliveredAt = entity.DeliveredAt,
            AcknowledgedAt = entity.AcknowledgedAt,
            ExecutingAt = entity.ExecutingAt,
            CompletedAt = entity.CompletedAt,
            Status = entity.Status,
            Priority = entity.Priority,
            CorrelationId = entity.CorrelationId,
            Payload = entity.Payload,
            ResultPayload = entity.ResultPayload,
            ErrorCode = entity.ErrorCode,
            FailureReason = entity.FailureReason,
            IsIdempotent = entity.IsIdempotent
        };
    }

    public class GetRemoteCommandsByWorkstationQueryHandler : IQueryHandler<GetRemoteCommandsByWorkstationQuery, IReadOnlyList<RemoteCommandResponseDto>>
    {
        private readonly IRemoteCommandRepository _repository;

        public GetRemoteCommandsByWorkstationQueryHandler(IRemoteCommandRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<Result<IReadOnlyList<RemoteCommandResponseDto>>> HandleAsync(GetRemoteCommandsByWorkstationQuery query, CancellationToken cancellationToken)
        {
            var entities = await _repository.GetPendingCommandsByWorkstationAsync(query.WorkstationId, cancellationToken);
            var dtos = entities.Select(MapToDto).ToList();
            return Result<IReadOnlyList<RemoteCommandResponseDto>>.Success(dtos);
        }

        private static RemoteCommandResponseDto MapToDto(RemoteCommand entity) => new()
        {
            Id = entity.Id,
            CommandId = entity.CommandId,
            CommandType = entity.CommandType,
            TargetWorkstationId = entity.TargetWorkstationId,
            TargetPcId = entity.TargetPcId,
            TargetConnectionId = entity.TargetConnectionId,
            TargetSessionId = entity.TargetSessionId,
            RequestedBy = entity.RequestedBy,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            DeliveredAt = entity.DeliveredAt,
            AcknowledgedAt = entity.AcknowledgedAt,
            ExecutingAt = entity.ExecutingAt,
            CompletedAt = entity.CompletedAt,
            Status = entity.Status,
            Priority = entity.Priority,
            CorrelationId = entity.CorrelationId,
            Payload = entity.Payload,
            ResultPayload = entity.ResultPayload,
            ErrorCode = entity.ErrorCode,
            FailureReason = entity.FailureReason,
            IsIdempotent = entity.IsIdempotent
        };
    }
}
