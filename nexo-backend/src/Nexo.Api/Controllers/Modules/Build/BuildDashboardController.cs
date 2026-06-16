using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Application.Modules.Build.Interfaces;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Dashboard read-model for ORKEN BUILD — aggregated, tenant-scoped, real data only.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize]
[RequireModule("build")]
public class BuildDashboardController : ControllerBase
{
    private readonly IBuildDashboardQueryService _dashboard;

    public BuildDashboardController(IBuildDashboardQueryService dashboard)
        => _dashboard = dashboard;

    /// <summary>Returns Build dashboard aggregates for the current tenant.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<BuildDashboardDto>> Get(CancellationToken ct)
        => Ok(await _dashboard.GetAsync(ct));
}
