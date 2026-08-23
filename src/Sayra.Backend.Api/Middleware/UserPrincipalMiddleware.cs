using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Api.Middleware
{
    public class UserPrincipalMiddleware
    {
        private readonly RequestDelegate _next;

        public UserPrincipalMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var principal = await ResolvePrincipalAsync(context);
            context.Items["UserPrincipal"] = principal;

            if (principal.IsAuthenticated)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, principal.UserId?.ToString() ?? principal.GamerId?.ToString() ?? "ANONYMOUS"),
                    new Claim(ClaimTypes.Name, principal.Username ?? "ANONYMOUS")
                };

                foreach (var role in principal.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                foreach (var perm in principal.Permissions)
                {
                    claims.Add(new Claim("permission", perm));
                }

                var identity = new ClaimsIdentity(claims, "HeaderScheme");
                context.User = new ClaimsPrincipal(identity);
            }

            await _next(context);
        }

        private async Task<UserPrincipal> ResolvePrincipalAsync(HttpContext context)
        {
            string? authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            string? xUserId = context.Request.Headers["X-User-Id"].FirstOrDefault();
            string? xGamerId = context.Request.Headers["X-Gamer-Id"].FirstOrDefault();
            string? xUserRole = context.Request.Headers["X-User-Role"].FirstOrDefault();
            string? xSiteId = context.Request.Headers["X-Site-Id"].FirstOrDefault();
            string? xOrgId = context.Request.Headers["X-Organization-Id"].FirstOrDefault();
            string? xPcId = context.Request.Headers["X-Pc-Id"].FirstOrDefault();

            string? token = null;
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }

            var sessionService = context.RequestServices.GetService<IAuthenticationSessionService>();
            AuthenticationSession? authSession = null;

            if (!string.IsNullOrWhiteSpace(token))
            {
                if (sessionService != null)
                {
                    bool isValid = await sessionService.ValidateSessionAsync(token, context.RequestAborted);
                    if (isValid)
                    {
                        authSession = await sessionService.GetSessionByTokenAsync(token, context.RequestAborted);
                    }
                    else
                    {
                        // Explicit Bearer token provided but invalid/expired/revoked -> Fail closed immediately
                        return UserPrincipal.Anonymous;
                    }
                }
            }

            string? lookupIdStr = authSession != null
                ? (authSession.UserId?.ToString() ?? authSession.GamerId?.ToString())
                : (!string.IsNullOrWhiteSpace(token) ? token : (!string.IsNullOrWhiteSpace(xUserId) ? xUserId : xGamerId));

            // Require an explicit user/gamer/token identifier to authenticate. Client-supplied role alone is unauthenticated.
            if (string.IsNullOrWhiteSpace(lookupIdStr))
            {
                return UserPrincipal.Anonymous;
            }

            var userRepository = context.RequestServices.GetService<IRepository<User>>();
            var gamerRepository = context.RequestServices.GetService<IRepository<Gamer>>();
            var userRoleRepository = context.RequestServices.GetService<IRepository<UserRoleEntity>>();
            var roleRepository = context.RequestServices.GetService<IRepository<Role>>();
            var rolePermRepository = context.RequestServices.GetService<IRepository<RolePermission>>();
            var permRepository = context.RequestServices.GetService<IRepository<Permission>>();

            if (!Guid.TryParse(lookupIdStr, out Guid entityGuid))
            {
                return UserPrincipal.Anonymous;
            }

            User? user = userRepository != null ? await userRepository.GetByIdAsync(entityGuid, track: false) : null;
            Gamer? gamer = null;

            if (user == null && gamerRepository != null)
            {
                gamer = await gamerRepository.GetByIdAsync(entityGuid, track: false);
                if (gamer != null && userRepository != null)
                {
                    user = await userRepository.FirstOrDefaultAsync(u => u.GamerEntityId == gamer.Id || u.Username.ToLower() == gamer.Username.ToLower(), track: false);
                }
            }

            // If neither User nor Gamer entity exists in database for this identifier, reject as unauthenticated
            if (user == null && gamer == null)
            {
                return UserPrincipal.Anonymous;
            }

            // Check account status state
            if (user != null && (user.Status == UserAccountState.Disabled || user.Status == UserAccountState.Suspended))
            {
                return UserPrincipal.Anonymous;
            }

            if (gamer != null && !gamer.CanOperate())
            {
                return UserPrincipal.Anonymous;
            }

            UserPrincipal principal = new UserPrincipal
            {
                IsAuthenticated = true,
                UserId = user?.Id ?? gamer?.Id,
                Username = user?.Username ?? gamer?.Username,
                AccountStatus = user?.Status ?? UserAccountState.Active,
                GamerId = gamer?.Id ?? user?.GamerEntityId,
                GamerBusinessId = gamer?.GamerId,
                OrganizationId = gamer?.OrganizationEntityId ?? user?.OrganizationEntityId,
                SiteId = gamer?.SiteEntityId ?? user?.SiteEntityId
            };

            // Resolve roles and permissions from database
            if (user != null && userRoleRepository != null && roleRepository != null && rolePermRepository != null && permRepository != null)
            {
                var uRoles = await userRoleRepository.FindAsync(ur => ur.UserEntityId == user.Id, track: false);
                var rIds = uRoles.Select(ur => ur.RoleId).ToList();
                if (rIds.Any())
                {
                    var roles = await roleRepository.FindAsync(r => rIds.Contains(r.Id), track: false);
                    principal.Roles.AddRange(roles.Select(r => r.Code));

                    var rPerms = await rolePermRepository.FindAsync(rp => rIds.Contains(rp.RoleId), track: false);
                    var pIds = rPerms.Select(rp => rp.PermissionId).Distinct().ToList();
                    if (pIds.Any())
                    {
                        var perms = await permRepository.FindAsync(p => pIds.Contains(p.Id), track: false);
                        principal.Permissions.AddRange(perms.Select(p => p.Code));
                    }
                }
            }

            if (!principal.Roles.Any() && user != null)
            {
                principal.Roles.Add(user.Role.ToString());
            }

            if (!principal.Roles.Any())
            {
                principal.Roles.Add(RoleCatalog.Gamer);
            }

            // Populate permissions for assigned roles
            PopulateDefaultRolePermissions(principal);

            if (Guid.TryParse(xSiteId, out Guid siteGuid)) principal.SiteId = siteGuid;
            if (Guid.TryParse(xOrgId, out Guid orgGuid)) principal.OrganizationId = orgGuid;
            if (!string.IsNullOrWhiteSpace(xPcId)) principal.PcId = xPcId.Trim();
            if (authSession != null && !string.IsNullOrWhiteSpace(authSession.PcId)) principal.PcId = authSession.PcId.Trim();

            return principal;
        }

        private static void PopulateDefaultRolePermissions(UserPrincipal principal)
        {
            var perms = new HashSet<string>(principal.Permissions, StringComparer.OrdinalIgnoreCase);

            foreach (var role in principal.Roles)
            {
                if (string.Equals(role, RoleCatalog.Administrator, StringComparison.OrdinalIgnoreCase))
                {
                    perms.Add(PermissionCatalog.ViewWorkstations);
                    perms.Add(PermissionCatalog.ControlWorkstations);
                    perms.Add(PermissionCatalog.LockWorkstation);
                    perms.Add(PermissionCatalog.UnlockWorkstation);
                    perms.Add(PermissionCatalog.ManageWorkstations);
                    perms.Add(PermissionCatalog.ManageDevices);
                    perms.Add(PermissionCatalog.StartSession);
                    perms.Add(PermissionCatalog.StopSession);
                    perms.Add(PermissionCatalog.PauseSession);
                    perms.Add(PermissionCatalog.ResumeSession);
                    perms.Add(PermissionCatalog.ExtendSession);
                    perms.Add(PermissionCatalog.ViewSessions);
                    perms.Add(PermissionCatalog.CreateReservation);
                    perms.Add(PermissionCatalog.ViewReservations);
                    perms.Add(PermissionCatalog.ManageReservations);
                    perms.Add(PermissionCatalog.CancelReservation);
                    perms.Add(PermissionCatalog.ViewPricing);
                    perms.Add(PermissionCatalog.ManagePricing);
                    perms.Add(PermissionCatalog.ViewFinancialData);
                    perms.Add(PermissionCatalog.ManageFinancialData);
                    perms.Add(PermissionCatalog.ProcessPayment);
                    perms.Add(PermissionCatalog.ViewLedger);
                    perms.Add(PermissionCatalog.ManageUsers);
                    perms.Add(PermissionCatalog.ManageRoles);
                    perms.Add(PermissionCatalog.ManagePermissions);
                    perms.Add(PermissionCatalog.ViewAuditLogs);
                    perms.Add(PermissionCatalog.ViewSecurityEvents);
                }
                else if (string.Equals(role, RoleCatalog.Manager, StringComparison.OrdinalIgnoreCase))
                {
                    perms.Add(PermissionCatalog.ViewWorkstations);
                    perms.Add(PermissionCatalog.ControlWorkstations);
                    perms.Add(PermissionCatalog.LockWorkstation);
                    perms.Add(PermissionCatalog.UnlockWorkstation);
                    perms.Add(PermissionCatalog.ManageWorkstations);
                    perms.Add(PermissionCatalog.ManageDevices);
                    perms.Add(PermissionCatalog.StartSession);
                    perms.Add(PermissionCatalog.StopSession);
                    perms.Add(PermissionCatalog.PauseSession);
                    perms.Add(PermissionCatalog.ResumeSession);
                    perms.Add(PermissionCatalog.ExtendSession);
                    perms.Add(PermissionCatalog.ViewSessions);
                    perms.Add(PermissionCatalog.CreateReservation);
                    perms.Add(PermissionCatalog.ViewReservations);
                    perms.Add(PermissionCatalog.ManageReservations);
                    perms.Add(PermissionCatalog.CancelReservation);
                    perms.Add(PermissionCatalog.ViewPricing);
                    perms.Add(PermissionCatalog.ManagePricing);
                    perms.Add(PermissionCatalog.ViewFinancialData);
                    perms.Add(PermissionCatalog.ManageFinancialData);
                    perms.Add(PermissionCatalog.ProcessPayment);
                    perms.Add(PermissionCatalog.ViewLedger);
                    perms.Add(PermissionCatalog.ViewAuditLogs);
                    perms.Add(PermissionCatalog.ViewSecurityEvents);
                }
                else if (string.Equals(role, RoleCatalog.Operator, StringComparison.OrdinalIgnoreCase))
                {
                    perms.Add(PermissionCatalog.ViewWorkstations);
                    perms.Add(PermissionCatalog.ControlWorkstations);
                    perms.Add(PermissionCatalog.LockWorkstation);
                    perms.Add(PermissionCatalog.UnlockWorkstation);
                    perms.Add(PermissionCatalog.StartSession);
                    perms.Add(PermissionCatalog.StopSession);
                    perms.Add(PermissionCatalog.PauseSession);
                    perms.Add(PermissionCatalog.ResumeSession);
                    perms.Add(PermissionCatalog.ExtendSession);
                    perms.Add(PermissionCatalog.ViewSessions);
                    perms.Add(PermissionCatalog.CreateReservation);
                    perms.Add(PermissionCatalog.ViewReservations);
                    perms.Add(PermissionCatalog.ManageReservations);
                    perms.Add(PermissionCatalog.CancelReservation);
                    perms.Add(PermissionCatalog.ViewPricing);
                    perms.Add(PermissionCatalog.ViewFinancialData);
                    perms.Add(PermissionCatalog.ProcessPayment);
                    perms.Add(PermissionCatalog.ViewLedger);
                }
                else if (string.Equals(role, RoleCatalog.Gamer, StringComparison.OrdinalIgnoreCase))
                {
                    perms.Add(PermissionCatalog.StartSession);
                    perms.Add(PermissionCatalog.StopSession);
                    perms.Add(PermissionCatalog.PauseSession);
                    perms.Add(PermissionCatalog.ResumeSession);
                    perms.Add(PermissionCatalog.ExtendSession);
                    perms.Add(PermissionCatalog.ViewSessions);
                    perms.Add(PermissionCatalog.CreateReservation);
                    perms.Add(PermissionCatalog.ViewReservations);
                    perms.Add(PermissionCatalog.CancelReservation);
                    perms.Add(PermissionCatalog.ViewPricing);
                    perms.Add(PermissionCatalog.ViewFinancialData);
                    perms.Add(PermissionCatalog.ProcessPayment);
                    perms.Add(PermissionCatalog.ViewLedger);
                }
            }

            principal.Permissions = perms.ToList();
        }
    }
}
