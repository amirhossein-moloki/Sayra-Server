using System;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Infrastructure.Security
{
    public class AccessAuditService : IAccessAuditService
    {
        private readonly ISecurityEventService _securityEventService;

        public AccessAuditService(ISecurityEventService securityEventService)
        {
            _securityEventService = securityEventService ?? throw new ArgumentNullException(nameof(securityEventService));
        }

        public async Task RecordAuthorizationGrantedAsync(
            UserPrincipal principal,
            string permission,
            object? resource = null,
            CancellationToken cancellationToken = default)
        {
            await _securityEventService.RecordSecurityEventAsync(
                eventType: "AUTHORIZATION_GRANTED",
                actorId: principal?.UserId ?? principal?.GamerId,
                actorType: ResolveActorType(principal),
                deviceId: principal?.PcId,
                organizationId: principal?.OrganizationId,
                siteId: principal?.SiteId,
                ResourceTypeFromObject(resource) ?? "Permission",
                ResourceIdFromObject(resource),
                action: permission,
                result: "GRANTED",
                failureReason: null,
                correlationId: CorrelationContext.CorrelationId,
                cancellationToken: cancellationToken);
        }

        public async Task RecordAuthorizationDeniedAsync(
            UserPrincipal principal,
            string permission,
            string reason,
            object? resource = null,
            CancellationToken cancellationToken = default)
        {
            string eventType = resource != null ? "RESOURCE_ACCESS_DENIED" : "AUTHORIZATION_DENIED";

            await _securityEventService.RecordSecurityEventAsync(
                eventType: eventType,
                actorId: principal?.UserId ?? principal?.GamerId,
                actorType: ResolveActorType(principal),
                deviceId: principal?.PcId,
                organizationId: principal?.OrganizationId,
                siteId: principal?.SiteId,
                ResourceTypeFromObject(resource) ?? "Permission",
                ResourceIdFromObject(resource),
                action: permission,
                result: "DENIED",
                failureReason: reason,
                correlationId: CorrelationContext.CorrelationId,
                cancellationToken: cancellationToken);
        }

        public async Task RecordDeviceHandshakeFailedAsync(
            string deviceId,
            string failureReason,
            string? ipAddress = null,
            CancellationToken cancellationToken = default)
        {
            await _securityEventService.RecordSecurityEventAsync(
                eventType: "DEVICE_AUTHENTICATION_FAILED",
                actorId: null,
                actorType: "DEVICE",
                deviceId: deviceId,
                organizationId: null,
                siteId: null,
                resourceType: "Workstation",
                resourceId: null,
                action: "HANDSHAKE",
                result: "FAILED",
                failureReason: failureReason,
                correlationId: CorrelationContext.CorrelationId,
                cancellationToken: cancellationToken);
        }

        public async Task RecordDeviceRegisteredAsync(
            string deviceId,
            Guid? siteId = null,
            CancellationToken cancellationToken = default)
        {
            await _securityEventService.RecordSecurityEventAsync(
                eventType: "DEVICE_REGISTERED",
                actorId: null,
                actorType: "DEVICE",
                deviceId: deviceId,
                organizationId: null,
                siteId: siteId,
                resourceType: "Workstation",
                resourceId: null,
                action: "REGISTER",
                result: "SUCCESS",
                failureReason: null,
                correlationId: CorrelationContext.CorrelationId,
                cancellationToken: cancellationToken);
        }

        private static string ResolveActorType(UserPrincipal? principal)
        {
            if (principal == null || !principal.IsAuthenticated) return "ANONYMOUS";
            if (principal.GamerId.HasValue) return "Gamer";
            return "User";
        }

        private static string? ResourceTypeFromObject(object? resource)
        {
            if (resource == null) return null;
            if (resource is BaseEntity baseEntity) return baseEntity.GetType().Name;
            return resource.GetType().Name;
        }

        private static Guid? ResourceIdFromObject(object? resource)
        {
            if (resource is BaseEntity baseEntity) return baseEntity.Id;
            return null;
        }
    }
}
