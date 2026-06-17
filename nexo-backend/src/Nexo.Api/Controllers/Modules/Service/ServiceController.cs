using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Orken Service — engine bootstrap.
///
/// PR0 exposes only the resolved preset (labels + capability flags) so the frontend can adapt
/// a single set of screens per vertical (decision D2). Gated by the family-aware module gate
/// (decision D1): any active service-family subscription unlocks the engine.
/// </summary>
[ApiController]
[Route("api/v1/service")]
[Authorize]
[RequireServiceModule]
public class ServiceController : ControllerBase
{
    private readonly ServicePresetService _presets;

    public ServiceController(ServicePresetService presets)
    {
        _presets = presets;
    }

    /// <summary>
    /// Returns the active Service preset for the tenant — display labels and the capability
    /// flags that toggle which engine surfaces (agenda / OS / pacotes / …) are shown.
    /// </summary>
    [HttpGet("preset")]
    public async Task<ActionResult<ServicePresetDto>> GetPreset(CancellationToken ct)
    {
        var preset = await _presets.GetActivePresetAsync(ct);
        return preset is null ? NotFound() : Ok(preset);
    }
}
