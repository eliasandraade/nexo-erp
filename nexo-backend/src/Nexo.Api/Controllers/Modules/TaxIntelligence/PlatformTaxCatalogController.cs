using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>
/// Catalogue administration — rules, versions, legal sources, professional reviews and internal pilot approvals. Platform operators
/// read; only platform administrators change anything. Versions are never edited; only their status moves.
/// </summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxCatalogController : ControllerBase
{
    private readonly TaxCatalogService _catalog;

    public PlatformTaxCatalogController(TaxCatalogService catalog) => _catalog = catalog;

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TaxRuleDto>>> Rules(CancellationToken ct) => Ok(await _catalog.GetRulesAsync(ct));

    [HttpGet("legal-sources")]
    public async Task<ActionResult<IReadOnlyList<TaxLegalSourceDto>>> LegalSources(CancellationToken ct) => Ok(await _catalog.GetLegalSourcesAsync(ct));

    /// <summary>Registers a legal source (unverified). A source never activates a rule by itself.</summary>
    [HttpPost("legal-sources")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxLegalSourceDto>> CreateLegalSource([FromBody] CreateLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateLegalSourceAsync(request, ct));

    /// <summary>Records that the excerpt was compared with the official text (who compared, as informed; who recorded, authenticated).</summary>
    [HttpPost("legal-sources/{code}/verify")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxLegalSourceDto>> VerifyLegalSource(string code, [FromBody] VerifyLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.VerifyLegalSourceAsync(code, request, ct));

    [HttpPost("rules")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleDto>> CreateRule([FromBody] CreateTaxRuleRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateRuleAsync(request, ct));

    /// <summary>Creates a new version as a draft (content fixed and fingerprinted).</summary>
    [HttpPost("rules/{key}/versions")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleVersionDto>> CreateVersion(string key, [FromBody] CreateTaxRuleVersionRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateVersionAsync(key, request, ct));

    /// <summary>Moves a version's status. Activation requires verified legal sources and supersedes fully covered older versions.</summary>
    [HttpPost("rule-versions/{id:guid}/status")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleVersionDto>> ChangeStatus(Guid id, [FromBody] ChangeTaxRuleVersionStatusRequest request, CancellationToken ct)
        => Ok(await _catalog.ChangeStatusAsync(id, request, ct));

    /// <summary>
    /// Records a tax professional's review of a version, as received. The professional is as informed; the authenticated user who records
    /// it and the server time are kept apart. Separate from the technical status: it never activates or deactivates anything. Audited.
    /// </summary>
    [HttpPost("rule-versions/{id:guid}/professional-review")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleVersionDto>> RecordProfessionalReview(Guid id, [FromBody] RecordProfessionalReviewRequest request, CancellationToken ct)
        => Ok(await _catalog.RecordProfessionalReviewAsync(id, request, ct));

    /// <summary>Every professional review recorded for a version, newest first.</summary>
    [HttpGet("rule-versions/{id:guid}/professional-reviews")]
    public async Task<ActionResult<IReadOnlyList<TaxProfessionalReviewRecordDto>>> ProfessionalReviews(Guid id, CancellationToken ct)
        => Ok(await _catalog.ReviewHistoryAsync(id, ct));

    /// <summary>
    /// Records the internal product/governance approval of an active version for the controlled pilot.
    /// It is not a professional review and never changes it; versions awaiting regulation or with an
    /// unverified source cannot be approved. Audited.
    /// </summary>
    [HttpPost("rule-versions/{id:guid}/pilot-approval")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleVersionDto>> ApproveForPilot(Guid id, [FromBody] ApproveRuleForPilotRequest request, CancellationToken ct)
        => Ok(await _catalog.ApproveForPilotAsync(id, request, ct));

    /// <summary>Withdraws the internal pilot approval of a version (e.g. a suspension criterion was met). Audited.</summary>
    [HttpPost("rule-versions/{id:guid}/pilot-approval/withdraw")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxRuleVersionDto>> WithdrawPilotApproval(Guid id, [FromBody] WithdrawPilotApprovalRequest request, CancellationToken ct)
        => Ok(await _catalog.WithdrawPilotApprovalAsync(id, request, ct));
}

/// <summary>The controlled pilot scope: window, decision record and participants (tenants / stores). Changes are audited.</summary>
[ApiController]
[Route("api/platform/tax/pilot")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxPilotController : ControllerBase
{
    private readonly TaxCatalogService _catalog;

    public PlatformTaxPilotController(TaxCatalogService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<TaxPilotScopeDto>> GetPilotScope(CancellationToken ct) => Ok(await _catalog.GetPilotScopeAsync(ct));

    /// <summary>Opens or changes the pilot window. Who and when come from the authenticated request.</summary>
    [HttpPut]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxPilotScopeDto>> ConfigurePilotScope([FromBody] ConfigurePilotScopeRequest request, CancellationToken ct)
        => Ok(await _catalog.ConfigurePilotScopeAsync(request, ct));

    /// <summary>Adds a tenant (or one of its stores) to the pilot. No tenant takes part by default.</summary>
    [HttpPost("participants")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxPilotScopeDto>> AddPilotParticipant([FromBody] AddPilotParticipantRequest request, CancellationToken ct)
        => Ok(await _catalog.AddPilotParticipantAsync(request, ct));

    /// <summary>Removes a participant: its new work is refused from then on; what it recorded stays readable.</summary>
    [HttpPost("participants/{id:guid}/remove")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxPilotScopeDto>> RemovePilotParticipant(Guid id, [FromBody] RemovePilotParticipantRequest request, CancellationToken ct)
        => Ok(await _catalog.RemovePilotParticipantAsync(id, request, ct));
}

/// <summary>
/// The module's runtime state and its administrative diagnosis. The configuration (kill switch, mode) is only shown here — it is
/// changed in the deployment. Activation needs the exact phrase and every precondition; any platform operator may suspend at once.
/// </summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = TaxCapabilities.PlatformRead)]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxModuleController : ControllerBase
{
    private readonly TaxModuleService _module;

    public PlatformTaxModuleController(TaxModuleService module) => _module = module;

    [HttpGet("module")]
    public async Task<ActionResult<TaxModuleStateDto>> State(CancellationToken ct) => Ok(await _module.GetStateAsync(ct));

    /// <summary>The preconditions of activating a mode today, item by item (nothing is changed).</summary>
    [HttpGet("module/checklist")]
    public async Task<ActionResult<TaxActivationChecklistDto>> Checklist([FromQuery] Nexo.Domain.Modules.TaxIntelligence.TaxApprovalMode mode, CancellationToken ct)
        => Ok(await _module.ChecklistAsync(mode, ct));

    [HttpPost("module/activate")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxModuleStateDto>> Activate([FromBody] ActivateTaxModuleRequest request, CancellationToken ct)
        => Ok(await _module.ActivateAsync(request, ct));

    /// <summary>Stops every new run at once — the first step of any incident. Reading and verifying stay available.</summary>
    [HttpPost("module/suspend")]
    public async Task<ActionResult<TaxModuleStateDto>> Suspend([FromBody] SuspendTaxModuleRequest request, CancellationToken ct)
        => Ok(await _module.SuspendAsync(request, ct));

    [HttpPost("module/deactivate")]
    [Authorize(Policy = TaxCapabilities.PlatformAdmin)]
    public async Task<ActionResult<TaxModuleStateDto>> Deactivate([FromBody] DeactivateTaxModuleRequest request, CancellationToken ct)
        => Ok(await _module.DeactivateAsync(request, ct));

    /// <summary>Administrative readiness of Tax Intelligence (never exposed in the public health check).</summary>
    [HttpGet("readiness")]
    public async Task<ActionResult<TaxReadinessDto>> Readiness(CancellationToken ct) => Ok(await _module.ReadinessAsync(ct));

    [HttpGet("refusals")]
    public async Task<ActionResult<IReadOnlyList<TaxRefusalDto>>> Refusals([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _module.RecentRefusalsAsync(take, ct));

    [HttpGet("audit")]
    public async Task<ActionResult<TaxPage<TaxAuditEventDto>>> Audit([FromQuery] string? action, [FromQuery] Guid? tenantId,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _module.AuditAsync(action, tenantId, skip, take, ct));
}
