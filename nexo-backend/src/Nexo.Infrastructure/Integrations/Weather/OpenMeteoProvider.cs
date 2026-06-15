using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Weather;

namespace Nexo.Infrastructure.Integrations.Weather;

public sealed class OpenMeteoProvider : IWeatherProvider
{
    private static readonly TimeSpan CurrentCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan HistoryCacheTtl = TimeSpan.FromDays(365);

    private static readonly Dictionary<int, string> WmoDescriptions = new()
    {
        [0]  = "Céu limpo",
        [1]  = "Majoritariamente limpo",
        [2]  = "Parcialmente nublado",
        [3]  = "Nublado",
        [45] = "Neblina",
        [48] = "Neblina com geada",
        [51] = "Garoa fraca",
        [53] = "Garoa moderada",
        [55] = "Garoa intensa",
        [61] = "Chuva fraca",
        [63] = "Chuva moderada",
        [65] = "Chuva forte",
        [71] = "Neve fraca",
        [73] = "Neve moderada",
        [75] = "Neve forte",
        [80] = "Pancadas de chuva fracas",
        [81] = "Pancadas de chuva moderadas",
        [82] = "Pancadas de chuva fortes",
        [95] = "Trovoada",
        [96] = "Trovoada com granizo fraco",
        [99] = "Trovoada com granizo forte",
    };

    private readonly HttpClient              _http;
    private readonly ICacheService           _cache;
    private readonly OpenMeteoOptions        _opts;
    private readonly ILogger<OpenMeteoProvider> _logger;

    public OpenMeteoProvider(
        HttpClient http,
        ICacheService cache,
        IOptions<OpenMeteoOptions> opts,
        ILogger<OpenMeteoProvider> logger)
    {
        _http   = http;
        _cache  = cache;
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task<WeatherResult?> GetCurrentAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var cacheKey = $"weather:current:{latitude:F2}:{longitude:F2}";

        var cached = await _cache.GetAsync<WeatherResult>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("[OpenMeteo] Cache hit for current weather at {Lat},{Lon}", latitude, longitude);
            return cached;
        }

        var url = $"{_opts.ForecastBaseUrl}/forecast" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode" +
                  $"&timezone={Uri.EscapeDataString(_opts.Timezone)}" +
                  $"&forecast_days=1";

        _logger.LogDebug("[OpenMeteo] Fetching current forecast from {Url}", url);

        var response = await _http.GetFromJsonAsync<MeteoResponse>(url, ct);
        var result   = MapToResult(latitude, longitude, DateOnly.FromDateTime(DateTime.UtcNow), response);

        if (result is not null)
            await _cache.SetAsync(cacheKey, result, CurrentCacheTtl, ct);

        return result;
    }

    public async Task<WeatherResult?> GetHistoryAsync(double latitude, double longitude, DateOnly date, CancellationToken ct = default)
    {
        var dateStr  = date.ToString("yyyy-MM-dd");
        var cacheKey = $"weather:history:{latitude:F2}:{longitude:F2}:{dateStr}";

        var cached = await _cache.GetAsync<WeatherResult>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("[OpenMeteo] Cache hit for history weather at {Lat},{Lon} on {Date}", latitude, longitude, dateStr);
            return cached;
        }

        var url = $"{_opts.ArchiveBaseUrl}/archive" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&start_date={dateStr}&end_date={dateStr}" +
                  $"&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode" +
                  $"&timezone={Uri.EscapeDataString(_opts.Timezone)}";

        _logger.LogDebug("[OpenMeteo] Fetching archive from {Url}", url);

        var response = await _http.GetFromJsonAsync<MeteoResponse>(url, ct);
        var result   = MapToResult(latitude, longitude, date, response);

        if (result is not null)
            await _cache.SetAsync(cacheKey, result, HistoryCacheTtl, ct);

        return result;
    }

    private static WeatherResult? MapToResult(double latitude, double longitude, DateOnly date, MeteoResponse? response)
    {
        var daily = response?.Daily;
        if (daily is null)
            return null;

        if (daily.TempMax is not { Length: > 0 } ||
            daily.TempMin is not { Length: > 0 } ||
            daily.Precip  is not { Length: > 0 } ||
            daily.WmoCodes is not { Length: > 0 })
            return null;

        var tempMax     = daily.TempMax[0];
        var tempMin     = daily.TempMin[0];
        var precip      = daily.Precip[0];
        var code        = daily.WmoCodes[0];
        var description = WmoDescriptions.TryGetValue(code, out var desc) ? desc : $"Código climático {code}";
        var summary     = $"{tempMax:F0}°C / {tempMin:F0}°C · {description} · Chuva: {precip:F1}mm";

        return new WeatherResult(
            Latitude:        latitude,
            Longitude:       longitude,
            Date:            date,
            TemperatureMax:  tempMax,
            TemperatureMin:  tempMin,
            PrecipitationMm: precip,
            WeatherCode:     code,
            Description:     description,
            Summary:         summary
        );
    }

    // ── Internal DTOs ──────────────────────────────────────────────────────────
    private sealed class MeteoResponse
    {
        [JsonPropertyName("daily")] public MeteoDailyData? Daily { get; init; }
    }

    private sealed class MeteoDailyData
    {
        [JsonPropertyName("temperature_2m_max")]  public double[]? TempMax  { get; init; }
        [JsonPropertyName("temperature_2m_min")]  public double[]? TempMin  { get; init; }
        [JsonPropertyName("precipitation_sum")]   public double[]? Precip   { get; init; }
        [JsonPropertyName("weathercode")]         public int[]?    WmoCodes { get; init; }
    }
}
