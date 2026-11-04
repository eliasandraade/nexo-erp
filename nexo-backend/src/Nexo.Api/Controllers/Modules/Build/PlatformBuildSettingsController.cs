using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Platform (SuperAdmin) control of a company's costly Orken Build features: Build3D, BuildAI, BuildVision, BuildPredict.
/// PlatformAdmin only (platform token with super_admin and, by default, a two-step sign-in) — a tenant token, an impersonation
/// token or a platform support user is refused. Every change is audited with the platform actor.
/// </summary>
[ApiController]
[Route("api/platform/tenants/{tenantId:guid}/build-settings")]
[Authorize(Policy = "PlatformAdmin")]
public class PlatformBuildSettingsController : ControllerBase
{
    private readonly BuildTenantSettingsService _settings;

    public PlatformBuildSettingsController(BuildTenantSettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<ActionResult<BuildTenantSettingsDto>> Get(Guid tenantId, CancellationToken ct) => Ok(await _settings.GetAsync(tenantId, ct));

    /// <summary>Changes only the flags sent (null keeps the current value). Audited.</summary>
    [HttpPut]
    public async Task<ActionResult<BuildTenantSettingsDto>> Update(Guid tenantId, [FromBody] UpdateBuildTenantSettingsRequest request,
        CancellationToken ct)
        => Ok(await _settings.UpdateAsync(tenantId, request, Actor(), ct));

    private PlatformActor Actor()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return new PlatformActor(Guid.TryParse(raw, out var id) ? id : null,
            User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
            Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
    }
}
