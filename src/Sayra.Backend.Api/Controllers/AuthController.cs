using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Gamers;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> _authenticateGamerHandler;
        private readonly ICommandHandler<LogoutCommand, LogoutResponseDto> _logoutHandler;

        public AuthController(
            ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> authenticateGamerHandler,
            ICommandHandler<LogoutCommand, LogoutResponseDto> logoutHandler)
        {
            _authenticateGamerHandler = authenticateGamerHandler ?? throw new ArgumentNullException(nameof(authenticateGamerHandler));
            _logoutHandler = logoutHandler ?? throw new ArgumentNullException(nameof(logoutHandler));
        }

        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync([FromBody] AuthenticateGamerRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new AuthenticateGamerCommand
            {
                UsernameOrEmail = request.UsernameOrEmail,
                Password = request.Password
            };

            var result = await _authenticateGamerHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "AUTHENTICATION_FAILED", message = result.ErrorMessage });
            }

            var authResponse = result.Value!;
            if (!authResponse.IsSuccess)
            {
                if (authResponse.ErrorCode == "ACCOUNT_LOCKED")
                {
                    return StatusCode(423, authResponse);
                }

                return Unauthorized(authResponse);
            }

            return Ok(authResponse);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> LogoutAsync([FromHeader(Name = "Authorization")] string? authHeader, CancellationToken cancellationToken)
        {
            string? token = null;
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }

            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;

            var command = new LogoutCommand
            {
                SessionToken = token,
                UserId = principal?.UserId,
                GamerId = principal?.GamerId,
                Reason = "USER_LOGOUT"
            };

            var result = await _logoutHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return BadRequest(new { code = result.ErrorCode ?? "LOGOUT_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value!);
        }

        [HttpGet("me")]
        public IActionResult GetCurrentPrincipal()
        {
            var principal = HttpContext.Items["UserPrincipal"] as UserPrincipal;
            if (principal == null || !principal.IsAuthenticated)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication required." });
            }

            return Ok(new
            {
                userId = principal.UserId,
                gamerId = principal.GamerId,
                gamerBusinessId = principal.GamerBusinessId,
                username = principal.Username,
                accountStatus = principal.AccountStatus.ToString(),
                roles = principal.Roles,
                permissions = principal.Permissions,
                organizationId = principal.OrganizationId,
                siteId = principal.SiteId,
                pcId = principal.PcId
            });
        }
    }
}
