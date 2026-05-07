using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Gerencia projetos de obra (ORKEN BUILD).
///
/// Ciclo de vida:
///   Planning → InProgress (start) → Paused (pause) → InProgress (start)
///   Planning | InProgress | Paused → Completed (complete) [terminal]
///   Planning | InProgress | Paused → Cancelled (cancel)  [terminal]
///
/// Financeiro:
///   GET /{id}/financial-summary → despesas realizadas via Core (ContextType=Obra).
///   Build NUNCA cria movimentos financeiros paralelos.
/// </summary>
[ApiController]
[Route("api/v1/build/projects")]
[Authorize]
[RequireModule("build")]
public class BuildProjectsController : ControllerBase
{
    private readonly BuildProjectService         _projects;
    private readonly BuildFinancialSummaryService _financial;

    public BuildProjectsController(
        BuildProjectService          projects,
        BuildFinancialSummaryService financial)
    {
        _projects  = projects;
        _financial = financial;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lista projetos do tenant com paginação.
    /// Filtro opcional por status: Planning | InProgress | Paused | Completed | Cancelled
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BuildPagedResult<BuildProjectDto>>> GetAll(
        [FromQuery] string? status   = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        CancellationToken   ct       = default)
    {
        BuildProjectStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BuildProjectStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new { error = $"Invalid status '{status}'." });
            statusEnum = parsed;
        }

        return Ok(await _projects.GetAllAsync(statusEnum, page, pageSize, ct));
    }

    /// <summary>Retorna projeto por ID (sem stages/logs para listagens).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BuildProjectDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _projects.GetByIdAsync(id, ct));

    /// <summary>Retorna projeto completo com etapas e últimos 5 logs.</summary>
    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<BuildProjectDetailsDto>> GetDetails(Guid id, CancellationToken ct)
        => Ok(await _projects.GetDetailsAsync(id, ct));

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Cria um novo projeto no status Planning.</summary>
    [HttpPost]
    public async Task<ActionResult<BuildProjectDto>> Create(
        [FromBody] CreateBuildProjectRequest request,
        CancellationToken ct)
    {
        var dto = await _projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Atualiza dados do projeto. Não permitido em status terminal.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BuildProjectDto>> Update(
        Guid id,
        [FromBody] UpdateBuildProjectRequest request,
        CancellationToken ct)
        => Ok(await _projects.UpdateAsync(id, request, ct));

    // ── Status transitions ────────────────────────────────────────────────────

    /// <summary>Inicia o projeto (Planning | Paused → InProgress).</summary>
    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<BuildProjectDto>> Start(Guid id, CancellationToken ct)
        => Ok(await _projects.StartAsync(id, ct));

    /// <summary>Pausa o projeto (InProgress → Paused).</summary>
    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<BuildProjectDto>> Pause(Guid id, CancellationToken ct)
        => Ok(await _projects.PauseAsync(id, ct));

    /// <summary>Conclui o projeto (qualquer status ativo → Completed). Terminal.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<BuildProjectDto>> Complete(Guid id, CancellationToken ct)
        => Ok(await _projects.CompleteAsync(id, ct));

    /// <summary>Cancela o projeto (qualquer status não-terminal → Cancelled). Terminal.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BuildProjectDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _projects.CancelAsync(id, ct));

    // ── Financial summary ─────────────────────────────────────────────────────

    /// <summary>
    /// Resumo financeiro da obra.
    /// Consulta exclusivamente FinancialMovement do Core com ContextType=Obra.
    /// Build nunca cria movimentos financeiros paralelos.
    /// </summary>
    [HttpGet("{id:guid}/financial-summary")]
    public async Task<ActionResult<BuildProjectFinancialSummaryDto>> GetFinancialSummary(
        Guid id, CancellationToken ct)
        => Ok(await _financial.GetAsync(id, ct));
}
