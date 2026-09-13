using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineDlqService : IOfflineDlqService
    {
        private readonly IDeadLetterEventRepository _dlqRepository;
        private readonly IOfflineOrderingAndReconciliationEngine _orderingEngine;
        private readonly IRepository<AuditEvent> _auditRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<OfflineDlqService> _logger;

        public OfflineDlqService(
            IDeadLetterEventRepository dlqRepository,
            IOfflineOrderingAndReconciliationEngine orderingEngine,
            IRepository<AuditEvent> auditRepository,
            IUnitOfWork unitOfWork,
            ILogger<OfflineDlqService> logger)
        {
            _dlqRepository = dlqRepository ?? throw new ArgumentNullException(nameof(dlqRepository));
            _orderingEngine = orderingEngine ?? throw new ArgumentNullException(nameof(orderingEngine));
            _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<PagedDlqResponseDto>> GetDlqEventsAsync(
            UserPrincipal principal,
            string? clientId = null,
            string? siteId = null,
            Guid? organizationId = null,
            string? failureCode = null,
            string? processingStatus = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Result<PagedDlqResponseDto>.Failure("UNAUTHORIZED", "Authentication is required.");
            }

            Guid? effectiveOrgId = organizationId;
            string? effectiveSiteId = siteId;

            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty)
            {
                if (effectiveOrgId.HasValue && effectiveOrgId.Value != principal.OrganizationId.Value)
                {
                    return Result<PagedDlqResponseDto>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access to requested organization is denied.");
                }
                effectiveOrgId = principal.OrganizationId.Value;
            }

            if (principal.SiteId.HasValue && principal.SiteId.Value != Guid.Empty)
            {
                string principalSiteStr = principal.SiteId.Value.ToString();
                if (!string.IsNullOrWhiteSpace(effectiveSiteId) && !effectiveSiteId.Equals(principalSiteStr, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<PagedDlqResponseDto>.Failure("CROSS_SITE_ACCESS_DENIED", "Access to requested site is denied.");
                }
                effectiveSiteId = principalSiteStr;
            }

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _dlqRepository.GetPagedAsync(
                clientId,
                effectiveSiteId,
                effectiveOrgId,
                failureCode,
                processingStatus,
                page,
                pageSize,
                cancellationToken);

            var dtos = items.Select(MapToDto).ToList();

            var result = new PagedDlqResponseDto
            {
                Items = dtos,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };

            return Result<PagedDlqResponseDto>.Success(result);
        }

        public async Task<Result<DlqEventResponseDto>> GetDlqEventByIdAsync(
            UserPrincipal principal,
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Result<DlqEventResponseDto>.Failure("UNAUTHORIZED", "Authentication is required.");
            }

            var dlq = await _dlqRepository.GetByEventIdAsync(eventId, cancellationToken)
                   ?? await _dlqRepository.GetByIdAsync(eventId, cancellationToken);

            if (dlq == null)
            {
                return Result<DlqEventResponseDto>.Failure("DLQ_EVENT_NOT_FOUND", $"DLQ event with ID '{eventId}' was not found.");
            }

            if (!HasTenantAccess(principal, dlq))
            {
                return Result<DlqEventResponseDto>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access to DLQ event is denied.");
            }

            return Result<DlqEventResponseDto>.Success(MapToDto(dlq));
        }

        public async Task<Result<OrderingAndReconciliationResult>> RetryDlqEventAsync(
            UserPrincipal principal,
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Result<OrderingAndReconciliationResult>.Failure("UNAUTHORIZED", "Authentication is required.");
            }

            var dlq = await _dlqRepository.GetByEventIdAsync(eventId, cancellationToken)
                   ?? await _dlqRepository.GetByIdAsync(eventId, cancellationToken);

            if (dlq == null)
            {
                return Result<OrderingAndReconciliationResult>.Failure("DLQ_EVENT_NOT_FOUND", $"DLQ event with ID '{eventId}' was not found.");
            }

            if (!HasTenantAccess(principal, dlq))
            {
                return Result<OrderingAndReconciliationResult>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access to DLQ event is denied.");
            }

            object payloadObj;
            try
            {
                using var doc = JsonDocument.Parse(dlq.Payload ?? "{}");
                payloadObj = doc.RootElement.Clone();
            }
            catch
            {
                payloadObj = dlq.Payload ?? "{}";
            }

            var queueItem = new OfflineQueueItem
            {
                EventId = dlq.EventId.ToString("D"),
                EventType = dlq.EventType,
                SequenceNumber = dlq.SequenceNumber,
                ReliabilityClass = dlq.ReliabilityClass,
                ContractVersion = "1.0",
                Payload = payloadObj,
                Timestamp = dlq.FirstSeenAt
            };

            var reconciliationResult = await _orderingEngine.EvaluateAndReconcileAsync(queueItem, dlq.ClientId, dlq.BatchId, cancellationToken);

            if (reconciliationResult.IsAcceptedForAck)
            {
                dlq.ProcessingStatus = DeadLetterStatus.Recovered;
                dlq.RecoveredAt = DateTime.UtcNow;
                dlq.RecoveredBy = principal.UserId?.ToString();
                await _dlqRepository.UpdateAsync(dlq, cancellationToken);

                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = "OFFLINE_EVENT_DLQ_RETRY",
                    WorkstationId = dlq.WorkstationId,
                    CorrelationId = dlq.BatchId,
                    Priority = 1,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.Serialize(new
                    {
                        eventId = dlq.EventId,
                        retriedBy = principal.UserId,
                        resultStatus = reconciliationResult.ReconciliationStatus,
                        orderingStatus = reconciliationResult.OrderingStatus
                    })
                };

                await _auditRepository.AddAsync(auditEvent, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("DLQ event {EventId} manually retried and recovered by user {UserId}.", dlq.EventId, principal.UserId);
            }

            return Result<OrderingAndReconciliationResult>.Success(reconciliationResult);
        }

        public async Task<Result<DlqEventResponseDto>> RejectDlqEventAsync(
            UserPrincipal principal,
            Guid eventId,
            string? rejectionReason = null,
            CancellationToken cancellationToken = default)
        {
            if (principal == null || !principal.IsAuthenticated)
            {
                return Result<DlqEventResponseDto>.Failure("UNAUTHORIZED", "Authentication is required.");
            }

            var dlq = await _dlqRepository.GetByEventIdAsync(eventId, cancellationToken)
                   ?? await _dlqRepository.GetByIdAsync(eventId, cancellationToken);

            if (dlq == null)
            {
                return Result<DlqEventResponseDto>.Failure("DLQ_EVENT_NOT_FOUND", $"DLQ event with ID '{eventId}' was not found.");
            }

            if (!HasTenantAccess(principal, dlq))
            {
                return Result<DlqEventResponseDto>.Failure("CROSS_ORGANIZATION_ACCESS_DENIED", "Access to DLQ event is denied.");
            }

            dlq.ProcessingStatus = DeadLetterStatus.Rejected;
            dlq.FailureReason = rejectionReason ?? dlq.FailureReason;
            await _dlqRepository.UpdateAsync(dlq, cancellationToken);

            var auditEvent = new AuditEvent
            {
                EventId = Guid.NewGuid(),
                EventType = "OFFLINE_EVENT_DLQ_REJECTED",
                WorkstationId = dlq.WorkstationId,
                CorrelationId = dlq.BatchId,
                Priority = 1,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.Serialize(new
                {
                    eventId = dlq.EventId,
                    rejectedBy = principal.UserId,
                    reason = rejectionReason
                })
            };

            await _auditRepository.AddAsync(auditEvent, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("DLQ event {EventId} permanently rejected by user {UserId}.", dlq.EventId, principal.UserId);

            return Result<DlqEventResponseDto>.Success(MapToDto(dlq));
        }

        private static bool HasTenantAccess(UserPrincipal principal, DeadLetterEvent dlq)
        {
            if (principal.OrganizationId.HasValue && principal.OrganizationId.Value != Guid.Empty)
            {
                if (dlq.OrganizationId.HasValue && dlq.OrganizationId.Value != principal.OrganizationId.Value)
                {
                    return false;
                }
            }

            if (principal.SiteId.HasValue && principal.SiteId.Value != Guid.Empty)
            {
                string principalSiteStr = principal.SiteId.Value.ToString();
                if (!string.IsNullOrWhiteSpace(dlq.SiteId) && !dlq.SiteId.Equals(principalSiteStr, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static DlqEventResponseDto MapToDto(DeadLetterEvent dlq)
        {
            return new DlqEventResponseDto
            {
                Id = dlq.Id,
                EventId = dlq.EventId.ToString("D"),
                BatchId = dlq.BatchId,
                ClientId = dlq.ClientId,
                WorkstationId = dlq.WorkstationId,
                SiteId = dlq.SiteId,
                OrganizationId = dlq.OrganizationId,
                EventType = dlq.EventType,
                SequenceNumber = dlq.SequenceNumber,
                ReliabilityClass = dlq.ReliabilityClass,
                Payload = dlq.Payload,
                FailureCode = dlq.FailureCode,
                FailureReason = dlq.FailureReason,
                RetryCount = dlq.RetryCount,
                FirstSeenAt = dlq.FirstSeenAt,
                LastAttemptAt = dlq.LastAttemptAt,
                DeadLetteredAt = dlq.DeadLetteredAt,
                CorrelationId = dlq.CorrelationId,
                ProcessingStatus = dlq.ProcessingStatus,
                RecoveredAt = dlq.RecoveredAt,
                RecoveredBy = dlq.RecoveredBy
            };
        }
    }
}
