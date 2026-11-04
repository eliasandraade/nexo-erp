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
    private static readonly TimeSpan DailyCacheTtl   = TimeSpan.FromHours(1);

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

    /// <summary>
    /// Daily forecast (Open-Meteo forecast endpoint, free, no key): precipitation_sum, precipitation_probability_max,
    /// wind_speed_10m_max, temperature_2m_max/min, weathercode. Cached 1 h per rounded coordinate; the HTTP client has the
    /// integration timeout/retry/circuit breaker of the provider.
    /// </summary>
    public async Task<IReadOnlyList<WeatherDailyForecast>> GetDailyForecastAsync(double latitude, double longitude, int days,
        CancellationToken ct = default)
    {
        var n = Math.Clamp(days, 1, 16);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var cacheKey = string.Create(inv, $"weather:daily:{latitude:F2}:{longitude:F2}:{n}");
        var cached = await _cache.GetAsync<List<WeatherDailyForecast>>(cacheKey, ct);
        if (cached is not null) return cached;

        var url = string.Create(inv, $"{_opts.ForecastBaseUrl}/forecast?latitude={latitude:0.####}&longitude={longitude:0.####}")
                  + "&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,weathercode"
                  + "&wind_speed_unit=kmh"
                  + $"&timezone={Uri.EscapeDataString(_opts.Timezone)}"
                  + string.Create(inv, $"&forecast_days={n}");

        var response = await _http.GetFromJsonAsync<MeteoDailyResponse>(url, ct);
        var daily = response?.Daily;
        var list = new List<WeatherDailyForecast>();
        if (daily?.Time is { Length: > 0 } dates)
        {
            for (var i = 0; i < dates.Length; i++)
            {
                if (!DateOnly.TryParseExact(dates[i], "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out var date)) continue;
                int? code = At(daily.WmoCodes, i);
                list.Add(new WeatherDailyForecast(date, At(daily.TempMax, i), At(daily.TempMin, i), At(daily.Precip, i),
                    At(daily.PrecipProbability, i), At(daily.WindMax, i), code,
                    code is { } c ? (WmoDescriptions.TryGetValue(c, out var d) ? d : $"Código climático {c}") : null));
            }
        }
        if (list.Count > 0) await _cache.SetAsync(cacheKey, list, DailyCacheTtl, ct);
        return list;
    }

    private static T? At<T>(T?[]? values, int i) where T : struct => values is not null && i < values.Length ? values[i] : null;

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

    /// <summary>Daily forecast payload — every value nullable (Open-Meteo sends null for missing values).</summary>
    private sealed class MeteoDailyResponse
    {
        [JsonPropertyName("daily")] public MeteoDailyForecastData? Daily { get; init; }
    }

    private sealed class MeteoDailyForecastData
    {
        [JsonPropertyName("time")]                          public string[]?  Time              { get; init; }
        [JsonPropertyName("temperature_2m_max")]            public double?[]? TempMax           { get; init; }
        [JsonPropertyName("temperature_2m_min")]            public double?[]? TempMin           { get; init; }
        [JsonPropertyName("precipitation_sum")]             public double?[]? Precip            { get; init; }
        [JsonPropertyName("precipitation_probability_max")] public int?[]?    PrecipProbability { get; init; }
        [JsonPropertyName("wind_speed_10m_max")]            public double?[]? WindMax           { get; init; }
        [JsonPropertyName("weathercode")]                   public int?[]?    WmoCodes          { get; init; }
    }
}
