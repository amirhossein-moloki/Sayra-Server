using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Workstations;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/clients")]
    public class WorkstationsController : ControllerBase
    {
        private readonly ICommandHandler<RegisterWorkstationCommand, Workstation> _registerHandler;

        public WorkstationsController(ICommandHandler<RegisterWorkstationCommand, Workstation> registerHandler)
        {
            _registerHandler = registerHandler ?? throw new ArgumentNullException(nameof(registerHandler));
        }

        [HttpPost]
        public async Task<IActionResult> RegisterAsync([FromBody] RegisterWorkstationRequestDto request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { code = "INVALID_PAYLOAD", message = "Request body cannot be empty." });
            }

            var command = new RegisterWorkstationCommand
            {
                PcId = request.PcId,
                SiteId = request.SiteId,
                Hostname = request.Hostname,
                MacAddress = request.MacAddress,
                IpAddress = request.IpAddress,
                ClientVersion = request.ClientVersion,
                OsVersion = request.OsVersion
            };

            var result = await _registerHandler.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    code = result.ErrorCode ?? "REGISTRATION_FAILED",
                    message = result.ErrorMessage ?? "Workstation registration failed."
                });
            }

            return Ok(result.Value);
        }
    }

    public class RegisterWorkstationRequestDto
    {
        public string PcId { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
    }
}
