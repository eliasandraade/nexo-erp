using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build observability for the platform (SuperAdmin, Wave 6). Reads use the platform read policy (Platform: a platform
/// token — a tenant or impersonation token is refused), like the other platform reads; retrying a failed processing job is an
/// admin act (PlatformAdmin: super_admin with a two-step sign-in), audited with the platform actor.
/// </summary>
[ApiController]
[Route("api/platform/build")]
[Authorize(Policy = "Platform")]
public class PlatformBuildController : ControllerBase
{
    private readonly BuildPlatformObservabilityService _service;

    public PlatformBuildController(BuildPlatformObservabilityService service) => _service = service;

    /// <summary>Per company with the Build module: projects, portals, model versions, jobs, storage by category, AI (30 days), flags.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<BuildPlatformOverviewDto>> Overview(CancellationToken ct) => Ok(await _service.OverviewAsync(ct));

    /// <summary>Recent processing jobs with a status (Failed by default; Pending, Running, Done), newest first (take ≤ 200).</summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<BuildPlatformJobDto>>> Jobs([FromQuery] string? status, [FromQuery] int? take, CancellationToken ct)
        => Ok(await _service.JobsAsync(status, take, ct));

    /// <summary>Queues a failed job again (new job of the same kind). PlatformAdmin with two-step sign-in; audited.</summary>
    [HttpPost("jobs/{jobId:guid}/retry")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<ActionResult<BuildPlatformJobDto>> Retry(Guid jobId, CancellationToken ct)
        => Ok(await _service.RetryAsync(jobId, Actor(), ct));

    private PlatformActor Actor()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return new PlatformActor(Guid.TryParse(raw, out var id) ? id : null,
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
            Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
    }
}
