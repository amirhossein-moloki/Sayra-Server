using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain.Entities;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.OfflineQueue
{
    public class DlqEventResponseDto
    {
        public Guid Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public Guid? WorkstationId { get; set; }
        public string? SiteId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string ReliabilityClass { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string FailureCode { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastAttemptAt { get; set; }
        public DateTime DeadLetteredAt { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = string.Empty;
        public DateTime? RecoveredAt { get; set; }
        public string? RecoveredBy { get; set; }
    }

    public class PagedDlqResponseDto
    {
        public IReadOnlyList<DlqEventResponseDto> Items { get; set; } = new List<DlqEventResponseDto>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    public interface IOfflineDlqService
    {
        Task<Result<PagedDlqResponseDto>> GetDlqEventsAsync(
            UserPrincipal principal,
            string? clientId = null,
            string? siteId = null,
            Guid? organizationId = null,
            string? failureCode = null,
            string? processingStatus = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<Result<DlqEventResponseDto>> GetDlqEventByIdAsync(
            UserPrincipal principal,
            Guid eventId,
            CancellationToken cancellationToken = default);

        Task<Result<OrderingAndReconciliationResult>> RetryDlqEventAsync(
            UserPrincipal principal,
            Guid eventId,
            CancellationToken cancellationToken = default);

        Task<Result<DlqEventResponseDto>> RejectDlqEventAsync(
            UserPrincipal principal,
            Guid eventId,
            string? rejectionReason = null,
            CancellationToken cancellationToken = default);
    }
}
