using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Gamers;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> _authenticateGamerHandler;

        public AuthController(ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> authenticateGamerHandler)
        {
            _authenticateGamerHandler = authenticateGamerHandler ?? throw new ArgumentNullException(nameof(authenticateGamerHandler));
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
                return Unauthorized(authResponse);
            }

            return Ok(authResponse);
        }
    }
}
