using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Locations;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/zones")]
    public class ZonesController : ControllerBase
    {
        private readonly ICommandHandler<CreateZoneCommand, Zone> _createHandler;
        private readonly IQueryHandler<GetZoneQuery, Zone> _getHandler;

        public ZonesController(
            ICommandHandler<CreateZoneCommand, Zone> createHandler,
            IQueryHandler<GetZoneQuery, Zone> getHandler)
        {
            _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
            _getHandler = getHandler ?? throw new ArgumentNullException(nameof(getHandler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateZoneRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreateZoneCommand
            {
                SiteId = request.SiteId,
                Name = request.Name,
                Code = request.Code
            };

            var result = await _createHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_ZONE_CODE")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                if (result.ErrorCode == "SITE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_FAILED", message = result.ErrorMessage });
            }

            var response = new ZoneResponseDto
            {
                Id = result.Value!.Id,
                ZoneId = result.Value.ZoneId,
                SiteId = result.Value.SiteId,
                Name = result.Value.Name,
                Code = result.Value.Code,
                Status = result.Value.Status,
                CreatedAt = result.Value.CreatedAt,
                UpdatedAt = result.Value.UpdatedAt
            };

            return Created($"/api/zones/{response.Id}", response);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetZoneQuery { ZoneId = id };
            var result = await _getHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            var response = new ZoneResponseDto
            {
                Id = result.Value!.Id,
                ZoneId = result.Value.ZoneId,
                SiteId = result.Value.SiteId,
                Name = result.Value.Name,
                Code = result.Value.Code,
                Status = result.Value.Status,
                CreatedAt = result.Value.CreatedAt,
                UpdatedAt = result.Value.UpdatedAt
            };

            return Ok(response);
        }
    }
}
