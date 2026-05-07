using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Diário de obra (ORKEN BUILD) — registro diário de atividades e fotos.
///
/// Regras:
///   - Um registro por projeto por data (único index no DB).
///   - O endpoint de criação retorna 422 se já existir um log naquela data.
///   - Fotos armazenam apenas StorageKey — o binário é gerenciado pelo serviço
///     de blob storage separado (mesmo padrão do MovementAttachment).
///   - O update de log é aberto — não há status de aprovação.
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize]
[RequireModule("build")]
public class BuildDailyLogsController : ControllerBase
{
    private readonly BuildDailyLogService _logs;

    public BuildDailyLogsController(BuildDailyLogService logs) => _logs = logs;

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lista logs de obra de um projeto com paginação.
    /// Filtro opcional por intervalo de datas (from/to em formato ISO: yyyy-MM-dd).
    /// </summary>
    [HttpGet("projects/{projectId:guid}/daily-logs")]
    public async Task<ActionResult<BuildPagedResult<BuildDailyLogDto>>> GetByProject(
        Guid       projectId,
        [FromQuery] DateOnly? from     = null,
        [FromQuery] DateOnly? to       = null,
        [FromQuery] int       page     = 1,
        [FromQuery] int       pageSize = 20,
        CancellationToken     ct       = default)
        => Ok(await _logs.GetByProjectAsync(projectId, from, to, page, pageSize, ct));

    /// <summary>Retorna log de obra por ID com todas as fotos.</summary>
    [HttpGet("daily-logs/{id:guid}")]
    public async Task<ActionResult<BuildDailyLogDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _logs.GetByIdAsync(id, ct));

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cria um log de obra para a data especificada.
    /// Retorna 422 se já existir um log nessa data para o projeto.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/daily-logs")]
    public async Task<ActionResult<BuildDailyLogDto>> Create(
        Guid projectId,
        [FromBody] CreateDailyLogRequest request,
        CancellationToken ct)
    {
        var dto = await _logs.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Atualiza notas e resumo climático do log.</summary>
    [HttpPut("daily-logs/{id:guid}")]
    public async Task<ActionResult<BuildDailyLogDto>> Update(
        Guid id,
        [FromBody] UpdateDailyLogRequest request,
        CancellationToken ct)
        => Ok(await _logs.UpdateAsync(id, request, ct));

    // ── Photos ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona foto ao log de obra.
    /// O campo storageKey deve ter sido obtido previamente via upload para blob storage.
    /// </summary>
    [HttpPost("daily-logs/{id:guid}/photos")]
    public async Task<ActionResult<BuildDailyLogDto>> AddPhoto(
        Guid id,
        [FromBody] AddDailyLogPhotoRequest request,
        CancellationToken ct)
        => Ok(await _logs.AddPhotoAsync(id, request, ct));

    /// <summary>Remove uma foto do log de obra pelo ID da foto.</summary>
    [HttpDelete("daily-log-photos/{photoId:guid}")]
    public async Task<ActionResult<BuildDailyLogDto>> RemovePhoto(
        Guid photoId,
        CancellationToken ct)
        => Ok(await _logs.RemovePhotoAsync(photoId, ct));
}
