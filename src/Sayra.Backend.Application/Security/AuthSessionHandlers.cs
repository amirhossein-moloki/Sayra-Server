using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Security
{
    public class LogoutCommandHandler : ICommandHandler<LogoutCommand, LogoutResponseDto>
    {
        private readonly IAuthenticationSessionService _sessionService;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public LogoutCommandHandler(
            IAuthenticationSessionService sessionService,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<LogoutResponseDto>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command.SessionToken))
                {
                    // If no token provided, revoke user/gamer sessions if principal provided
                    if (command.UserId.HasValue)
                    {
                        await _sessionService.RevokeAllUserSessionsAsync(command.UserId.Value, command.Reason, cancellationToken);
                    }
                    else if (command.GamerId.HasValue)
                    {
                        await _sessionService.RevokeAllGamerSessionsAsync(command.GamerId.Value, command.Reason, cancellationToken);
                    }

                    return Result<LogoutResponseDto>.Success(new LogoutResponseDto
                    {
                        IsSuccess = true,
                        Message = "Logged out successfully."
                    });
                }

                string token = command.SessionToken.Trim();
                var session = await _sessionService.GetSessionByTokenAsync(token, cancellationToken);

                if (session != null)
                {
                    // Verify ownership if caller principal IDs are present
                    if (command.UserId.HasValue && session.UserId.HasValue && session.UserId.Value != command.UserId.Value)
                    {
                        return Result<LogoutResponseDto>.Failure("CROSS_USER_LOGOUT_DENIED", "Cannot revoke another user's authentication session.");
                    }

                    if (command.GamerId.HasValue && session.GamerId.HasValue && session.GamerId.Value != command.GamerId.Value)
                    {
                        return Result<LogoutResponseDto>.Failure("CROSS_GAMER_LOGOUT_DENIED", "Cannot revoke another gamer's authentication session.");
                    }

                    await _sessionService.RevokeSessionAsync(token, command.Reason, cancellationToken);

                    var auditEvent = new AuditEvent
                    {
                        EventId = Guid.NewGuid(),
                        EventType = "LOGOUT",
                        Timestamp = DateTime.UtcNow,
                        CorrelationId = session.UserId?.ToString() ?? session.GamerId?.ToString() ?? "ANONYMOUS",
                        Payload = ProtocolSerialization.Serialize(new
                        {
                            sessionId = session.Id,
                            userId = session.UserId,
                            gamerId = session.GamerId,
                            reason = command.Reason,
                            timestamp = DateTime.UtcNow
                        })
                    };

                    await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                return Result<LogoutResponseDto>.Success(new LogoutResponseDto
                {
                    IsSuccess = true,
                    Message = "Logged out successfully."
                });
            }
            catch (Exception ex)
            {
                return Result<LogoutResponseDto>.Failure("LOGOUT_FAILED", ex.Message);
            }
        }
    }

    public class GetCurrentAuthenticationSessionQueryHandler : IQueryHandler<GetCurrentAuthenticationSessionQuery, AuthenticationSession>
    {
        private readonly IAuthenticationSessionService _sessionService;

        public GetCurrentAuthenticationSessionQueryHandler(IAuthenticationSessionService sessionService)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        }

        public async Task<Result<AuthenticationSession>> HandleAsync(GetCurrentAuthenticationSessionQuery query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query.SessionToken))
            {
                return Result<AuthenticationSession>.Failure("INVALID_TOKEN", "Session token cannot be empty.");
            }

            var session = await _sessionService.GetSessionByTokenAsync(query.SessionToken.Trim(), cancellationToken);
            if (session == null)
            {
                return Result<AuthenticationSession>.Failure("SESSION_NOT_FOUND", "Authentication session not found.");
            }

            return Result<AuthenticationSession>.Success(session);
        }
    }
}
