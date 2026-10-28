using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Opportunity review workflow and decision dossiers.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxOpportunitiesController : ControllerBase
{
    private readonly TaxOpportunityService _service;

    public TaxOpportunitiesController(TaxOpportunityService service) => _service = service;

    [HttpGet("opportunities")]
    public async Task<ActionResult<IReadOnlyList<TaxOpportunitySummaryDto>>> List(
        [FromQuery] TaxOpportunityStatus? status, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _service.ListAsync(status, take, ct));

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
    [Authorize(Roles = AppRoles.Directors)]
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
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxOpportunityDto>> Approve(Guid id, [FromBody] ApproveOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.ApproveAsync(id, request, ct));

    [HttpGet("decisions/{id:guid}")]
    public async Task<ActionResult<TaxDecisionDto>> Decision(Guid id, CancellationToken ct) => Ok(await _service.GetDecisionAsync(id, ct));
}
