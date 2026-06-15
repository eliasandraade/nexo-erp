using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Integrations.Weather;

namespace Nexo.Api.Controllers.Integrations;

[ApiController]
[Route("api/integrations/weather")]
[Authorize]
public class WeatherController : ControllerBase
{
    private readonly IWeatherProvider          _provider;
    private readonly IIntegrationFeatureFlags  _flags;
    private readonly ILogger<WeatherController> _logger;

    public WeatherController(
        IWeatherProvider provider,
        IIntegrationFeatureFlags flags,
        ILogger<WeatherController> logger)
    {
        _provider = provider;
        _flags    = flags;
        _logger   = logger;
    }

    /// <summary>
    /// Get today's weather forecast for the given coordinates.
    /// Returns 404 when WeatherEnabled is false.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] double lat,
        [FromQuery] double lon,
        CancellationToken ct)
    {
        if (!_flags.WeatherEnabled)
            return NotFound(new { error = "Consulta de clima não está habilitada." });

        if (!IsValidCoordinate(lat, lon))
            return BadRequest(new { error = "Coordenadas inválidas. Latitude: -90 a 90, Longitude: -180 a 180." });

        _logger.LogInformation("[Weather] Current lookup — lat={Lat}, lon={Lon}", lat, lon);

        try
        {
            var result = await _provider.GetCurrentAsync(lat, lon, ct);

            if (result == null)
                return Ok(new { found = false, data = (object?)null });

            return Ok(new { found = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Weather] Current lookup failed — lat={Lat}, lon={Lon}", lat, lon);
            return Ok(new { found = false, data = (object?)null, unavailable = true });
        }
    }

    /// <summary>
    /// Get historical daily weather for the given coordinates and date.
    /// Returns 404 when WeatherEnabled is false.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        if (!_flags.WeatherEnabled)
            return NotFound(new { error = "Consulta de clima não está habilitada." });

        if (!IsValidCoordinate(lat, lon))
            return BadRequest(new { error = "Coordenadas inválidas. Latitude: -90 a 90, Longitude: -180 a 180." });

        if (date > DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { error = "Data deve ser no passado ou hoje." });

        _logger.LogInformation("[Weather] History lookup — lat={Lat}, lon={Lon}, date={Date}", lat, lon, date);

        try
        {
            var result = await _provider.GetHistoryAsync(lat, lon, date, ct);

            if (result == null)
                return Ok(new { found = false, data = (object?)null });

            return Ok(new { found = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Weather] History lookup failed — lat={Lat}, lon={Lon}, date={Date}", lat, lon, date);
            return Ok(new { found = false, data = (object?)null, unavailable = true });
        }
    }

    private static bool IsValidCoordinate(double lat, double lon) =>
        lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
}
