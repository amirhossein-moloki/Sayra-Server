using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;

        public HealthController(HealthCheckService healthCheckService)
        {
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        }

        // GET /api/health
        [HttpGet]
        public async Task<IActionResult> GetHealthAsync()
        {
            var report = await _healthCheckService.CheckHealthAsync();
            var response = new
            {
                status = report.Status.ToString(),
                duration = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.Select(e => new
                {
                    key = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                })
            };

            return report.Status == HealthStatus.Healthy ? Ok(response) : StatusCode(503, response);
        }

        // GET /api/health/live
        [HttpGet("live")]
        public IActionResult GetLive()
        {
            // Liveness must not depend on PostgreSQL or Redis
            return Ok(new { status = "Healthy", liveness = "Alive" });
        }

        // GET /api/health/ready
        [HttpGet("ready")]
        public async Task<IActionResult> GetReadyAsync()
        {
            // Readiness must verify database and Redis
            var report = await _healthCheckService.CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            var response = new
            {
                status = report.Status.ToString(),
                duration = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.Select(e => new
                {
                    key = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                })
            };

            return report.Status == HealthStatus.Healthy ? Ok(response) : StatusCode(503, response);
        }
    }
}
