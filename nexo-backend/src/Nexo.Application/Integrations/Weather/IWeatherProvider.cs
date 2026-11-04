namespace Nexo.Application.Integrations.Weather;

public interface IWeatherProvider
{
    /// <summary>Gets today's forecast for the given coordinates.</summary>
    Task<WeatherResult?> GetCurrentAsync(double latitude, double longitude, CancellationToken ct = default);

    /// <summary>Gets historical daily weather for the given coordinates and date.</summary>
    Task<WeatherResult?> GetHistoryAsync(double latitude, double longitude, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Daily forecast from today for <paramref name="days"/> days (1–16): precipitation sum and max probability, max wind,
    /// max/min temperature. Empty when the provider answered without data.
    /// </summary>
    Task<IReadOnlyList<WeatherDailyForecast>> GetDailyForecastAsync(double latitude, double longitude, int days, CancellationToken ct = default);
}

/// <summary>One forecast day (values may be missing when the provider does not have them).</summary>
public sealed record WeatherDailyForecast(
    DateOnly Date,
    double?  TemperatureMax,
    double?  TemperatureMin,
    double?  PrecipitationMm,
    int?     PrecipitationProbabilityMax,
    double?  WindSpeedMaxKmh,
    int?     WeatherCode,
    string?  Description);
