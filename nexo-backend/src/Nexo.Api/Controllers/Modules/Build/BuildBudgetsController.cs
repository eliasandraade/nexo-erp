using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Gerencia orçamentos de obra e seus itens (ORKEN BUILD).
///
/// Um orçamento pode existir sem projeto (pré-venda) ou vinculado a um projeto.
///
/// Ciclo de vida:
///   Draft → Sent → Approved → Converted (vinculado a projeto)
///   Draft | Sent → Rejected  (terminal parcial — não bloqueia criar novo)
///
/// Itens:
///   Apenas orçamentos em Draft ou Sent aceitam Add/Update/Remove de itens.
///   TotalCost e FinalPrice são recalculados automaticamente após cada mutação.
///   Margem pode ser ajustada a qualquer momento enquanto editável.
///
/// Conversão:
///   POST /{id}/convert — vincula orçamento aprovado a um projeto existente.
///   O projeto recebe automaticamente BudgetApproved = budget.FinalPrice.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[Nexo.Api.Filters.ValidateBody]
public class BuildBudgetsController : ControllerBase
{
    private readonly BuildBudgetService _budgets;
    private readonly BuildBudgetCommercialService _commercial;

    public BuildBudgetsController(BuildBudgetService budgets, BuildBudgetCommercialService commercial)
    {
        _budgets = budgets;
        _commercial = commercial;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lista orçamentos com paginação.
    /// Filtros opcionais: projectId, status (Draft | Sent | Approved | Rejected | Converted).
    /// </summary>
    [HttpGet("budgets")]
    public async Task<ActionResult<BuildPagedResult<BuildBudgetDto>>> GetAll(
        [FromQuery] Guid?   projectId = null,
        [FromQuery] string? status    = null,
        [FromQuery] int     page      = 1,
        [FromQuery] int     pageSize  = 20,
        CancellationToken   ct        = default)
    {
        BuildBudgetStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BuildBudgetStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new { error = $"Invalid status '{status}'." });
            statusEnum = parsed;
        }

