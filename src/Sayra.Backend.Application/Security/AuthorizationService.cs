using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Security
{
    public class AuthorizationService : IAuthorizationService
    {
        private readonly IRepository<AuditEvent> _auditEventRepository;

        public AuthorizationService(IRepository<AuditEvent> auditEventRepository)
        {
            _auditEventRepository = auditEventRepository ?? throw new ArgumentNullException(nameof(auditEventRepository));
        }

        public async Task<AuthorizationResult> AuthorizeAsync(
            UserPrincipal? principal,
            string permission,
            object? resource = null,
            CancellationToken cancellationToken = default)
        {
            // 1. Fail Closed on Null / Unauthenticated Principal
            if (principal == null || !principal.IsAuthenticated)
            {
                await RecordAuditEventAsync("AUTHORIZATION_DENIED", principal, permission, "User is not authenticated.", cancellationToken);
                return AuthorizationResult.Denied("User is not authenticated.", "UNAUTHORIZED");
            }

            // 2. Account Status Check (Disabled/Suspended/Locked/Deleted Accounts Fail Authorization)
            if (principal.AccountStatus != UserAccountState.Active)
            {
                await RecordAuditEventAsync("AUTHORIZATION_DENIED", principal, permission, $"User account status is {principal.AccountStatus}.", cancellationToken);
                return AuthorizationResult.Denied($"User account is {principal.AccountStatus}.", "ACCOUNT_DISABLED");
            }

            bool isAdmin = principal.Roles.Any(r => string.Equals(r, RoleCatalog.Administrator, StringComparison.OrdinalIgnoreCase));
            bool isGamerOnly = principal.Roles.All(r => string.Equals(r, RoleCatalog.Gamer, StringComparison.OrdinalIgnoreCase));

            // 3. Permission Check
            if (!isAdmin)
            {
                bool hasPermission = principal.Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
                if (!hasPermission)
                {
                    await RecordAuditEventAsync("AUTHORIZATION_DENIED", principal, permission, $"Missing permission '{permission}'.", cancellationToken);
                    return AuthorizationResult.Denied($"Permission '{permission}' is required.", "PERMISSION_DENIED");
                }
            }

            // 4. Resource Scope & Ownership Checks
            if (resource != null)
            {
                // Gamer Ownership Enforcement
                if (isGamerOnly && !isAdmin)
                {
                    var ownershipResult = EvaluateGamerOwnership(principal, resource);
                    if (!ownershipResult.IsAllowed)
                    {
                        await RecordAuditEventAsync("RESOURCE_ACCESS_DENIED", principal, permission, ownershipResult.FailureReason ?? "Gamer ownership violation.", cancellationToken);
                        return ownershipResult;
                    }
                }

                // Site Scope Isolation (Operator / Manager)
                if (!isAdmin && principal.SiteId.HasValue)
                {
                    var siteResult = EvaluateSiteScope(principal, resource);
                    if (!siteResult.IsAllowed)
                    {
                        await RecordAuditEventAsync("CROSS_SITE_ACCESS_DENIED", principal, permission, siteResult.FailureReason ?? "Cross-site access violation.", cancellationToken);
                        return siteResult;
                    }
                }

                // Organization Scope Isolation (Operator / Manager)
                if (!isAdmin && principal.OrganizationId.HasValue)
                {
                    var orgResult = EvaluateOrganizationScope(principal, resource);
                    if (!orgResult.IsAllowed)
                    {
                        await RecordAuditEventAsync("CROSS_ORGANIZATION_ACCESS_DENIED", principal, permission, orgResult.FailureReason ?? "Cross-organization access violation.", cancellationToken);
                        return orgResult;
                    }
                }

                // Device Identity Matching
                if (!string.IsNullOrWhiteSpace(principal.PcId))
                {
                    var deviceResult = EvaluateDeviceIdentity(principal, resource);
                    if (!deviceResult.IsAllowed)
                    {
                        await RecordAuditEventAsync("RESOURCE_ACCESS_DENIED", principal, permission, deviceResult.FailureReason ?? "Device identity mismatch.", cancellationToken);
                        return deviceResult;
                    }
                }
            }

            await RecordAuditEventAsync("AUTHORIZATION_GRANTED", principal, permission, "Access granted.", cancellationToken);
            return AuthorizationResult.Allowed();
        }

        private AuthorizationResult EvaluateGamerOwnership(UserPrincipal principal, object resource)
        {
            Guid? principalGamerId = principal.GamerId ?? principal.UserId;

            switch (resource)
            {
                case Gamer gamer:
                    if (principalGamerId.HasValue && gamer.Id != principalGamerId.Value &&
                        !string.Equals(gamer.GamerId, principal.GamerBusinessId, StringComparison.OrdinalIgnoreCase))
                    {
                        return AuthorizationResult.Denied("Cannot access another gamer's profile.", "CROSS_GAMER_ACCESS_DENIED");
                    }
                    break;

                case Reservation reservation:
                    if (principalGamerId.HasValue && reservation.GamerId != principalGamerId.Value)
                    {
                        return AuthorizationResult.Denied("Cannot access another gamer's reservation.", "CROSS_GAMER_ACCESS_DENIED");
                    }
                    break;

                case Session session:
                    if (principalGamerId.HasValue && session.GamerId != principalGamerId.Value)
                    {
                        return AuthorizationResult.Denied("Cannot access another gamer's session.", "CROSS_GAMER_ACCESS_DENIED");
                    }
                    break;

                case GamerAccount account:
                    if (principalGamerId.HasValue && account.GamerEntityId != principalGamerId.Value && account.Id != principalGamerId.Value)
                    {
                        return AuthorizationResult.Denied("Cannot access another gamer's account.", "CROSS_GAMER_ACCESS_DENIED");
                    }
                    break;
            }

            return AuthorizationResult.Allowed();
        }

        private AuthorizationResult EvaluateSiteScope(UserPrincipal principal, object resource)
        {
            if (!principal.SiteId.HasValue) return AuthorizationResult.Allowed();

            Guid? resourceSiteId = GetSiteIdFromResource(resource);
            if (resourceSiteId.HasValue && resourceSiteId.Value != principal.SiteId.Value)
            {
                return AuthorizationResult.Denied("Cannot access resources outside assigned site.", "CROSS_SITE_ACCESS_DENIED");
            }

            return AuthorizationResult.Allowed();
        }

        private AuthorizationResult EvaluateOrganizationScope(UserPrincipal principal, object resource)
        {
            if (!principal.OrganizationId.HasValue) return AuthorizationResult.Allowed();

            Guid? resourceOrgId = GetOrganizationIdFromResource(resource);
            if (resourceOrgId.HasValue && resourceOrgId.Value != principal.OrganizationId.Value)
            {
                return AuthorizationResult.Denied("Cannot access resources outside assigned organization.", "CROSS_ORGANIZATION_ACCESS_DENIED");
            }

            return AuthorizationResult.Allowed();
        }

        private AuthorizationResult EvaluateDeviceIdentity(UserPrincipal principal, object resource)
        {
            if (resource is Workstation workstation)
            {
                if (!string.Equals(workstation.PcId, principal.PcId, StringComparison.OrdinalIgnoreCase))
                {
                    return AuthorizationResult.Denied("Authenticated device identity does not match workstation.", "DEVICE_IDENTITY_MISMATCH");
                }
            }

            return AuthorizationResult.Allowed();
        }

        private Guid? GetSiteIdFromResource(object resource)
        {
            return resource switch
            {
                Site site => site.Id,
                Workstation ws => ws.SiteEntityId,
                Session session => session.SiteId,
                Reservation res => res.SiteId,
                _ => null
            };
        }

        private Guid? GetOrganizationIdFromResource(object resource)
        {
            return resource switch
            {
                Organization org => org.Id,
                Site site => site.OrganizationId,
                Workstation ws => ws.OrganizationEntityId,
                Session session => session.OrganizationId,
                Reservation res => res.OrganizationId,
                _ => null
            };
        }

        private async Task RecordAuditEventAsync(
            string eventType,
            UserPrincipal? principal,
            string permission,
            string reason,
            CancellationToken cancellationToken)
        {
            try
            {
                var auditEvent = new AuditEvent
                {
                    EventId = Guid.NewGuid(),
                    EventType = eventType,
                    CorrelationId = principal?.UserId?.ToString() ?? principal?.GamerId?.ToString() ?? "ANONYMOUS",
                    Payload = ProtocolSerialization.Serialize(new
                    {
                        userId = principal?.UserId,
                        gamerId = principal?.GamerId,
                        username = principal?.Username,
                        roles = principal?.Roles,
                        permission = permission,
                        reason = reason,
                        timestamp = DateTime.UtcNow
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await _auditEventRepository.AddAsync(auditEvent, cancellationToken);
            }
            catch
            {
                // Non-blocking audit recording failure
            }
        }
    }
}
