using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Communication;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Exceptions;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class RemoteCommandManager : IRemoteCommandManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITcpConnectionRegistry _connectionRegistry;
        private readonly ISecureMessageService _secureMessageService;
        private readonly IRedisService _redisService;
        private readonly ILogger<RemoteCommandManager> _logger;

        public RemoteCommandManager(
            IServiceScopeFactory scopeFactory,
            ITcpConnectionRegistry connectionRegistry,
            ISecureMessageService secureMessageService,
            IRedisService redisService,
            ILogger<RemoteCommandManager> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
            _secureMessageService = secureMessageService ?? throw new ArgumentNullException(nameof(secureMessageService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<RemoteCommandResponseDto>> CreateAndDispatchCommandAsync(CreateRemoteCommandRequestDto request, CancellationToken cancellationToken = default)
        {
            if (request == null) return Result<RemoteCommandResponseDto>.Failure("INVALID_REQUEST", "Request body cannot be null.");
            if (string.IsNullOrWhiteSpace(request.CommandType)) return Result<RemoteCommandResponseDto>.Failure("INVALID_COMMAND_TYPE", "CommandType is required.");
            if (string.IsNullOrWhiteSpace(request.TargetPcId)) return Result<RemoteCommandResponseDto>.Failure("INVALID_TARGET_PC_ID", "TargetPcId is required.");
            if (string.IsNullOrWhiteSpace(request.RequestedBy)) return Result<RemoteCommandResponseDto>.Failure("INVALID_REQUESTED_BY", "RequestedBy is required.");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
            var securityEventService = scope.ServiceProvider.GetService<ISecurityEventService>();
            var authorizationService = scope.ServiceProvider.GetService<IAuthorizationService>();

            var pcIdUpper = request.TargetPcId.Trim().ToUpperInvariant();

            // Validate Target Workstation in PostgreSQL
            var workstation = await dbContext.Workstations.FirstOrDefaultAsync(w => w.Id == request.TargetWorkstationId || w.PcId == pcIdUpper, cancellationToken);
            if (workstation == null)
            {
                _logger.LogWarning("COMMAND_CREATION_FAILED: Target Workstation {PcId} not found.", pcIdUpper);
                return Result<RemoteCommandResponseDto>.Failure("WORKSTATION_NOT_FOUND", $"Target workstation '{pcIdUpper}' was not found.");
            }

            // Authorization Check
            if (request.CallerPrincipal is UserPrincipal principal && authorizationService != null)
            {
                string requiredPermission = request.CommandType.Trim().ToUpperInvariant() switch
                {
                    "LOCK_WORKSTATION" => PermissionCatalog.LockWorkstation,
                    "UNLOCK_WORKSTATION" => PermissionCatalog.UnlockWorkstation,
                    "RESTART_WORKSTATION" or "SHUTDOWN_WORKSTATION" or "LAUNCH_APPLICATION" or "TERMINATE_APPLICATION" => PermissionCatalog.ControlWorkstations,
                    _ => PermissionCatalog.ControlWorkstations
                };

                var authResult = await authorizationService.AuthorizeAsync(principal, requiredPermission, workstation, cancellationToken);
                if (!authResult.IsAllowed)
                {
                    _logger.LogWarning("COMMAND_CREATION_DENIED: Caller {RequestedBy} denied {Permission} on Workstation {PcId}: {Reason}",
                        request.RequestedBy, requiredPermission, pcIdUpper, authResult.FailureReason);

                    if (securityEventService != null)
                    {
                        await securityEventService.RecordSecurityEventAsync(
                            "COMMAND_AUTHORIZATION_DENIED",
                            principal.UserId,
                            "USER",
                            pcIdUpper,
                            workstation.OrganizationEntityId,
                            workstation.SiteEntityId,
                            "WorkstationCommand",
                            null,
                            request.CommandType,
                            "DENIED",
                            authResult.FailureReason,
                            request.CorrelationId,
                            null,
                            cancellationToken);
                    }

                    return Result<RemoteCommandResponseDto>.Failure("FORBIDDEN", authResult.FailureReason ?? "Caller is not authorized to create command.");
                }
            }

            if (workstation.IsDeactivated || workstation.IsDisabled)
            {
                _logger.LogWarning("COMMAND_CREATION_FAILED: Target Workstation {PcId} is disabled or deactivated.", pcIdUpper);
                return Result<RemoteCommandResponseDto>.Failure("WORKSTATION_INELIGIBLE", $"Target workstation '{pcIdUpper}' is disabled or deactivated.");
            }

            string commandId = $"CMD-{Guid.NewGuid():N}".ToUpperInvariant();
            TimeSpan ttl = request.TtlSeconds.HasValue && request.TtlSeconds.Value > 0 ? TimeSpan.FromSeconds(request.TtlSeconds.Value) : TimeSpan.FromMinutes(5);

            var command = RemoteCommand.Create(
                commandId,
                request.CommandType,
                workstation.Id,
                workstation.PcId,
                request.RequestedBy,
                request.Payload,
                ttl,
                request.Priority,
                request.CorrelationId,
                request.IsIdempotent);

            await dbContext.RemoteCommands.AddAsync(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("COMMAND_CREATED: Command {CommandId} ({CommandType}) created for Workstation {PcId} by {RequestedBy}.",
                command.CommandId, command.CommandType, command.TargetPcId, command.RequestedBy);

            if (securityEventService != null)
            {
                await securityEventService.RecordSecurityEventAsync(
                    "COMMAND_CREATED",
                    null,
                    "API",
                    command.TargetPcId,
                    workstation.OrganizationEntityId,
                    workstation.SiteEntityId,
                    "WorkstationCommand",
                    command.Id,
                    command.CommandType,
                    "SUCCESS",
                    null,
                    command.CorrelationId,
                    null,
                    cancellationToken);
            }

            // Connection routing
            var connection = _connectionRegistry.GetByPcId(pcIdUpper);
            if (connection == null || connection.State != Domain.ConnectionLifecycleState.Active)
            {
                command.TransitionTo("QUEUED", "Target workstation client is offline or disconnected.");
                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("COMMAND_QUEUED: Workstation {PcId} is offline. Command {CommandId} queued.", command.TargetPcId, command.CommandId);
                return Result<RemoteCommandResponseDto>.Success(MapToDto(command));
            }

            // Bind connection info
            command.TargetConnectionId = connection.ConnectionId;
            command.TransitionTo("SENDING");
            await dbContext.SaveChangesAsync(cancellationToken);

            // Ephemeral delivery state in Redis
            var redisKey = $"v1:remote-command:{command.CommandId}:state";
            await _redisService.SetAsync(redisKey, new
            {
                command.CommandId,
                command.CommandType,
                command.TargetPcId,
                ConnectionId = connection.ConnectionId,
                command.Status,
                UpdatedAt = DateTime.UtcNow
            }, ttl, cancellationToken);

            // Secure delivery pipeline over TCP/TLS envelope
            var commandMsg = new CommandMessage<string>
            {
                CommandId = command.CommandId,
                Type = command.CommandType,
                Payload = command.Payload,
                CorrelationId = command.CorrelationId,
                Timestamp = DateTime.UtcNow
            };

            var session = new ConnectionSession
            {
                ConnectionId = connection.ConnectionId,
                PcId = connection.PcId,
                SessionKey = connection.SessionKey,
                HandshakeState = connection.State
            };

            try
            {
                await _secureMessageService.SendSecureMessageAsync(session, commandMsg);
                command.TransitionTo("DELIVERED");
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("COMMAND_DISPATCHED: Command {CommandId} sent securely to connection {ConnectionId} for {PcId}.",
                    command.CommandId, connection.ConnectionId, command.TargetPcId);
            }
            catch (Exception sendEx)
            {
                _logger.LogWarning(sendEx, "COMMAND_DELIVERY_FAILED: Exception sending secure message frame for command {CommandId} to connection {ConnectionId}.",
                    command.CommandId, connection.ConnectionId);

                command.TransitionTo("DELIVERY_TIMEOUT", $"Delivery failed due to transport exception: {sendEx.Message}");
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result<RemoteCommandResponseDto>.Success(MapToDto(command));
        }

        public async Task<Result<bool>> ProcessCommandAckAsync(string commandId, string pcId, string status, string? failureReason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return Result<bool>.Failure("INVALID_COMMAND_ID", "CommandId is required.");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();

            var command = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
            if (command == null)
            {
                _logger.LogWarning("COMMAND_ACK_REJECTED: Command {CommandId} not found.", commandId);
                return Result<bool>.Failure("COMMAND_NOT_FOUND", $"Command '{commandId}' not found.");
            }

            // Cross-Workstation Protection: PC-ID validation
            var normalizedPcId = (pcId ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(command.TargetPcId, normalizedPcId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("CROSS_WORKSTATION_VIOLATION: Connection PC-ID '{ConnPcId}' attempted to ACK command {CommandId} belonging to PC-ID '{TargetPcId}'.",
                    normalizedPcId, commandId, command.TargetPcId);
                return Result<bool>.Failure("CROSS_WORKSTATION_FORGERY", "Client is not authorized to send ACK for another workstation's command.");
            }

            if (command.IsTerminal)
            {
                _logger.LogInformation("COMMAND_ACK_IGNORED: Command {CommandId} is already in terminal state {Status}.", commandId, command.Status);
                return Result<bool>.Success(true);
            }

            var ackStatusUpper = (status ?? string.Empty).Trim().ToUpperInvariant();
            if (ackStatusUpper == "REJECTED" || ackStatusUpper == "FAILED")
            {
                command.TransitionTo("REJECTED", failureReason ?? "Client rejected command execution.");
            }
            else
            {
                command.TransitionTo("ACKNOWLEDGED");
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var redisKey = $"v1:remote-command:{command.CommandId}:state";
            await _redisService.SetAsync(redisKey, new
            {
                command.CommandId,
                command.TargetPcId,
                command.Status,
                UpdatedAt = DateTime.UtcNow
            }, TimeSpan.FromMinutes(10), cancellationToken);

            _logger.LogInformation("COMMAND_ACKNOWLEDGED: Command {CommandId} state updated to {Status} by Workstation {PcId}.", command.CommandId, command.Status, command.TargetPcId);
            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> ProcessCommandResultAsync(string commandId, string pcId, string status, string? message, string? errorCode, string? resultPayload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return Result<bool>.Failure("INVALID_COMMAND_ID", "CommandId is required.");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();
            var securityEventService = scope.ServiceProvider.GetService<ISecurityEventService>();

            var command = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
            if (command == null)
            {
                _logger.LogWarning("COMMAND_RESULT_REJECTED: Command {CommandId} not found.", commandId);
                return Result<bool>.Failure("COMMAND_NOT_FOUND", $"Command '{commandId}' not found.");
            }

            // Cross-Workstation Protection: PC-ID validation
            var normalizedPcId = (pcId ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(command.TargetPcId, normalizedPcId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("CROSS_WORKSTATION_VIOLATION: Connection PC-ID '{ConnPcId}' attempted to submit result for command {CommandId} belonging to PC-ID '{TargetPcId}'.",
                    normalizedPcId, commandId, command.TargetPcId);
                return Result<bool>.Failure("CROSS_WORKSTATION_FORGERY", "Client is not authorized to submit result for another workstation's command.");
            }

            // Deterministic Timeout vs Success Race handling: first terminal transition wins
            if (command.IsTerminal)
            {
                _logger.LogInformation("COMMAND_RESULT_IGNORED: Late or duplicate result received for command {CommandId} already in terminal state {Status}.",
                    commandId, command.Status);
                return Result<bool>.Success(true);
            }

            var resultStatusUpper = (status ?? string.Empty).Trim().ToUpperInvariant();

            if (resultStatusUpper is "EXECUTED" or "SUCCEEDED" or "SUCCESS")
            {
                command.TransitionTo("SUCCEEDED", message, errorCode, resultPayload);
            }
            else
            {
                command.TransitionTo("FAILED", message ?? "Client reported command failure.", errorCode ?? "CLIENT_EXECUTION_FAILED", resultPayload);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            // Clean up ephemeral state in Redis
            var redisKey = $"v1:remote-command:{command.CommandId}:state";
            await _redisService.RemoveAsync(redisKey, cancellationToken);

            _logger.LogInformation("COMMAND_RESULT_PROCESSED: Command {CommandId} finalized as {Status} for Workstation {PcId}.",
                command.CommandId, command.Status, command.TargetPcId);

            if (securityEventService != null)
            {
                await securityEventService.RecordSecurityEventAsync(
                    $"COMMAND_{command.Status}",
                    null,
                    "CLIENT",
                    command.TargetPcId,
                    null,
                    null,
                    "WorkstationCommand",
                    command.Id,
                    command.CommandType,
                    command.Status,
                    command.FailureReason,
                    command.CorrelationId,
                    null,
                    cancellationToken);
            }

            return Result<bool>.Success(true);
        }

        public async Task<Result<bool>> CancelCommandAsync(string commandId, string requestedBy, string? reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return Result<bool>.Failure("INVALID_COMMAND_ID", "CommandId is required.");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();

            var command = await dbContext.RemoteCommands.FirstOrDefaultAsync(c => c.CommandId == commandId, cancellationToken);
            if (command == null) return Result<bool>.Failure("COMMAND_NOT_FOUND", $"Command '{commandId}' not found.");

            if (command.IsTerminal)
            {
                _logger.LogWarning("COMMAND_CANCEL_REJECTED: Command {CommandId} is already in terminal state {Status}.", commandId, command.Status);
                return Result<bool>.Failure("COMMAND_TERMINAL", $"Command '{commandId}' is already in terminal state '{command.Status}'.");
            }

            command.TransitionTo("CANCELLED", reason ?? $"Cancelled by {requestedBy}.");
            await dbContext.SaveChangesAsync(cancellationToken);

            var redisKey = $"v1:remote-command:{command.CommandId}:state";
            await _redisService.RemoveAsync(redisKey, cancellationToken);

            _logger.LogInformation("COMMAND_CANCELLED: Command {CommandId} cancelled by {RequestedBy}.", command.CommandId, requestedBy);
            return Result<bool>.Success(true);
        }

        public async Task EvaluateTimeoutsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Persistence.ApplicationDbContext>();

            var now = DateTime.UtcNow;

            // 1. Evaluate Expired Commands (ExpiresAt reached)
            var expired = await dbContext.RemoteCommands
                .Where(c => !RemoteCommand.IsTerminalState(c.Status) && c.ExpiresAt != null && c.ExpiresAt <= now)
                .ToListAsync(cancellationToken);

            foreach (var cmd in expired)
            {
                try
                {
                    cmd.TransitionTo("EXPIRED", "Command lifetime expired before completion.");
                    _logger.LogInformation("COMMAND_EXPIRED: Command {CommandId} timed out and marked EXPIRED.", cmd.CommandId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error expiring command {CommandId}.", cmd.CommandId);
                }
            }

            // 2. Evaluate Delivery Timeouts (SENDING/QUEUED for > 2 minutes)
            var deliveryTimeoutCutoff = now.AddMinutes(-2);
            var deliveryTimedOut = await dbContext.RemoteCommands
                .Where(c => (c.Status == "SENDING" || c.Status == "QUEUED") && c.CreatedAt <= deliveryTimeoutCutoff)
                .ToListAsync(cancellationToken);

            foreach (var cmd in deliveryTimedOut)
            {
                try
                {
                    cmd.TransitionTo("DELIVERY_TIMEOUT", "Delivery timed out without client acknowledgement.");
                    _logger.LogWarning("COMMAND_DELIVERY_TIMEOUT: Command {CommandId} marked DELIVERY_TIMEOUT.", cmd.CommandId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error transitioning delivery timeout for command {CommandId}.", cmd.CommandId);
                }
            }

            // 3. Evaluate Execution Timeouts (DELIVERED/ACKNOWLEDGED/EXECUTING for > 5 minutes)
            var executionTimeoutCutoff = now.AddMinutes(-5);
            var executionTimedOut = await dbContext.RemoteCommands
                .Where(c => (c.Status == "DELIVERED" || c.Status == "ACKNOWLEDGED" || c.Status == "EXECUTING") && c.DeliveredAt != null && c.DeliveredAt <= executionTimeoutCutoff)
                .ToListAsync(cancellationToken);

            foreach (var cmd in executionTimedOut)
            {
                try
                {
                    cmd.TransitionTo("EXECUTION_TIMEOUT", "Execution timed out without client result.");
                    _logger.LogWarning("COMMAND_EXECUTION_TIMEOUT: Command {CommandId} marked EXECUTION_TIMEOUT.", cmd.CommandId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error transitioning execution timeout for command {CommandId}.", cmd.CommandId);
                }
            }

            if (expired.Count > 0 || deliveryTimedOut.Count > 0 || executionTimedOut.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
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
