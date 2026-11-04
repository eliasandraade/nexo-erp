using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Progress of a project (weighted stages — the overall % is never typed), schedule baseline/revisions,
/// "how was the project on day X", and cost per stage on the single ledger (FinancialMovement.StageId).
/// </summary>
[ApiController]
[Route("api/v1/build/projects/{projectId:guid}")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[Nexo.Api.Filters.ValidateBody]
public class BuildProgressController : ControllerBase
{
    private readonly BuildProgressService _progress;
    private readonly BuildCostService _costs;

    public BuildProgressController(BuildProgressService progress, BuildCostService costs)
    {
        _progress = progress;
        _costs = costs;
    }

    /// <summary>Planned / actual / forecast overall %, forecast delivery, per-stage figures and weekly curves.</summary>
    [HttpGet("progress")]
    public async Task<ActionResult<BuildProgressDto>> Get(Guid projectId, CancellationToken ct)
        => Ok(await _progress.GetAsync(projectId, ct));

    /// <summary>The latest daily snapshot on or before <paramref name="date"/> (yyyy-MM-dd). 404 when there is none.</summary>
    [HttpGet("progress/at")]
    public async Task<ActionResult<BuildProgressSnapshotDto>> GetAt(Guid projectId, [FromQuery] DateOnly date, CancellationToken ct)
        => Ok(await _progress.GetAtAsync(projectId, date, ct));

    /// <summary>Freezes the current plan: the first time as the baseline, then as a revision (reason required).</summary>
    [HttpPost("schedule/baseline")]
    public async Task<ActionResult<BuildScheduleRevisionDto>> Freeze(
        Guid projectId, [FromBody] CreateScheduleBaselineRequest? request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _progress.FreezeScheduleAsync(projectId, request?.Reason, ct));

    [HttpGet("schedule/revisions")]
    public async Task<ActionResult<IReadOnlyList<BuildScheduleRevisionDto>>> Revisions(Guid projectId, CancellationToken ct)
        => Ok(await _progress.GetRevisionsAsync(projectId, ct));

    /// <summary>Budgeted (approved budget items) vs realized (confirmed outgoing movements) per stage, plus "Sem etapa".</summary>
    [HttpGet("cost-by-stage")]
    public async Task<ActionResult<BuildCostByStageDto>> CostByStage(Guid projectId, CancellationToken ct)
        => Ok(await _costs.GetCostByStageAsync(projectId, ct));

    /// <summary>Tags (or untags, with stageId null) a cost of this project with one of its stages.</summary>
    [HttpPut("movements/{movementId:guid}/stage")]
    public async Task<ActionResult<BuildMovementStageDto>> AssignStage(
        Guid projectId, Guid movementId, [FromBody] AssignMovementStageRequest request, CancellationToken ct)
        => Ok(await _costs.AssignStageAsync(projectId, movementId, request.StageId, ct));
}
