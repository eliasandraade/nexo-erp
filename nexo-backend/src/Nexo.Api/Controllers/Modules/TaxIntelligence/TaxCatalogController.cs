using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Rule catalogue and legal sources, read-only for tenants.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxCatalogController : ControllerBase
{
    private readonly TaxCatalogService _catalog;
    private readonly TaxApprovalGate _approvals;

    public TaxCatalogController(TaxCatalogService catalog, TaxApprovalGate approvals)
    {
        _catalog = catalog;
        _approvals = approvals;
    }

    /// <summary>
    /// Where this company/store stands today: kill switch, approval mode, whether new work is admitted (and
    /// why not) and, in the controlled pilot, its shadow-mode notice and scope.
    /// </summary>
    [HttpGet("governance")]
    public async Task<ActionResult<TaxGovernanceStatusDto>> Governance(CancellationToken ct) => Ok(await _approvals.StatusAsync(ct));

    /// <summary>Rule catalogue with every version, status and legal sources.</summary>
    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TaxRuleDto>>> Rules(CancellationToken ct) => Ok(await _catalog.GetRulesAsync(ct));

    /// <summary>Legal sources with their literal excerpts and verification.</summary>
    [HttpGet("legal-sources")]
    public async Task<ActionResult<IReadOnlyList<TaxLegalSourceDto>>> LegalSources(CancellationToken ct) => Ok(await _catalog.GetLegalSourcesAsync(ct));
}
