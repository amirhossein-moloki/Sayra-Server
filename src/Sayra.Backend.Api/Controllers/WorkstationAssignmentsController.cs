using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Contracts;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/workstations")]
    public class WorkstationAssignmentsController : ControllerBase
    {
        private readonly ICommandHandler<AssignWorkstationCommand, WorkstationAssignmentResponseDto> _assignHandler;
        private readonly IQueryHandler<GetWorkstationAssignmentQuery, WorkstationAssignmentResponseDto> _getAssignmentHandler;

        public WorkstationAssignmentsController(
            ICommandHandler<AssignWorkstationCommand, WorkstationAssignmentResponseDto> assignHandler,
            IQueryHandler<GetWorkstationAssignmentQuery, WorkstationAssignmentResponseDto> getAssignmentHandler)
        {
            _assignHandler = assignHandler ?? throw new ArgumentNullException(nameof(assignHandler));
            _getAssignmentHandler = getAssignmentHandler ?? throw new ArgumentNullException(nameof(getAssignmentHandler));
        }

        [HttpPost("{id:guid}/assignment")]
        public async Task<IActionResult> AssignAsync(Guid id, [FromBody] AssignWorkstationRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new AssignWorkstationCommand
            {
                WorkstationId = id,
                OrganizationId = request.OrganizationId,
                SiteId = request.SiteId,
                ZoneId = request.ZoneId
            };

            var result = await _assignHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "WORKSTATION_NOT_FOUND" ||
                    result.ErrorCode == "ORGANIZATION_NOT_FOUND" ||
                    result.ErrorCode == "SITE_NOT_FOUND" ||
                    result.ErrorCode == "ZONE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "SITE_ORGANIZATION_MISMATCH" ||
                    result.ErrorCode == "ZONE_SITE_MISMATCH")
                {
                    return BadRequest(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "ASSIGNMENT_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }

        [HttpGet("{id:guid}/assignment")]
        public async Task<IActionResult> GetAssignmentAsync(Guid id, CancellationToken cancellationToken)
        {
            var query = new GetWorkstationAssignmentQuery { WorkstationId = id };
            var result = await _getAssignmentHandler.HandleAsync(query, cancellationToken);

            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "NOT_FOUND" || result.ErrorCode == "NOT_ASSIGNED")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_ASSIGNMENT_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }
    }
}
