using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Telemetry;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Enums;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Events
{
    public record IngestClientEventCommand(
        string ConnectionPcId,
        ClientEventEnvelopeDto Event
    ) : ICommand<bool>;

    public class IngestClientEventCommandHandler : ICommandHandler<IngestClientEventCommand, bool>
    {
        private readonly ITelemetryIngestionService _ingestionService;
        private readonly IRepository<AuditEvent> _auditEventRepository;
        private readonly IUnitOfWork _unitOfWork;

        public IngestClientEventCommandHandler(
            ITelemetryIngestionService ingestionService,
            IRepository<AuditEvent> auditEventRepository,
            IUnitOfWork unitOfWork)
        {
            _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task<Result<bool>> HandleAsync(IngestClientEventCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null || command.Event == null)
            {
                return Result<bool>.Failure("PayloadNull", "Client event payload cannot be null.");
            }

            var context = new TelemetryConnectionContext(
                connectionId: Guid.NewGuid().ToString(),
                pcId: command.ConnectionPcId);

            var ingestionResult = await _ingestionService.IngestOperationalEventAsync(context, command.Event, cancellationToken);

            if (ingestionResult.Status == TelemetryIngestionStatus.Duplicate)
            {
                // Duplicate event safely ignored idempotently
                return Result<bool>.Success(true);
            }

            if (!ingestionResult.IsAccepted)
            {
                return Result<bool>.Failure(ingestionResult.RejectionReason.ToString(), ingestionResult.ErrorMessage ?? "Client event processing rejected.");
            }

            var signal = ingestionResult.EventSignal;
            if (signal == null)
            {
                return Result<bool>.Success(true);
            }

            Guid parsedEventGuid;
            if (!Guid.TryParse(signal.EventId, out parsedEventGuid))
            {
                parsedEventGuid = Guid.NewGuid();
            }

            Guid? parsedSessionGuid = null;
            if (!string.IsNullOrEmpty(signal.SessionId) && Guid.TryParse(signal.SessionId, out var sGuid))
            {
                parsedSessionGuid = sGuid;
            }

            // Audit record persistence in AuditEvent repository
            var auditEvent = new AuditEvent
            {
                EventId = parsedEventGuid,
                EventType = signal.EventType,
                CorrelationId = signal.CorrelationId,
                SessionId = parsedSessionGuid,
                Timestamp = signal.ServerReceivedAt,
                Payload = signal.Payload
            };

            await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
    }
}
