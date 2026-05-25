using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Dashboard;
using Nexo.Infrastructure.Dashboard;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _service;

    public DashboardController(DashboardService service) => _service = service;

    /// <summary>
    /// Returns all KPIs, top products/sellers, daily chart data, and stock alerts
    /// in a single aggregated response. Replaces multiple client-side list fetches.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken ct) =>
        Ok(await _service.GetSummaryAsync(ct));
}
