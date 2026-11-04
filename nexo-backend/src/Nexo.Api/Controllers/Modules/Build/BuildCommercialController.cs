using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 2 "Comercial": budget templates, change orders (aditivos), client selections, materials and
/// measurements. Company side (Management, module build). Every change is audited. See docs/build/internal-api.md.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[ValidateBody]
public class BuildCommercialController : ControllerBase
{
    private readonly BuildBudgetCommercialService _budgets;
    private readonly BuildChangeOrderService _changeOrders;
    private readonly BuildSelectionService _selections;
    private readonly BuildMaterialService _materials;
    private readonly BuildMeasurementService _measurements;

    public BuildCommercialController(BuildBudgetCommercialService budgets, BuildChangeOrderService changeOrders,
        BuildSelectionService selections, BuildMaterialService materials, BuildMeasurementService measurements)
    {
        _budgets = budgets;
        _changeOrders = changeOrders;
        _selections = selections;
        _materials = materials;
        _measurements = measurements;
    }

    // ── Budget templates ──────────────────────────────────────────────────────

    [HttpGet("budget-templates")]
    public async Task<ActionResult<IReadOnlyList<BuildBudgetTemplateDto>>> GetTemplates(CancellationToken ct)
        => Ok(await _budgets.GetTemplatesAsync(ct));

    [HttpGet("budget-templates/{id:guid}")]
    public async Task<ActionResult<BuildBudgetTemplateDto>> GetTemplate(Guid id, CancellationToken ct)
        => Ok(await _budgets.GetTemplateAsync(id, ct));

    [HttpPost("budget-templates")]
    public async Task<ActionResult<BuildBudgetTemplateDto>> CreateTemplate([FromBody] SaveBuildBudgetTemplateRequest request,
        CancellationToken ct)
    {
        var dto = await _budgets.CreateTemplateAsync(request, ct);
        return CreatedAtAction(nameof(GetTemplate), new { id = dto.Id }, dto);
    }

    /// <summary>Replaces the template (items included).</summary>
    [HttpPut("budget-templates/{id:guid}")]
    public async Task<ActionResult<BuildBudgetTemplateDto>> UpdateTemplate(Guid id, [FromBody] SaveBuildBudgetTemplateRequest request,
        CancellationToken ct)
        => Ok(await _budgets.UpdateTemplateAsync(id, request, ct));

    [HttpDelete("budget-templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        await _budgets.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    /// <summary>Creates a Draft budget from the template (per-m² quantities × areaM2).</summary>
    [HttpPost("budget-templates/{id:guid}/budgets")]
    public async Task<ActionResult<BuildBudgetDto>> CreateBudgetFromTemplate(Guid id,
        [FromBody] CreateBudgetFromTemplateRequest request, CancellationToken ct)
    {
        var dto = await _budgets.CreateFromTemplateAsync(id, request, ct);
        return Created($"/api/v1/build/budgets/{dto.Id}", dto);
    }

    // ── Change orders ─────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/change-orders")]
    public async Task<ActionResult<IReadOnlyList<BuildChangeOrderDto>>> GetChangeOrders(Guid projectId,
        [FromQuery] string? status, CancellationToken ct)
        => Ok(await _changeOrders.ListAsync(projectId, status, ct));

    [HttpPost("projects/{projectId:guid}/change-orders")]
    public async Task<ActionResult<BuildChangeOrderDto>> CreateChangeOrder(Guid projectId,
        [FromBody] SaveBuildChangeOrderRequest request, CancellationToken ct)
    {
        var dto = await _changeOrders.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetChangeOrder), new { id = dto.Id }, dto);
    }

    [HttpGet("change-orders/{id:guid}")]
    public async Task<ActionResult<BuildChangeOrderDto>> GetChangeOrder(Guid id, CancellationToken ct)
        => Ok(await _changeOrders.GetAsync(id, ct));

    [HttpPut("change-orders/{id:guid}")]
    public async Task<ActionResult<BuildChangeOrderDto>> UpdateChangeOrder(Guid id, [FromBody] SaveBuildChangeOrderRequest request,
        CancellationToken ct)
        => Ok(await _changeOrders.UpdateAsync(id, request, ct));

