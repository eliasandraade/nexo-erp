using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 6, company side (Management, module build): deterministic delivery forecast (methodology productivity/1;
/// needs BuildPredict, 403 build_predict_disabled), its history, weather per activity at the site and the weather sensitivity
/// of a stage. See docs/build/forecast.md and docs/build/internal-api.md (# Wave 6).
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
public class BuildForecastController : ControllerBase
{
    private readonly BuildForecastService _forecast;

    public BuildForecastController(BuildForecastService forecast) => _forecast = forecast;

    /// <summary>Today's stored forecast (latest of the day) or, when none was stored today, one computed on the fly (stored false).</summary>
    [HttpGet("projects/{projectId:guid}/forecast")]
    public async Task<ActionResult<BuildForecastDto>> Get(Guid projectId, CancellationToken ct)
        => Ok(await _forecast.GetAsync(projectId, ct));

    /// <summary>Recomputes the forecast now and stores it (source Manual); the history is kept.</summary>
    [HttpPost("projects/{projectId:guid}/forecast")]
    public async Task<ActionResult<BuildForecastDto>> Recompute(Guid projectId, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _forecast.RecomputeAsync(projectId, ct));

    /// <summary>Stored forecasts, newest first (take 1–365, default 60).</summary>
    [HttpGet("projects/{projectId:guid}/forecast/history")]
    public async Task<ActionResult<IReadOnlyList<BuildForecastHistoryItemDto>>> History(Guid projectId, [FromQuery] int? take, CancellationToken ct)
        => Ok(await _forecast.HistoryAsync(projectId, take, ct));

    /// <summary>
    /// Weather at the site — today and the next 7 days — with the risk of each day for the weather-sensitive stages in the field.
    /// 200 with available false and a reason (no_coordinates, weather_disabled, provider_unavailable) when there is no forecast.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/weather")]
    public async Task<ActionResult<BuildWeatherDto>> Weather(Guid projectId, CancellationToken ct)
        => Ok(await _forecast.WeatherAsync(projectId, ct));

    /// <summary>Sets ({ weatherSensitive: true/false }) or clears (null → keyword default) the weather sensitivity of a stage.</summary>
    [HttpPut("stages/{stageId:guid}/weather-sensitivity")]
    public async Task<ActionResult<BuildWeatherStageDto>> SetWeatherSensitivity(Guid stageId,
        [FromBody] SetBuildStageWeatherSensitivityRequest request, CancellationToken ct)
        => Ok(await _forecast.SetWeatherSensitivityAsync(stageId, request, ct));
}
