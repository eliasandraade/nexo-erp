using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 3 "Qualidade e pós-obra": checklist templates, inspections, non-conformities, quality summary,
/// handover (walkthrough, delivery, acceptance), warranty terms and technical assistance. Company side (Management,
/// module build). Every change is audited. See docs/build/internal-api.md (# Wave 3).
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[ValidateBody]
public class BuildQualityController : ControllerBase
{
    private readonly BuildQualityService _quality;
    private readonly BuildPostConstructionService _post;

    public BuildQualityController(BuildQualityService quality, BuildPostConstructionService post)
    {
        _quality = quality;
        _post = post;
    }

    // ── Checklist templates ───────────────────────────────────────────────────

    [HttpGet("checklist-templates")]
    public async Task<ActionResult<IReadOnlyList<BuildChecklistTemplateDto>>> GetTemplates(CancellationToken ct)
        => Ok(await _quality.GetTemplatesAsync(ct));

    [HttpGet("checklist-templates/{id:guid}")]
    public async Task<ActionResult<BuildChecklistTemplateDto>> GetTemplate(Guid id, CancellationToken ct)
        => Ok(await _quality.GetTemplateAsync(id, ct));

    [HttpPost("checklist-templates")]
    public async Task<ActionResult<BuildChecklistTemplateDto>> CreateTemplate([FromBody] SaveBuildChecklistTemplateRequest request,
        CancellationToken ct)
    {
        var dto = await _quality.CreateTemplateAsync(request, ct);
        return CreatedAtAction(nameof(GetTemplate), new { id = dto.Id }, dto);
    }

    /// <summary>Replaces the template (items included); past inspections keep their own copy.</summary>
    [HttpPut("checklist-templates/{id:guid}")]
    public async Task<ActionResult<BuildChecklistTemplateDto>> UpdateTemplate(Guid id,
        [FromBody] SaveBuildChecklistTemplateRequest request, CancellationToken ct)
        => Ok(await _quality.UpdateTemplateAsync(id, request, ct));

    [HttpDelete("checklist-templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        await _quality.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    // ── Inspections ───────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/inspections")]
    public async Task<ActionResult<IReadOnlyList<BuildInspectionDto>>> GetInspections(Guid projectId, [FromQuery] string? kind,
        [FromQuery] string? status, CancellationToken ct)
        => Ok(await _quality.ListInspectionsAsync(projectId, kind, status, ct));

