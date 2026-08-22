using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Locations;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/sites")]
    public class SitesController : ControllerBase
    {
        private readonly ICommandHandler<CreateSiteCommand, Site> _createHandler;
        private readonly IQueryHandler<GetSiteQuery, Site> _getHandler;

        public SitesController(
            ICommandHandler<CreateSiteCommand, Site> createHandler,
            IQueryHandler<GetSiteQuery, Site> getHandler)
        {
            _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
            _getHandler = getHandler ?? throw new ArgumentNullException(nameof(getHandler));
        }

        [HttpPost]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateSiteRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreateSiteCommand
            {
                OrganizationId = request.OrganizationId,
                Name = request.Name,
                Code = request.Code,
                Timezone = request.Timezone
            };

            var result = await _createHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_SITE_CODE")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }
                if (result.ErrorCode == "ORGANIZATION_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_FAILED", message = result.ErrorMessage });
            }

            var response = new SiteResponseDto
            {
                Id = result.Value!.Id,
                SiteId = result.Value.SiteId,
                OrganizationId = result.Value.OrganizationId,
                Name = result.Value.Name,
                Code = result.Value.Code,
                Status = result.Value.Status,
                Timezone = result.Value.Timezone,
                CreatedAt = result.Value.CreatedAt,
                UpdatedAt = result.Value.UpdatedAt
            };

            return Created($"/api/sites/{response.Id}", response);
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionCatalog.ManageUsers)]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetSiteQuery { SiteId = id };
            var result = await _getHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            var response = new SiteResponseDto
            {
                Id = result.Value!.Id,
                SiteId = result.Value.SiteId,
                OrganizationId = result.Value.OrganizationId,
                Name = result.Value.Name,
                Code = result.Value.Code,
                Status = result.Value.Status,
                Timezone = result.Value.Timezone,
                CreatedAt = result.Value.CreatedAt,
                UpdatedAt = result.Value.UpdatedAt
            };

            return Ok(response);
        }
    }
}