    [HttpPost("change-orders/{id:guid}/send")]
    public async Task<ActionResult<BuildChangeOrderDto>> SendChangeOrder(Guid id, CancellationToken ct)
        => Ok(await _changeOrders.SendAsync(id, ct));

    /// <summary>Internal approval (channel Internal): contract value, stage end and schedule revision updated.</summary>
    [HttpPost("change-orders/{id:guid}/approve")]
    public async Task<ActionResult<BuildChangeOrderDto>> ApproveChangeOrder(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DecideBuildChangeOrderRequest? request, CancellationToken ct)
        => Ok(await _changeOrders.ApproveAsync(id, request?.Comment, ct));

    [HttpPost("change-orders/{id:guid}/reject")]
    public async Task<ActionResult<BuildChangeOrderDto>> RejectChangeOrder(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DecideBuildChangeOrderRequest? request, CancellationToken ct)
        => Ok(await _changeOrders.RejectAsync(id, request?.Comment, ct));

    [HttpPost("change-orders/{id:guid}/cancel")]
    public async Task<ActionResult<BuildChangeOrderDto>> CancelChangeOrder(Guid id, CancellationToken ct)
        => Ok(await _changeOrders.CancelAsync(id, ct));

    // ── Selections ────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/selections")]
    public async Task<ActionResult<IReadOnlyList<BuildSelectionDto>>> GetSelections(Guid projectId, CancellationToken ct)
        => Ok(await _selections.ListAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/selections")]
    public async Task<ActionResult<BuildSelectionDto>> CreateSelection(Guid projectId, [FromBody] SaveBuildSelectionRequest request,
        CancellationToken ct)
    {
        var dto = await _selections.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetSelection), new { id = dto.Id }, dto);
    }

    [HttpGet("selections/{id:guid}")]
    public async Task<ActionResult<BuildSelectionDto>> GetSelection(Guid id, CancellationToken ct)
        => Ok(await _selections.GetAsync(id, ct));

    [HttpPut("selections/{id:guid}")]
    public async Task<ActionResult<BuildSelectionDto>> UpdateSelection(Guid id, [FromBody] SaveBuildSelectionRequest request,
        CancellationToken ct)
        => Ok(await _selections.UpdateAsync(id, request, ct));

    [HttpPost("selections/{id:guid}/options")]
    public async Task<ActionResult<BuildSelectionDto>> AddSelectionOption(Guid id, [FromBody] SaveBuildSelectionOptionRequest request,
        CancellationToken ct)
        => Ok(await _selections.AddOptionAsync(id, request, ct));

    [HttpPut("selection-options/{optionId:guid}")]
    public async Task<ActionResult<BuildSelectionDto>> UpdateSelectionOption(Guid optionId,
        [FromBody] SaveBuildSelectionOptionRequest request, CancellationToken ct)
        => Ok(await _selections.UpdateOptionAsync(optionId, request, ct));

    [HttpDelete("selection-options/{optionId:guid}")]
    public async Task<ActionResult<BuildSelectionDto>> RemoveSelectionOption(Guid optionId, CancellationToken ct)
        => Ok(await _selections.RemoveOptionAsync(optionId, ct));

    /// <summary>Confirms the client's choice (or records one with optionId while Open). May create a draft change order.</summary>
    [HttpPost("selections/{id:guid}/confirm")]
    public async Task<ActionResult<BuildSelectionDto>> ConfirmSelection(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmBuildSelectionRequest? request, CancellationToken ct)
        => Ok(await _selections.ConfirmAsync(id, request?.OptionId, ct));

    [HttpPost("selections/{id:guid}/cancel")]
    public async Task<ActionResult<BuildSelectionDto>> CancelSelection(Guid id, CancellationToken ct)
        => Ok(await _selections.CancelAsync(id, ct));

    // ── Materials ─────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/materials")]
    public async Task<ActionResult<IReadOnlyList<BuildMaterialRequestDto>>> GetMaterials(Guid projectId,
        [FromQuery] string? status, [FromQuery] Guid? stageId, [FromQuery] bool? late, CancellationToken ct)
        => Ok(await _materials.ListAsync(projectId, status, stageId, late, ct));

    [HttpPost("projects/{projectId:guid}/materials")]
    public async Task<ActionResult<BuildMaterialRequestDto>> CreateMaterial(Guid projectId, [FromBody] SaveBuildMaterialRequest request,
        CancellationToken ct)
    {
        var dto = await _materials.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetMaterial), new { id = dto.Id }, dto);
    }

    [HttpGet("materials/{id:guid}")]
    public async Task<ActionResult<BuildMaterialRequestDto>> GetMaterial(Guid id, CancellationToken ct)
        => Ok(await _materials.GetAsync(id, ct));

    [HttpPut("materials/{id:guid}")]
    public async Task<ActionResult<BuildMaterialRequestDto>> UpdateMaterial(Guid id, [FromBody] SaveBuildMaterialRequest request,
        CancellationToken ct)
        => Ok(await _materials.UpdateAsync(id, request, ct));

    [HttpPost("materials/{id:guid}/quote")]
    public async Task<ActionResult<BuildMaterialRequestDto>> QuoteMaterial(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QuoteBuildMaterialRequest? request, CancellationToken ct)
        => Ok(await _materials.QuoteAsync(id, request ?? new QuoteBuildMaterialRequest(), ct));

    [HttpPost("materials/{id:guid}/order")]
    public async Task<ActionResult<BuildMaterialRequestDto>> OrderMaterial(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OrderBuildMaterialRequest? request, CancellationToken ct)
        => Ok(await _materials.OrderAsync(id, request ?? new OrderBuildMaterialRequest(), ct));

    /// <summary>Registers a delivery (partial or total).</summary>
    [HttpPost("materials/{id:guid}/deliveries")]
    public async Task<ActionResult<BuildMaterialRequestDto>> DeliverMaterial(Guid id, [FromBody] DeliverBuildMaterialRequest request,
        CancellationToken ct)
        => Ok(await _materials.DeliverAsync(id, request, ct));

    [HttpPost("materials/{id:guid}/cancel")]
    public async Task<ActionResult<BuildMaterialRequestDto>> CancelMaterial(Guid id, CancellationToken ct)
        => Ok(await _materials.CancelAsync(id, ct));

    /// <summary>Links (or unlinks with null) the obra's financial movement that paid the material.</summary>
    [HttpPut("materials/{id:guid}/movement")]
    public async Task<ActionResult<BuildMaterialRequestDto>> LinkMaterialMovement(Guid id,
        [FromBody] LinkBuildMaterialMovementRequest request, CancellationToken ct)
        => Ok(await _materials.LinkMovementAsync(id, request.MovementId, ct));

    // ── Measurements ──────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/measurements")]
    public async Task<ActionResult<IReadOnlyList<BuildMeasurementDto>>> GetMeasurements(Guid projectId, CancellationToken ct)
        => Ok(await _measurements.ListAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/measurements")]
    public async Task<ActionResult<BuildMeasurementDto>> CreateMeasurement(Guid projectId,
        [FromBody] SaveBuildMeasurementRequest request, CancellationToken ct)
    {
        var dto = await _measurements.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetMeasurement), new { id = dto.Id }, dto);
    }

    [HttpGet("measurements/{id:guid}")]
    public async Task<ActionResult<BuildMeasurementDto>> GetMeasurement(Guid id, CancellationToken ct)
        => Ok(await _measurements.GetAsync(id, ct));

    [HttpPut("measurements/{id:guid}")]
    public async Task<ActionResult<BuildMeasurementDto>> UpdateMeasurement(Guid id, [FromBody] SaveBuildMeasurementRequest request,
        CancellationToken ct)
        => Ok(await _measurements.UpdateAsync(id, request, ct));

    [HttpPost("measurements/{id:guid}/submit")]
    public async Task<ActionResult<BuildMeasurementDto>> SubmitMeasurement(Guid id, CancellationToken ct)
        => Ok(await _measurements.SubmitAsync(id, ct));

    /// <summary>Approves and applies the measured % to the stages (progress events with source Measurement).</summary>
    [HttpPost("measurements/{id:guid}/approve")]
    public async Task<ActionResult<BuildMeasurementDto>> ApproveMeasurement(Guid id, CancellationToken ct)
        => Ok(await _measurements.ApproveAsync(id, ct));

    [HttpPost("measurements/{id:guid}/reject")]
    public async Task<ActionResult<BuildMeasurementDto>> RejectMeasurement(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectBuildMeasurementRequest? request, CancellationToken ct)
        => Ok(await _measurements.RejectAsync(id, request?.Reason, ct));
}