    /// <summary>Creates a quality inspection; the template's items are copied (snapshot).</summary>
    [HttpPost("projects/{projectId:guid}/inspections")]
    public async Task<ActionResult<BuildInspectionDto>> CreateInspection(Guid projectId, [FromBody] CreateBuildInspectionRequest request,
        CancellationToken ct)
    {
        var dto = await _quality.CreateInspectionAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetInspection), new { id = dto.Id }, dto);
    }

    [HttpGet("inspections/{id:guid}")]
    public async Task<ActionResult<BuildInspectionDto>> GetInspection(Guid id, CancellationToken ct)
        => Ok(await _quality.GetInspectionAsync(id, ct));

    [HttpPut("inspections/{id:guid}")]
    public async Task<ActionResult<BuildInspectionDto>> UpdateInspection(Guid id, [FromBody] UpdateBuildInspectionRequest request,
        CancellationToken ct)
        => Ok(await _quality.UpdateInspectionAsync(id, request, ct));

    [HttpPost("inspections/{id:guid}/start")]
    public async Task<ActionResult<BuildInspectionDto>> StartInspection(Guid id, CancellationToken ct)
        => Ok(await _quality.StartInspectionAsync(id, ct));

    [HttpPut("inspections/{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<BuildInspectionDto>> RecordItem(Guid id, Guid itemId,
        [FromBody] RecordBuildInspectionItemRequest request, CancellationToken ct)
        => Ok(await _quality.RecordItemAsync(id, itemId, request, ct));

    /// <summary>Completes with the computed outcome; each NotOk item opens a non-conformity.</summary>
    [HttpPost("inspections/{id:guid}/complete")]
    public async Task<ActionResult<BuildInspectionDto>> CompleteInspection(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteBuildInspectionRequest? request, CancellationToken ct)
        => Ok(await _quality.CompleteInspectionAsync(id, request?.Notes, ct));

    [HttpPost("inspections/{id:guid}/cancel")]
    public async Task<ActionResult<BuildInspectionDto>> CancelInspection(Guid id, CancellationToken ct)
        => Ok(await _quality.CancelInspectionAsync(id, ct));

    // ── Non-conformities ──────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/non-conformities")]
    public async Task<ActionResult<IReadOnlyList<BuildNonConformityDto>>> GetNonConformities(Guid projectId, [FromQuery] string? status,
        [FromQuery] string? severity, [FromQuery] bool? overdue, CancellationToken ct)
        => Ok(await _quality.ListNonConformitiesAsync(projectId, status, severity, overdue, ct));

    [HttpPost("projects/{projectId:guid}/non-conformities")]
    public async Task<ActionResult<BuildNonConformityDto>> CreateNonConformity(Guid projectId,
        [FromBody] SaveBuildNonConformityRequest request, CancellationToken ct)
    {
        var dto = await _quality.CreateNonConformityAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetNonConformity), new { id = dto.Id }, dto);
    }

    [HttpGet("non-conformities/{id:guid}")]
    public async Task<ActionResult<BuildNonConformityDto>> GetNonConformity(Guid id, CancellationToken ct)
        => Ok(await _quality.GetNonConformityAsync(id, ct));

    [HttpPut("non-conformities/{id:guid}")]
    public async Task<ActionResult<BuildNonConformityDto>> UpdateNonConformity(Guid id, [FromBody] SaveBuildNonConformityRequest request,
        CancellationToken ct)
        => Ok(await _quality.UpdateNonConformityAsync(id, request, ct));

    [HttpPost("non-conformities/{id:guid}/start")]
    public async Task<ActionResult<BuildNonConformityDto>> StartNonConformity(Guid id, CancellationToken ct)
        => Ok(await _quality.StartTreatmentAsync(id, ct));

    [HttpPost("non-conformities/{id:guid}/resolve")]
    public async Task<ActionResult<BuildNonConformityDto>> ResolveNonConformity(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResolveBuildNonConformityRequest? request, CancellationToken ct)
        => Ok(await _quality.ResolveNonConformityAsync(id, request, ct));

    [HttpPost("non-conformities/{id:guid}/verify")]
    public async Task<ActionResult<BuildNonConformityDto>> VerifyNonConformity(Guid id, CancellationToken ct)
        => Ok(await _quality.VerifyNonConformityAsync(id, ct));

    /// <summary>The verification failed: Resolved → InTreatment.</summary>
    [HttpPost("non-conformities/{id:guid}/reopen")]
    public async Task<ActionResult<BuildNonConformityDto>> ReopenNonConformity(Guid id, CancellationToken ct)
        => Ok(await _quality.ReopenNonConformityAsync(id, ct));

    [HttpPost("non-conformities/{id:guid}/cancel")]
    public async Task<ActionResult<BuildNonConformityDto>> CancelNonConformity(Guid id,
        [FromBody] CancelBuildNonConformityRequest request, CancellationToken ct)
        => Ok(await _quality.CancelNonConformityAsync(id, request.Reason, ct));

    [HttpGet("projects/{projectId:guid}/quality/summary")]
    public async Task<ActionResult<BuildQualitySummaryDto>> GetQualitySummary(Guid projectId, CancellationToken ct)
        => Ok(await _quality.GetSummaryAsync(projectId, ct));

    // ── Handover ──────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/handover")]
    public async Task<ActionResult<BuildHandoverDto>> GetHandover(Guid projectId, CancellationToken ct)
        => Ok(await _post.GetHandoverAsync(projectId, ct));

    /// <summary>Creates (first call) or updates the handover: date, keys, documents (context build-handover), internal notes.</summary>
    [HttpPut("projects/{projectId:guid}/handover")]
    public async Task<ActionResult<BuildHandoverDto>> SaveHandover(Guid projectId, [FromBody] SaveBuildHandoverRequest request,
        CancellationToken ct)
        => Ok(await _post.SaveHandoverAsync(projectId, request, ct));

    /// <summary>Creates the walkthrough with the client (inspection of kind ClientWalkthrough).</summary>
    [HttpPost("projects/{projectId:guid}/handover/walkthrough")]
    public async Task<ActionResult<BuildInspectionDto>> CreateWalkthrough(Guid projectId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateBuildWalkthroughRequest? request, CancellationToken ct)
    {
        var dto = await _post.CreateWalkthroughAsync(projectId, request ?? new CreateBuildWalkthroughRequest(), ct);
        return CreatedAtAction(nameof(GetInspection), new { id = dto.Id }, dto);
    }

    /// <summary>Delivers: refused with an open Critical NC; completes the project; starts/creates the warranty terms.</summary>
    [HttpPost("projects/{projectId:guid}/handover/deliver")]
    public async Task<ActionResult<BuildHandoverDto>> DeliverHandover(Guid projectId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeliverBuildHandoverRequest? request, CancellationToken ct)
        => Ok(await _post.DeliverAsync(projectId, request, ct));

    /// <summary>Records the client's acceptance obtained outside the portal (channel Internal).</summary>
    [HttpPost("projects/{projectId:guid}/handover/accept")]
    public async Task<ActionResult<BuildHandoverDto>> AcceptHandover(Guid projectId, [FromBody] AcceptBuildHandoverRequest request,
        CancellationToken ct)
        => Ok(await _post.AcceptAsync(projectId, request, ct));

    // ── Warranty ──────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/warranty")]
    public async Task<ActionResult<IReadOnlyList<BuildWarrantyTermDto>>> GetWarranty(Guid projectId, CancellationToken ct)
        => Ok(await _post.ListWarrantyAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/warranty")]
    public async Task<ActionResult<BuildWarrantyTermDto>> CreateWarranty(Guid projectId, [FromBody] SaveBuildWarrantyTermRequest request,
        CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _post.CreateWarrantyAsync(projectId, request, ct));

    /// <summary>Creates the default reference terms (not legal advice) when the project has none.</summary>
    [HttpPost("projects/{projectId:guid}/warranty/defaults")]
    public async Task<ActionResult<IReadOnlyList<BuildWarrantyTermDto>>> CreateDefaultWarranty(Guid projectId, CancellationToken ct)
        => Ok(await _post.CreateDefaultWarrantyAsync(projectId, ct));

    [HttpPut("warranty-terms/{id:guid}")]
    public async Task<ActionResult<BuildWarrantyTermDto>> UpdateWarranty(Guid id, [FromBody] SaveBuildWarrantyTermRequest request,
        CancellationToken ct)
        => Ok(await _post.UpdateWarrantyAsync(id, request, ct));

    [HttpDelete("warranty-terms/{id:guid}")]
    public async Task<IActionResult> DeleteWarranty(Guid id, CancellationToken ct)
    {
        await _post.DeleteWarrantyAsync(id, ct);
        return NoContent();
    }

    // ── Assistance ────────────────────────────────────────────────────────────

    /// <summary>Every request of the company (filters: project, status, covered).</summary>
    [HttpGet("assistance")]
    public async Task<ActionResult<IReadOnlyList<BuildAssistanceDto>>> GetAllAssistance([FromQuery] Guid? projectId,
        [FromQuery] string? status, [FromQuery] bool? covered, CancellationToken ct)
        => Ok(await _post.ListAssistanceAsync(projectId, status, covered, ct));

    [HttpGet("projects/{projectId:guid}/assistance")]
    public async Task<ActionResult<IReadOnlyList<BuildAssistanceDto>>> GetAssistance(Guid projectId, [FromQuery] string? status,
        [FromQuery] bool? covered, CancellationToken ct)
        => Ok(await _post.ListAssistanceAsync(projectId, status, covered, ct));

    [HttpPost("projects/{projectId:guid}/assistance")]
    public async Task<ActionResult<BuildAssistanceDto>> OpenAssistance(Guid projectId, [FromBody] OpenBuildAssistanceRequest request,
        CancellationToken ct)
    {
        var dto = await _post.OpenAssistanceAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetAssistanceRequest), new { id = dto.Id }, dto);
    }

    [HttpGet("assistance/{id:guid}")]
    public async Task<ActionResult<BuildAssistanceDto>> GetAssistanceRequest(Guid id, CancellationToken ct)
        => Ok(await _post.GetAssistanceAsync(id, ct));

    /// <summary>Evaluates the warranty coverage (term of the category on the opening date) unless warrantyCovered is given.</summary>
    [HttpPost("assistance/{id:guid}/triage")]
    public async Task<ActionResult<BuildAssistanceDto>> TriageAssistance(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TriageBuildAssistanceRequest? request, CancellationToken ct)
        => Ok(await _post.TriageAsync(id, request, ct));

    [HttpPost("assistance/{id:guid}/schedule")]
    public async Task<ActionResult<BuildAssistanceDto>> ScheduleAssistance(Guid id, [FromBody] ScheduleBuildAssistanceRequest request,
        CancellationToken ct)
        => Ok(await _post.ScheduleAsync(id, request.ScheduledFor, ct));

    [HttpPost("assistance/{id:guid}/start")]
    public async Task<ActionResult<BuildAssistanceDto>> StartAssistance(Guid id, CancellationToken ct)
        => Ok(await _post.StartAssistanceAsync(id, ct));

    [HttpPost("assistance/{id:guid}/resolve")]
    public async Task<ActionResult<BuildAssistanceDto>> ResolveAssistance(Guid id, [FromBody] ResolveBuildAssistanceRequest request,
        CancellationToken ct)
        => Ok(await _post.ResolveAssistanceAsync(id, request.ResolutionNotes, ct));

    [HttpPost("assistance/{id:guid}/close")]
    public async Task<ActionResult<BuildAssistanceDto>> CloseAssistance(Guid id, CancellationToken ct)
        => Ok(await _post.CloseAssistanceAsync(id, ct));

    [HttpPost("assistance/{id:guid}/reject")]
    public async Task<ActionResult<BuildAssistanceDto>> RejectAssistance(Guid id, [FromBody] RejectBuildAssistanceRequest request,
        CancellationToken ct)
        => Ok(await _post.RejectAssistanceAsync(id, request.Reason, ct));

    /// <summary>Attaches a photo uploaded with the context build-assistance.</summary>
    [HttpPost("assistance/{id:guid}/photos")]
    public async Task<ActionResult<BuildAssistanceDto>> AddAssistancePhoto(Guid id, [FromBody] AddBuildAssistancePhotoRequest request,
        CancellationToken ct)
        => Ok(await _post.AddAssistancePhotoAsync(id, request.Key, ct));
}
