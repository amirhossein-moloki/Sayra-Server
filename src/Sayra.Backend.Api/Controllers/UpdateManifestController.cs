using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;

#nullable enable

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates/manifest")]
    public class UpdateManifestController : ControllerBase
    {
        private readonly IUpdateManifestService _manifestService;

        public UpdateManifestController(IUpdateManifestService manifestService)
        {
            _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetUpdateManifestAsync(
            [FromQuery] string? currentVersion,
            [FromQuery] string? reportedVersion,
            [FromQuery] string? osVersion,
            [FromQuery] string? architecture,
            CancellationToken cancellationToken)
        {
            // 1. Resolve Authenticated UserPrincipal from Middleware Context
            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required to request update manifest." });
            }

            // Must have a bound workstation identity
            if (string.IsNullOrWhiteSpace(principal.PcId) && !principal.UserId.HasValue)
            {
                return StatusCode(403, new { code = "NO_WORKSTATION_IDENTITY", message = "The authenticated session is not bound to a valid workstation identity." });
            }

            string versionToUse = !string.IsNullOrWhiteSpace(currentVersion)
                ? currentVersion
                : (!string.IsNullOrWhiteSpace(reportedVersion) ? reportedVersion : string.Empty);

            string downloadBaseUrl = $"{Request.Scheme}://{Request.Host}";

            var manifestRequest = new UpdateManifestRequest
            {
                Principal = principal,
                ReportedVersion = versionToUse,
                OsVersion = osVersion,
                Architecture = architecture,
                DownloadBaseUrl = downloadBaseUrl
            };

            // 2. Delegate to Server-Authoritative Manifest Application Service
            var result = await _manifestService.GetManifestAsync(manifestRequest, cancellationToken);

            if (!result.UpdateAvailable)
            {
                if (result.ReasonCode == "UNAUTHORIZED")
                {
                    return Unauthorized(new { code = "UNAUTHORIZED", message = result.ReasonDetails });
                }

                if (result.ReasonCode == EligibilityReasonCodes.WorkstationNotFound)
                {
                    return NotFound(new { code = result.ReasonCode, message = result.ReasonDetails });
                }

                if (result.ReasonCode == EligibilityReasonCodes.WorkstationDeactivated ||
                    result.ReasonCode == EligibilityReasonCodes.OrganizationMismatch)
                {
                    return StatusCode(403, new { code = result.ReasonCode, message = result.ReasonDetails });
                }

                // Normal update-not-available state (No update, client current, outside rollout, etc.) -> HTTP 204 No Content
                return NoContent();
            }

            // 3. Return 200 OK with ClientUpdateManifestContract
            return Ok(result.Manifest);
        }
    }
}
