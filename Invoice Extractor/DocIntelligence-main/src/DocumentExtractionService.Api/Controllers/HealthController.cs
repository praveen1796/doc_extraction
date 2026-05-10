using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentExtractionService.Api.Controllers;

/// <summary>
/// Health check endpoints for liveness, readiness, and detailed diagnostics.
/// These endpoints are intentionally unauthenticated (safe for load balancer probes).
/// </summary>
[ApiController]
[Route("health")]
[Tags("Health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly DocumentTypeRegistry _registry;
    private readonly ILogger<HealthController> _logger;
    private readonly IWebHostEnvironment _env;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public HealthController(
        DocumentTypeRegistry registry,
        ILogger<HealthController> logger,
        IWebHostEnvironment env)
    {
        _registry = registry;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Liveness probe — returns 200 if service is running. Used by Kubernetes/Docker.
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Live() => Ok(new { status = "alive", timestamp = DateTime.UtcNow });

    /// <summary>
    /// Readiness probe — returns 200 only if service is ready to handle requests.
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Ready()
    {
        var types = _registry.GetAllDocumentTypes();
        if (types.Count == 0)
        {
            _logger.LogWarning("Readiness check failed: no document types loaded");
            return StatusCode(503, new
            {
                status = "not_ready",
                reason = "No document types loaded",
                timestamp = DateTime.UtcNow
            });
        }

        return Ok(new
        {
            status = "ready",
            documentTypesLoaded = types.Count,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Detailed health report — shows uptime, loaded document types, and configuration status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Detailed()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var types = _registry.GetAllDocumentTypes();

        return Ok(new
        {
            status = "healthy",
            service = "DocumentExtractionService",
            version = "1.0.0",
            environment = _env.EnvironmentName,
            uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
            startTime = _startTime,
            timestamp = DateTime.UtcNow,
            documentTypes = new
            {
                total = types.Count,
                enabled = types.Count(t => t.Enabled),
                disabled = types.Count(t => !t.Enabled),
                types = types.Select(t => new
                {
                    id = t.Id,
                    displayName = t.DisplayName,
                    enabled = t.Enabled,
                    version = t.Version
                })
            }
        });
    }
}
