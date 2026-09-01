using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Caching;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Events
{
    public record IngestClientEventCommand(
        string ConnectionPcId,
        ClientEventEnvelopeDto Event
    ) : ICommand<bool>;

    public class IngestClientEventCommandHandler : ICommandHandler<IngestClientEventCommand, bool>
    {
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRedisService _redisService;

        public IngestClientEventCommandHandler(
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork,
            IRedisService redisService)
        {
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        }

        public async Task<Result<bool>> HandleAsync(IngestClientEventCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null || command.Event == null)
            {
                return Result<bool>.Failure("Client event payload cannot be null.");
            }

            var evt = command.Event;

            // Strict Validation
            if (string.IsNullOrWhiteSpace(evt.EventId))
            {
                return Result<bool>.Failure("EventId is required.");
            }

            if (string.IsNullOrWhiteSpace(evt.EventType))
            {
                return Result<bool>.Failure("EventType is required.");
            }

            // Caller identity consistency verification
            if (!string.IsNullOrEmpty(command.ConnectionPcId))
            {
                var connPcIdUpper = command.ConnectionPcId.Trim().ToUpperInvariant();

                if (!string.IsNullOrEmpty(evt.ClientId) && !evt.ClientId.Trim().Equals(connPcIdUpper, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<bool>.Failure("ClientId does not match authenticated connection PC-ID.");
                }

                if (!string.IsNullOrEmpty(evt.WorkstationId) && !evt.WorkstationId.Trim().Equals(connPcIdUpper, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<bool>.Failure("WorkstationId does not match authenticated connection PC-ID.");
                }
            }

            // Deduplication via Redis key (24 hour deduplication window)
            string dedupKey = $"v1:event:dedup:{evt.EventId.Trim()}";
            string? existingDedup = await _redisService.GetStringAsync(dedupKey, cancellationToken);
            if (!string.IsNullOrEmpty(existingDedup))
            {
                // Duplicate event safely ignored idempotently
                return Result<bool>.Success(true);
            }

            await _redisService.SetStringAsync(dedupKey, "PROCESSED", TimeSpan.FromHours(24), cancellationToken);

            var serverReceivedAt = DateTime.UtcNow;

            Guid parsedEventGuid;
            if (!Guid.TryParse(evt.EventId, out parsedEventGuid))
            {
                parsedEventGuid = Guid.NewGuid();
            }

            Guid? parsedSessionGuid = null;
            if (!string.IsNullOrEmpty(evt.SessionId) && Guid.TryParse(evt.SessionId, out var sGuid))
            {
                parsedSessionGuid = sGuid;
            }

            // Audit record persistence in AuditEvent repository
            var auditEvent = new AuditEvent
            {
                EventId = parsedEventGuid,
                EventType = evt.EventType.Trim().ToUpperInvariant(),
                CorrelationId = evt.CorrelationId,
                SessionId = parsedSessionGuid,
                Timestamp = serverReceivedAt,
                Payload = evt.Payload ?? "{}"
            };

            await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
    }
}
