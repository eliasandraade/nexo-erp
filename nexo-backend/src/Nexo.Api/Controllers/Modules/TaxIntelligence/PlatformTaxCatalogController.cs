using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Catalogue administration — platform admins only. Versions are never edited; only their status moves.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = "Platform")]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxCatalogController : ControllerBase
{
    private readonly TaxCatalogService _catalog;

    public PlatformTaxCatalogController(TaxCatalogService catalog) => _catalog = catalog;

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TaxRuleDto>>> Rules(CancellationToken ct) => Ok(await _catalog.GetRulesAsync(ct));

    [HttpGet("legal-sources")]
    public async Task<ActionResult<IReadOnlyList<TaxLegalSourceDto>>> LegalSources(CancellationToken ct) => Ok(await _catalog.GetLegalSourcesAsync(ct));

    [HttpPost("legal-sources")]
    public async Task<ActionResult<TaxLegalSourceDto>> CreateLegalSource([FromBody] CreateLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateLegalSourceAsync(request, ct));

    /// <summary>Records that the excerpt was compared with the official text.</summary>
    [HttpPost("legal-sources/{code}/verify")]
    public async Task<ActionResult<TaxLegalSourceDto>> VerifyLegalSource(string code, [FromBody] VerifyLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.VerifyLegalSourceAsync(code, request, ct));

    [HttpPost("rules")]
    public async Task<ActionResult<TaxRuleDto>> CreateRule([FromBody] CreateTaxRuleRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateRuleAsync(request, ct));

    /// <summary>Creates a new version as a draft (content fixed and fingerprinted).</summary>
    [HttpPost("rules/{key}/versions")]
    public async Task<ActionResult<TaxRuleVersionDto>> CreateVersion(string key, [FromBody] CreateTaxRuleVersionRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateVersionAsync(key, request, ct));

    /// <summary>Moves a version's status. Activation requires verified legal sources and supersedes fully covered older versions.</summary>
    [HttpPost("rule-versions/{id:guid}/status")]
    public async Task<ActionResult<TaxRuleVersionDto>> ChangeStatus(Guid id, [FromBody] ChangeTaxRuleVersionStatusRequest request, CancellationToken ct)
        => Ok(await _catalog.ChangeStatusAsync(id, request, ct));

    /// <summary>
    /// Records a tax professional's review of a version (Pending/Approved/ChangesRequired/Rejected).
    /// Separate from the technical status: it never activates or deactivates anything. Audited.
    /// </summary>
    [HttpPost("rule-versions/{id:guid}/professional-review")]
    public async Task<ActionResult<TaxRuleVersionDto>> RecordProfessionalReview(Guid id, [FromBody] RecordProfessionalReviewRequest request, CancellationToken ct)
        => Ok(await _catalog.RecordProfessionalReviewAsync(id, request, ct));

    /// <summary>
    /// Records the internal product/governance approval of an active version for the controlled pilot.
    /// It is not a professional review and never changes it; versions awaiting regulation or with an
    /// unverified source cannot be approved. Audited.
    /// </summary>
    [HttpPost("rule-versions/{id:guid}/pilot-approval")]
    public async Task<ActionResult<TaxRuleVersionDto>> ApproveForPilot(Guid id, [FromBody] ApproveRuleForPilotRequest request, CancellationToken ct)
        => Ok(await _catalog.ApproveForPilotAsync(id, request, ct));

    /// <summary>Withdraws the internal pilot approval of a version (e.g. a suspension criterion was met). Audited.</summary>
    [HttpPost("rule-versions/{id:guid}/pilot-approval/withdraw")]
    public async Task<ActionResult<TaxRuleVersionDto>> WithdrawPilotApproval(Guid id, [FromBody] WithdrawPilotApprovalRequest request, CancellationToken ct)
        => Ok(await _catalog.WithdrawPilotApprovalAsync(id, request, ct));
}
