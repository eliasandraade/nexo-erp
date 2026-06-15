namespace Nexo.Application.Integrations.Weather;

public interface IWeatherProvider
{
    /// <summary>Gets today's forecast for the given coordinates.</summary>
    Task<WeatherResult?> GetCurrentAsync(double latitude, double longitude, CancellationToken ct = default);

    /// <summary>Gets historical daily weather for the given coordinates and date.</summary>
    Task<WeatherResult?> GetHistoryAsync(double latitude, double longitude, DateOnly date, CancellationToken ct = default);
}
