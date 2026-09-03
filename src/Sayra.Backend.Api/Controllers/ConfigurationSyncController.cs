using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Configuration;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/config/package")]
    public class ConfigurationSyncController : ControllerBase
    {
        private readonly IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult> _syncHandler;

        public ConfigurationSyncController(IQueryHandler<SynchronizeConfigurationQuery, ConfigurationSyncResult> syncHandler)
        {
            _syncHandler = syncHandler ?? throw new ArgumentNullException(nameof(syncHandler));
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> SynchronizeConfigurationAsync(
            [FromQuery] string? currentVersion,
            CancellationToken cancellationToken)
        {
            // 1. Resolve Authenticated Server-Authoritative Identity from UserPrincipalMiddleware
            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to synchronize configuration." });
            }

            // Must have a bound workstation identity (PcId or UserId/GamerId)
            string? pcId = principal.PcId;
            Guid? workstationId = null;

            if (string.IsNullOrWhiteSpace(pcId) && principal.UserId.HasValue)
            {
                workstationId = principal.UserId.Value;
            }

            if (string.IsNullOrWhiteSpace(pcId) && !workstationId.HasValue)
            {
                return StatusCode(403, new { code = "NO_WORKSTATION_IDENTITY", message = "The authenticated session is not bound to a valid workstation identity." });
            }

            // 2. Safely parse client-provided currentVersion query parameter
            long? parsedClientVersion = ParseClientVersion(currentVersion);

            // 3. Dispatch SynchronizeConfigurationQuery
            var query = new SynchronizeConfigurationQuery(
                ClientPcId: pcId,
                ClientVersion: parsedClientVersion,
                WorkstationId: workstationId,
                OrganizationId: principal.OrganizationId);

            var result = await _syncHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                string code = result.ErrorCode ?? "CONFIG_SYNC_FAILED";
                string message = result.ErrorMessage ?? "Configuration synchronization failed.";

                if (code == "WORKSTATION_NOT_FOUND")
                {
                    return NotFound(new { code, message });
                }

                if (code == "WORKSTATION_DISABLED" || code == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(403, new { code, message });
                }

                return BadRequest(new { code, message });
            }

            var syncResult = result.Value!;

            // 4. Set HTTP Caching Headers (ETag and Cache-Control)
            if (!string.IsNullOrWhiteSpace(syncResult.Hash))
            {
                Response.Headers["ETag"] = $"\"{syncResult.Hash}\"";
            }
            Response.Headers["Cache-Control"] = "private, no-cache";

            // 5. Handle HTTP 304 Not Modified
            if (syncResult.Status == ConfigurationSyncStatus.UpToDate)
            {
                return StatusCode(304);
            }

            // 6. Map to ConfigurationPackageContract (SAYRA Client Protocol Contract)
            var responseContract = new ConfigurationPackageContract
            {
                Version = syncResult.Version,
                CreatedAt = syncResult.CreatedAt,
                IssuedBy = syncResult.IssuedBy,
                Hash = syncResult.Hash,
                Signature = syncResult.Signature,
                Payload = syncResult.Payload!,
                PayloadType = syncResult.PayloadType,
                TargetClient = syncResult.TargetClient,
                TargetGroup = syncResult.TargetGroup
            };

            return Ok(responseContract);
        }

        private static long? ParseClientVersion(string? rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            string trimmed = rawVersion.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1).Trim();
            }

            if (long.TryParse(trimmed, out long parsed) && parsed >= 0)
            {
                return parsed;
            }

            return null;
        }
    }
}