        return Ok(await _budgets.GetAllAsync(projectId, statusEnum, page, pageSize, ct));
    }

    /// <summary>Retorna orçamento por ID com todos os itens.</summary>
    [HttpGet("budgets/{id:guid}")]
    public async Task<ActionResult<BuildBudgetDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _budgets.GetByIdAsync(id, ct));

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Cria um orçamento em status Draft.</summary>
    [HttpPost("budgets")]
    public async Task<ActionResult<BuildBudgetDto>> Create(
        [FromBody] CreateBuildBudgetRequest request,
        CancellationToken ct)
    {
        var dto = await _budgets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Marca orçamento como Enviado ao cliente (Draft → Sent).</summary>
    [HttpPost("budgets/{id:guid}/send")]
    public async Task<ActionResult<BuildBudgetDto>> Send(Guid id, CancellationToken ct)
        => Ok(await _budgets.MarkSentAsync(id, ct));

    /// <summary>Aprova o orçamento (Draft | Sent → Approved).</summary>
    [HttpPost("budgets/{id:guid}/approve")]
    public async Task<ActionResult<BuildBudgetDto>> Approve(Guid id, CancellationToken ct)
        => Ok(await _budgets.ApproveBudgetAsync(id, ct));

    /// <summary>Rejeita o orçamento (Draft | Sent → Rejected). Corpo opcional: { reason }.</summary>
    [HttpPost("budgets/{id:guid}/reject")]
    public async Task<ActionResult<BuildBudgetDto>> Reject(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectBuildBudgetRequest? request,
        CancellationToken ct)
        => Ok(await _budgets.RejectBudgetAsync(id, request?.Reason, ct));

    // ── Wave 2: terms, duplicate, versions, PDF, save as template ────────────

    /// <summary>Validade, observações (cliente), observações internas e área (Draft | Sent).</summary>
    [HttpPut("budgets/{id:guid}/terms")]
    public async Task<ActionResult<BuildBudgetDto>> SetTerms(Guid id, [FromBody] SetBuildBudgetTermsRequest request, CancellationToken ct)
        => Ok(await _commercial.SetTermsAsync(id, request, ct));

    /// <summary>Novo rascunho com os mesmos itens (não entra na família de versões).</summary>
    [HttpPost("budgets/{id:guid}/duplicate")]
    public async Task<ActionResult<BuildBudgetDto>> Duplicate(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DuplicateBuildBudgetRequest? request, CancellationToken ct)
    {
        var dto = await _commercial.DuplicateAsync(id, request?.Name, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Nova versão de um orçamento enviado, aprovado ou recusado (Version+1, o anterior fica intacto).</summary>
    [HttpPost("budgets/{id:guid}/versions")]
    public async Task<ActionResult<BuildBudgetDto>> NewVersion(Guid id, CancellationToken ct)
    {
        var dto = await _commercial.NewVersionAsync(id, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Todas as versões da família do orçamento.</summary>
    [HttpGet("budgets/{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<BuildBudgetVersionDto>>> Versions(Guid id, CancellationToken ct)
        => Ok(await _commercial.GetVersionsAsync(id, ct));

    /// <summary>PDF do orçamento para o cliente (preços com margem; sem custos, margem nem observações internas).</summary>
    [HttpGet("budgets/{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var pdf = await _commercial.RenderPdfAsync(id, ct);
        Response.Headers.CacheControl = "no-store";
        return File(pdf.Bytes, "application/pdf", pdf.FileName);
    }

    /// <summary>Salva os itens do orçamento como modelo (quantidade fixa ou por m²).</summary>
    [HttpPost("budgets/{id:guid}/save-as-template")]
    public async Task<ActionResult<BuildBudgetTemplateDto>> SaveAsTemplate(
        Guid id, [FromBody] SaveBudgetAsTemplateRequest request, CancellationToken ct)
        => Ok(await _commercial.SaveAsTemplateAsync(id, request, ct));

    /// <summary>
    /// Converte orçamento aprovado em projeto (Approved → Converted).
    /// Atualiza automaticamente project.BudgetApproved = budget.FinalPrice.
    /// </summary>
    [HttpPost("budgets/{id:guid}/convert")]
    public async Task<ActionResult<BuildBudgetDto>> ConvertToProject(
        Guid id,
        [FromBody] ConvertBudgetToProjectRequest request,
        CancellationToken ct)
        => Ok(await _budgets.ConvertToProjectAsync(id, request, ct));

    /// <summary>Ajusta margem de lucro do orçamento (recalcula FinalPrice).</summary>
    [HttpPut("budgets/{id:guid}/margin")]
    public async Task<ActionResult<BuildBudgetDto>> SetMargin(
        Guid id,
        [FromBody] SetBudgetMarginRequest request,
        CancellationToken ct)
        => Ok(await _budgets.SetMarginAsync(id, request, ct));

    // ── Items ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona um item ao orçamento (apenas Draft ou Sent).
    /// TotalCost do orçamento é recalculado automaticamente.
    /// </summary>
    [HttpPost("budgets/{budgetId:guid}/items")]
    public async Task<ActionResult<BuildBudgetDto>> AddItem(
        Guid budgetId,
        [FromBody] AddBuildBudgetItemRequest request,
        CancellationToken ct)
        => Ok(await _budgets.AddItemAsync(budgetId, request, ct));

    /// <summary>Atualiza um item do orçamento.</summary>
    [HttpPut("budget-items/{id:guid}")]
    public async Task<ActionResult<BuildBudgetDto>> UpdateItem(
        Guid id,
        [FromBody] UpdateBuildBudgetItemRequest request,
        CancellationToken ct)
        => Ok(await _budgets.UpdateItemAsync(id, request, ct));

    /// <summary>Remove um item do orçamento.</summary>
    [HttpDelete("budget-items/{id:guid}")]
    public async Task<ActionResult<BuildBudgetDto>> RemoveItem(Guid id, CancellationToken ct)
        => Ok(await _budgets.RemoveItemAsync(id, ct));
}
