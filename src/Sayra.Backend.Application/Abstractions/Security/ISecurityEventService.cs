using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface ISecurityEventService
    {
        Task RecordSecurityEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default);

        Task RecordSecurityEventAsync(
            string eventType,
            Guid? actorId,
            string? actorType,
            string? deviceId,
            Guid? organizationId,
            Guid? siteId,
            string? resourceType,
            Guid? resourceId,
            string? action,
            string result,
            string? failureReason,
            string? correlationId = null,
            string? traceId = null,
            CancellationToken cancellationToken = default);
    }
}
