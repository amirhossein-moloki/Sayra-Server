using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Gamers;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/gamers")]
    public class GamersController : ControllerBase
    {
        private readonly ICommandHandler<CreateGamerCommand, Gamer> _createGamerHandler;
        private readonly ICommandHandler<UpdateGamerProfileCommand, Gamer> _updateProfileHandler;
        private readonly ICommandHandler<DeactivateGamerCommand, Gamer> _deactivateGamerHandler;
        private readonly ICommandHandler<ChangeGamerPasswordCommand, bool> _changePasswordHandler;
        private readonly ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> _authenticateGamerHandler;
        private readonly IQueryHandler<GetGamerQuery, Gamer> _getGamerHandler;
        private readonly IQueryHandler<GetGamerAccountQuery, GamerAccount> _getGamerAccountHandler;

        public GamersController(
            ICommandHandler<CreateGamerCommand, Gamer> createGamerHandler,
            ICommandHandler<UpdateGamerProfileCommand, Gamer> updateProfileHandler,
            ICommandHandler<DeactivateGamerCommand, Gamer> deactivateGamerHandler,
            ICommandHandler<ChangeGamerPasswordCommand, bool> changePasswordHandler,
            ICommandHandler<AuthenticateGamerCommand, AuthenticateGamerResponseDto> authenticateGamerHandler,
            IQueryHandler<GetGamerQuery, Gamer> getGamerHandler,
            IQueryHandler<GetGamerAccountQuery, GamerAccount> getGamerAccountHandler)
        {
            _createGamerHandler = createGamerHandler ?? throw new ArgumentNullException(nameof(createGamerHandler));
            _updateProfileHandler = updateProfileHandler ?? throw new ArgumentNullException(nameof(updateProfileHandler));
            _deactivateGamerHandler = deactivateGamerHandler ?? throw new ArgumentNullException(nameof(deactivateGamerHandler));
            _changePasswordHandler = changePasswordHandler ?? throw new ArgumentNullException(nameof(changePasswordHandler));
            _authenticateGamerHandler = authenticateGamerHandler ?? throw new ArgumentNullException(nameof(authenticateGamerHandler));
            _getGamerHandler = getGamerHandler ?? throw new ArgumentNullException(nameof(getGamerHandler));
            _getGamerAccountHandler = getGamerAccountHandler ?? throw new ArgumentNullException(nameof(getGamerAccountHandler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateGamerRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreateGamerCommand
            {
                Username = request.Username,
                Email = request.Email,
                Password = request.Password,
                PhoneNumber = request.PhoneNumber,
                FirstName = request.FirstName,
                LastName = request.LastName,
                BirthDate = request.BirthDate,
                OrganizationId = request.OrganizationId,
                SiteId = request.SiteId
            };

            var result = await _createGamerHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_USERNAME" || result.ErrorCode == "DUPLICATE_EMAIL")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_GAMER_FAILED", message = result.ErrorMessage });
            }

            var gamer = result.Value!;
            var response = MapToGamerResponseDto(gamer);

            return Created($"/api/gamers/{response.Id}", response);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetGamerQuery { GamerEntityId = id };
            var result = await _getGamerHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            var response = MapToGamerResponseDto(result.Value!);
            return Ok(response);
        }

        [HttpPut("{id:guid}/profile")]
        public async Task<IActionResult> UpdateProfileAsync(Guid id, [FromBody] UpdateGamerProfileRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new UpdateGamerProfileCommand
            {
                GamerEntityId = id,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                FirstName = request.FirstName,
                LastName = request.LastName,
                BirthDate = request.BirthDate
            };

            var result = await _updateProfileHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                if (result.ErrorCode == "DUPLICATE_EMAIL")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "UPDATE_FAILED", message = result.ErrorMessage });
            }

            var response = MapToGamerResponseDto(result.Value!);
            return Ok(response);
        }

        [HttpPost("{id:guid}/deactivate")]
        public async Task<IActionResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
        {
            var command = new DeactivateGamerCommand { GamerEntityId = id };
            var result = await _deactivateGamerHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "DEACTIVATE_FAILED", message = result.ErrorMessage });
            }

            var response = MapToGamerResponseDto(result.Value!);
            return Ok(response);
        }

        [HttpPost("authenticate")]
        public async Task<IActionResult> AuthenticateAsync([FromBody] AuthenticateGamerRequestDto request, CancellationToken cancellationToken)
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

        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePasswordAsync(Guid id, [FromBody] ChangeGamerPasswordRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new ChangeGamerPasswordCommand
            {
                GamerEntityId = id,
                CurrentPassword = request.CurrentPassword,
                NewPassword = request.NewPassword
            };

            var result = await _changePasswordHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CHANGE_PASSWORD_FAILED", message = result.ErrorMessage });
            }

            return Ok(new { message = "Password updated successfully." });
        }

        [HttpGet("{id:guid}/account")]
        public async Task<IActionResult> GetAccountAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetGamerAccountQuery { GamerEntityId = id };
            var result = await _getGamerAccountHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            var account = result.Value!;
            var response = new GamerAccountResponseDto
            {
                Id = account.Id,
                GamerEntityId = account.GamerEntityId,
                AccountNumber = account.AccountNumber,
                Status = account.Status,
                Currency = account.Currency,
                Balance = account.Balance,
                BonusBalance = account.BonusBalance,
                CreatedAt = account.CreatedAt
            };

            return Ok(response);
        }

        private static GamerResponseDto MapToGamerResponseDto(Gamer gamer)
        {
            return new GamerResponseDto
            {
                Id = gamer.Id,
                GamerId = gamer.GamerId,
                Username = gamer.Username,
                Email = gamer.Email,
                PhoneNumber = gamer.PhoneNumber,
                FirstName = gamer.FirstName,
                LastName = gamer.LastName,
                BirthDate = gamer.BirthDate,
                Status = gamer.Status,
                OrganizationEntityId = gamer.OrganizationEntityId,
                SiteEntityId = gamer.SiteEntityId,
                CreatedAt = gamer.CreatedAt,
                UpdatedAt = gamer.UpdatedAt
            };
        }
    }
}
