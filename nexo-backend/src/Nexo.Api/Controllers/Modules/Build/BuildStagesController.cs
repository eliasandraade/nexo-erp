using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Gerencia etapas (fases) de um projeto de obra.
///
/// Etapas são ordenadas explicitamente (campo Order).
/// A rota de reorder recebe a nova ordem de todas as etapas de uma vez.
///
/// Progressão:
///   0–99% = em andamento
///   100%  = auto-completa (Pending | InProgress → Completed)
///   Status pode ser forçado via campo opcional na request de progress update.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize]
[RequireModule("build")]
public class BuildStagesController : ControllerBase
{
    private readonly BuildStageService _stages;

    public BuildStagesController(BuildStageService stages) => _stages = stages;

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Lista todas as etapas de um projeto, ordenadas por Order.</summary>
    [HttpGet("projects/{projectId:guid}/stages")]
    public async Task<ActionResult<IReadOnlyList<BuildStageDto>>> GetByProject(
        Guid projectId, CancellationToken ct)
        => Ok(await _stages.GetByProjectAsync(projectId, ct));

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona uma etapa ao projeto.
    /// Order é atribuído automaticamente (max + 1).
    /// </summary>
    [HttpPost("projects/{projectId:guid}/stages")]
    public async Task<ActionResult<BuildStageDto>> Create(
        Guid projectId,
        [FromBody] CreateBuildStageRequest request,
        CancellationToken ct)
    {
        var dto = await _stages.CreateAsync(projectId, request, ct);
        return CreatedAtAction(
            actionName:        nameof(GetByProject),
            controllerName:    "BuildStages",
            routeValues:       new { projectId = dto.ProjectId },
            value:             dto);
    }

    /// <summary>
    /// Atualiza progresso (0-100%) de uma etapa.
    /// 100% auto-completa a etapa.
    /// Campo status é opcional — permite override explícito do status.
    /// </summary>
    [HttpPut("stages/{id:guid}/progress")]
    public async Task<ActionResult<BuildStageDto>> UpdateProgress(
        Guid id,
        [FromBody] UpdateBuildStageProgressRequest request,
        CancellationToken ct)
        => Ok(await _stages.UpdateProgressAsync(id, request, ct));

    /// <summary>
    /// Reordena etapas de um projeto.
    /// Enviar a lista completa de {stageId, order} para o projeto.
    /// </summary>
    [HttpPut("projects/{projectId:guid}/stages/reorder")]
    public async Task<IActionResult> Reorder(
        Guid projectId,
        [FromBody] ReorderBuildStagesRequest request,
        CancellationToken ct)
    {
        await _stages.ReorderAsync(projectId, request, ct);
        return NoContent();
    }

    /// <summary>Remove uma etapa. Não permitido em projetos terminais.</summary>
    [HttpDelete("stages/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _stages.DeleteAsync(id, ct);
        return NoContent();
    }
}
