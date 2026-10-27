using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Tax Intelligence dashboard.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxOverviewController : ControllerBase
{
    private readonly TaxOverviewService _overview;

    public TaxOverviewController(TaxOverviewService overview) => _overview = overview;

    /// <summary>Last 30 days, credits, open opportunities by nature of impact, split divergences, rules in force.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<TaxOverviewDto>> Overview(CancellationToken ct) => Ok(await _overview.GetAsync(TaxToday.Brazil(), ct));
}
