using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Filters of the opportunity list (query string).</summary>
public sealed class OpportunityListQuery
{
    public TaxOpportunityStatus? Status { get; set; }
    public TaxRiskLevel? Risk { get; set; }
    public TaxOpportunityType[]? Type { get; set; }
    public bool? Open { get; set; }
    public bool? Mine { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

/// <summary>Opportunity review workflow: list, timeline, assignment, requirements, evidence review, decision and execution.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxOpportunitiesController : ControllerBase
{
    private readonly TaxOpportunityService _service;

    public TaxOpportunitiesController(TaxOpportunityService service) => _service = service;

    [HttpGet("opportunities")]
    public async Task<ActionResult<TaxPage<TaxOpportunitySummaryDto>>> List([FromQuery] OpportunityListQuery q, CancellationToken ct)
        => Ok(await _service.ListAsync(new TaxOpportunityFilter(q.Status, q.Risk, q.Type, q.Open, q.Mine, q.From, q.To, q.Search, q.Skip, q.Take), ct));

    /// <summary>What happened to the opportunity, from the audit trail (who, when, what).</summary>
    [HttpGet("opportunities/{id:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<TaxTimelineEventDto>>> Timeline(Guid id, CancellationToken ct) => Ok(await _service.TimelineAsync(id, ct));

    /// <summary>Assigns who moves the analysis forward — a work assignment, never an approval power.</summary>
    [HttpPost("opportunities/{id:guid}/assign")]
    [Authorize(Roles = TaxCapabilities.Analyze)]
    public async Task<ActionResult<TaxOpportunityDto>> Assign(Guid id, [FromBody] AssignOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.AssignAsync(id, request, ct));

    [HttpGet("opportunities/{id:guid}")]
    public async Task<ActionResult<TaxOpportunityDto>> Get(Guid id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

    [HttpPost("opportunities/{id:guid}/review")]
    public async Task<ActionResult<TaxOpportunityDto>> StartReview(Guid id, CancellationToken ct) => Ok(await _service.StartReviewAsync(id, ct));

    /// <summary>Answers a requirement. Some "not met" answers block the opportunity as a prohibited structure.</summary>
    [HttpPost("opportunities/{id:guid}/requirements/{requirementId:guid}")]
    public async Task<ActionResult<TaxOpportunityDto>> AnswerRequirement(Guid id, Guid requirementId, [FromBody] AnswerRequirementRequest request, CancellationToken ct)
        => Ok(await _service.AnswerRequirementAsync(id, requirementId, request, ct));

    [HttpPost("opportunities/{id:guid}/evidence")]
    public async Task<ActionResult<TaxOpportunityDto>> AddEvidence(Guid id, [FromBody] AddEvidenceRequest request, CancellationToken ct)
        => Ok(await _service.AddEvidenceAsync(id, request, ct));

    /// <summary>Accepts or rejects a piece of evidence; a rejected one supports no requirement.</summary>
    [HttpPost("opportunities/{id:guid}/evidence/{evidenceId:guid}/review")]
    public async Task<ActionResult<TaxOpportunityDto>> ReviewEvidence(Guid id, Guid evidenceId, [FromBody] ReviewEvidenceRequest request, CancellationToken ct)
        => Ok(await _service.ReviewEvidenceAsync(id, evidenceId, request, ct));

    /// <summary>
    /// After the operation: records whether it happened as the scenario was approved (authorised fiscal
    /// document, final amounts) or diverged. Diretoria only; sealed with its own dossier hash.
    /// </summary>
    [HttpPost("opportunities/{id:guid}/execution")]
    [Authorize(Roles = TaxCapabilities.Decide)]
    public async Task<ActionResult<TaxOpportunityDto>> VerifyExecution(Guid id, [FromBody] VerifyExecutionRequest request, CancellationToken ct)
        => Ok(await _service.VerifyExecutionAsync(id, request, ct));

    [HttpPost("opportunities/{id:guid}/block")]
    public async Task<ActionResult<TaxOpportunityDto>> Block(Guid id, [FromBody] BlockOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.BlockAsync(id, request, ct));

    [HttpPost("opportunities/{id:guid}/reject")]
    public async Task<ActionResult<TaxOpportunityDto>> Reject(Guid id, [FromBody] RejectOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.RejectAsync(id, request, ct));

    /// <summary>
    /// Approves the scenario and records the decision dossier — it does not execute any commercial or fiscal
    /// change. Diretoria only; high risk must be acknowledged; the maker-checker requirement follows risk and
    /// materiality; with the approval policy on, every rule behind it must be approved for the mode —
    /// internally for the controlled pilot, professionally in the definitive mode.
    /// </summary>
    [HttpPost("opportunities/{id:guid}/approve")]
    [Authorize(Roles = TaxCapabilities.Decide)]
    public async Task<ActionResult<TaxOpportunityDto>> Approve(Guid id, [FromBody] ApproveOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.ApproveAsync(id, request, ct));
}

/// <summary>Frozen decision dossiers: read, and exported as a ZIP with a SHA-256 manifest (the board only).</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
public class TaxDecisionsController : ControllerBase
{
    private readonly TaxOpportunityService _service;
    private readonly TaxDossierExportService _exports;

    public TaxDecisionsController(TaxOpportunityService service, TaxDossierExportService exports)
    {
        _service = service;
        _exports = exports;
    }

    [HttpGet("decisions/{id:guid}")]
    public async Task<ActionResult<TaxDecisionDto>> Decision(Guid id, CancellationToken ct) => Ok(await _service.GetDecisionAsync(id, ct));

    /// <summary>
    /// The decision dossier as a ZIP (JSON exactly as frozen, readable version, sources, evidence index, files when asked for, SHA-256 manifest).
    /// The board only; another company's dossier is simply not found.
    /// </summary>
    [HttpGet("decisions/{id:guid}/export")]
    [Authorize(Roles = TaxCapabilities.Export)]
    public async Task<IActionResult> ExportDecision(Guid id, [FromQuery] bool includeFiles = false, CancellationToken ct = default)
    {
        var (name, content) = await _exports.ExportAsync(id, includeFiles, ct);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/zip", name);
    }
}
