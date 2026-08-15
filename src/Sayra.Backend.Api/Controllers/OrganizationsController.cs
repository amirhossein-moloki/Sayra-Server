using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Organizations;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/organizations")]
    public class OrganizationsController : ControllerBase
    {
        private readonly ICommandHandler<CreateOrganizationCommand, Organization> _createHandler;
        private readonly IQueryHandler<GetOrganizationQuery, Organization> _getHandler;

        public OrganizationsController(
            ICommandHandler<CreateOrganizationCommand, Organization> createHandler,
            IQueryHandler<GetOrganizationQuery, Organization> getHandler)
        {
            _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
            _getHandler = getHandler ?? throw new ArgumentNullException(nameof(getHandler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync([FromBody] CreateOrganizationRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new CreateOrganizationCommand
            {
                Name = request.Name,
                Code = request.Code
            };

            var result = await _createHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "DUPLICATE_ORGANIZATION_CODE")
                {
                    return Conflict(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "CREATE_FAILED", message = result.ErrorMessage });
            }

            var response = new OrganizationResponseDto
            {
                Id = result.Value!.Id,
                OrganizationId = result.Value.OrganizationId,
                Name = result.Value.Name,
                Code = result.Value.Code,
                Status = result.Value.Status,
                CreatedAt = result.Value.CreatedAt,
                UpdatedAt = result.Value.UpdatedAt
            };

            return Created($"/api/organizations/{response.Id}", response);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetOrganizationQuery { OrganizationId = id };
            var result = await _getHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return NotFound(new { code = result.ErrorCode ?? "NOT_FOUND", message = result.ErrorMessage });
            }

            var response = new OrganizationResponseDto
            {
                Id = result.Value!.Id,
                OrganizationId = result.Value.OrganizationId,
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
