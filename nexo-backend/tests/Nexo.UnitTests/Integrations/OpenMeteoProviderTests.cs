using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Weather;
using Nexo.Infrastructure.Integrations.Weather;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// OpenMeteoProvider com um HttpMessageHandler falso (sem rede): leitura das respostas no formato real da Open-Meteo
/// (forecast e archive), descrição WMO em português, cache por coordenada, e o que acontece com HTTP de erro, JSON
/// quebrado, campos ausentes e tempo esgotado.
/// </summary>
public class OpenMeteoProviderTests
{
    // ── Respostas no formato devolvido pela Open-Meteo ────────────────────────

    private const string ForecastJson = """
        {
          "latitude": -23.5, "longitude": -46.625, "generationtime_ms": 0.04, "utc_offset_seconds": -10800,
          "timezone": "America/Sao_Paulo", "timezone_abbreviation": "-03", "elevation": 760.0,
          "daily_units": { "time": "iso8601", "temperature_2m_max": "°C", "temperature_2m_min": "°C",
                           "precipitation_sum": "mm", "weathercode": "wmo code" },
          "daily": { "time": ["2026-11-13"], "temperature_2m_max": [29.4], "temperature_2m_min": [18.6],
                     "precipitation_sum": [12.4], "weathercode": [63] }
        }
        """;

    private const string ArchiveJson = """
        {
          "latitude": -23.5, "longitude": -46.625, "timezone": "America/Sao_Paulo",
          "daily_units": { "time": "iso8601" },
          "daily": { "time": ["2026-03-10"], "temperature_2m_max": [31.0], "temperature_2m_min": [21.2],
                     "precipitation_sum": [0.0], "weathercode": [1] }
        }
        """;

    private const string DailyForecastJson = """
        {
          "latitude": -23.5, "longitude": -46.625, "timezone": "America/Sao_Paulo",
          "daily_units": { "time": "iso8601", "wind_speed_10m_max": "km/h", "precipitation_probability_max": "%" },
          "daily": {
            "time": ["2026-11-13", "2026-11-14", "data-quebrada", "2026-11-16"],
            "temperature_2m_max": [29.4, null, 25.0, 27.1],
            "temperature_2m_min": [18.6, 17.0, 16.0],
            "precipitation_sum": [12.4, 0.0, 3.2, 0.4],
            "precipitation_probability_max": [90, 5, 40, 10],
            "wind_speed_10m_max": [22.3, 8.1, 14.0, 9.9],
            "weathercode": [63, null, 61, 7]
          }
        }
        """;

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public List<Uri> Requests { get; } = [];

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public static FakeHandler Returning(HttpStatusCode status, string body) => new((_, _) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return _respond(request, cancellationToken);
        }
    }

    private static (OpenMeteoProvider Provider, ICacheService Cache) Create(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<WeatherResult>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((WeatherResult?)null);
        cache.GetAsync<List<WeatherDailyForecast>>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((List<WeatherDailyForecast>?)null);
        var http = new HttpClient(handler);
        if (timeout is { } t) http.Timeout = t;
        var provider = new OpenMeteoProvider(http, cache, Options.Create(new OpenMeteoOptions
        {
            ForecastBaseUrl = "https://forecast.example.test/v1",
            ArchiveBaseUrl = "https://archive.example.test/v1",
            Timezone = "America/Sao_Paulo",
        }), NullLogger<OpenMeteoProvider>.Instance);
        return (provider, cache);
    }

    /// <summary>Cultura fixa durante o teste (o resumo usa a cultura atual), restaurada ao final.</summary>
    private static IDisposable Culture(string name)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        return new Restore(() => CultureInfo.CurrentCulture = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    // ── Tempo atual (forecast) ────────────────────────────────────────────────

    [Fact]
    public async Task Current_weather_is_read_from_the_forecast_endpoint_mapped_to_portuguese_and_cached_for_30_minutes()
    {
        using var _ = Culture("pt-BR");
        var handler = FakeHandler.Returning(HttpStatusCode.OK, ForecastJson);
        var (provider, cache) = Create(handler);

        var result = await provider.GetCurrentAsync(-23.55, -46.63);

        result.Should().NotBeNull();
        result!.Latitude.Should().Be(-23.55);
        result.Longitude.Should().Be(-46.63);
        result.TemperatureMax.Should().Be(29.4);
        result.TemperatureMin.Should().Be(18.6);
        result.PrecipitationMm.Should().Be(12.4);
        result.WeatherCode.Should().Be(63);
        result.Description.Should().Be("Chuva moderada");
        result.Summary.Should().Be("29°C / 19°C · Chuva moderada · Chuva: 12,4mm");
        result.Date.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));

        var url = handler.Requests.Should().ContainSingle().Subject.ToString();
        url.Should().StartWith("https://forecast.example.test/v1/forecast?");
        url.Should().Contain("daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode");
        url.Should().Contain("timezone=America%2FSao_Paulo");
        url.Should().EndWith("&forecast_days=1");

        await cache.Received(1).SetAsync(Arg.Is<string>(k => k.StartsWith("weather:current:")), Arg.Is(result),
            Arg.Is<TimeSpan?>(TimeSpan.FromMinutes(30)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Current_weather_coordinates_go_in_the_url_and_cache_key()
    {
        using var _ = Culture("en-US");
        var handler = FakeHandler.Returning(HttpStatusCode.OK, ForecastJson);
        var (provider, cache) = Create(handler);

        await provider.GetCurrentAsync(-23.5505, -46.6333);

        handler.Requests.Single().Query.Should().Contain("latitude=-23.5505&longitude=-46.6333");
        await cache.Received(1).GetAsync<WeatherResult>(Arg.Is("weather:current:-23.55:-46.63"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cached_current_weather_is_returned_without_calling_open_meteo()
    {
        var handler = FakeHandler.Returning(HttpStatusCode.OK, ForecastJson);
        var (provider, cache) = Create(handler);
        var cached = new WeatherResult(-23.55, -46.63, new DateOnly(2026, 11, 13), 30, 20, 0, 0, "Céu limpo", "do cache");
        cache.GetAsync<WeatherResult>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cached);

        var result = await provider.GetCurrentAsync(-23.55, -46.63);

        result.Should().BeSameAs(cached);
        handler.Requests.Should().BeEmpty();
        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
    }

    [Fact]
    public async Task An_unknown_wmo_code_is_described_by_its_number()
    {
        using var _ = Culture("pt-BR");
        var json = ForecastJson.Replace("\"weathercode\": [63]", "\"weathercode\": [7]");
        var (provider, _) = Create(FakeHandler.Returning(HttpStatusCode.OK, json));

        var result = await provider.GetCurrentAsync(-23.55, -46.63);

        result!.Description.Should().Be("Código climático 7");
        result.Summary.Should().Contain("· Código climático 7 ·");
    }

    [Theory]
    [InlineData("""{ "latitude": -23.5 }""")]                                                        // sem "daily"
    [InlineData("""{ "daily": null }""")]
    [InlineData("""{ "daily": { "time": [], "temperature_2m_max": [], "temperature_2m_min": [], "precipitation_sum": [], "weathercode": [] } }""")]
    [InlineData("""{ "daily": { "temperature_2m_max": [29.4], "temperature_2m_min": [18.6], "precipitation_sum": [1.0] } }""")]   // sem código
    [InlineData("""{ "daily": { "temperature_2m_max": [29.4], "precipitation_sum": [1.0], "weathercode": [3] } }""")]             // sem mínima
    [InlineData("""{ "daily": { "temperature_2m_max": [29.4], "temperature_2m_min": [18.6], "weathercode": [3] } }""")]           // sem chuva
    [InlineData("""{ "daily": { "temperature_2m_min": [18.6], "precipitation_sum": [1.0], "weathercode": [3] } }""")]             // sem máxima
    public async Task A_response_without_the_daily_values_gives_no_result_and_is_not_cached(string json)
    {
        var (provider, cache) = Create(FakeHandler.Returning(HttpStatusCode.OK, json));

        (await provider.GetCurrentAsync(-23.55, -46.63)).Should().BeNull();

        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
    }

    [Fact]
    public async Task A_literal_null_body_gives_no_result()
    {
        var (provider, cache) = Create(FakeHandler.Returning(HttpStatusCode.OK, "null"));

        (await provider.GetCurrentAsync(-23.55, -46.63)).Should().BeNull();
        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task An_http_error_from_open_meteo_is_raised_and_nothing_is_cached(HttpStatusCode status)
    {
        var (provider, cache) = Create(FakeHandler.Returning(status, """{ "error": true, "reason": "Falha" }"""));

        await provider.Invoking(p => p.GetCurrentAsync(-23.55, -46.63)).Should().ThrowAsync<HttpRequestException>();
        await provider.Invoking(p => p.GetHistoryAsync(-23.55, -46.63, new DateOnly(2026, 3, 10))).Should().ThrowAsync<HttpRequestException>();
        await provider.Invoking(p => p.GetDailyForecastAsync(-23.55, -46.63, 7)).Should().ThrowAsync<HttpRequestException>();

        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
        await cache.DidNotReceiveWithAnyArgs().SetAsync<List<WeatherDailyForecast>>(default!, default!, default, default);
    }

    [Fact]
    public async Task Malformed_json_is_raised_as_a_json_error_and_nothing_is_cached()
    {
        var (provider, cache) = Create(FakeHandler.Returning(HttpStatusCode.OK, """{ "daily": { "temperature_2m_max": [29.4, """));

        await provider.Invoking(p => p.GetCurrentAsync(-23.55, -46.63)).Should().ThrowAsync<JsonException>();
        await provider.Invoking(p => p.GetDailyForecastAsync(-23.55, -46.63, 3)).Should().ThrowAsync<JsonException>();

        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
        await cache.DidNotReceiveWithAnyArgs().SetAsync<List<WeatherDailyForecast>>(default!, default!, default, default);
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_a_json_error()
    {
        var json = ForecastJson.Replace("\"weathercode\": [63]", "\"weathercode\": [\"chuva\"]");
        var (provider, _) = Create(FakeHandler.Returning(HttpStatusCode.OK, json));

        await provider.Invoking(p => p.GetCurrentAsync(-23.55, -46.63)).Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task A_request_that_times_out_is_cancelled_and_nothing_is_cached()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (provider, cache) = Create(handler, timeout: TimeSpan.FromMilliseconds(50));

        await provider.Invoking(p => p.GetCurrentAsync(-23.55, -46.63)).Should().ThrowAsync<TaskCanceledException>();
        await provider.Invoking(p => p.GetDailyForecastAsync(-23.55, -46.63, 3)).Should().ThrowAsync<TaskCanceledException>();

        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
        await cache.DidNotReceiveWithAnyArgs().SetAsync<List<WeatherDailyForecast>>(default!, default!, default, default);
    }

    [Fact]
    public async Task The_callers_cancellation_reaches_the_request()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (provider, _) = Create(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await provider.Invoking(p => p.GetHistoryAsync(-23.55, -46.63, new DateOnly(2026, 3, 10), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Histórico (archive) ───────────────────────────────────────────────────

    [Fact]
    public async Task History_is_read_from_the_archive_for_that_single_day_and_cached_for_a_year()
    {
        using var _ = Culture("en-US");
        var handler = FakeHandler.Returning(HttpStatusCode.OK, ArchiveJson);
        var (provider, cache) = Create(handler);
        var day = new DateOnly(2026, 3, 10);

        var result = await provider.GetHistoryAsync(-23.55, -46.63, day);

        result.Should().NotBeNull();
        result!.Date.Should().Be(day);
        result.Description.Should().Be("Majoritariamente limpo");
        result.TemperatureMax.Should().Be(31.0);
        result.PrecipitationMm.Should().Be(0);
        result.Summary.Should().Be("31°C / 21°C · Majoritariamente limpo · Chuva: 0.0mm");

        var url = handler.Requests.Should().ContainSingle().Subject.ToString();
        url.Should().StartWith("https://archive.example.test/v1/archive?");
        url.Should().Contain("start_date=2026-03-10&end_date=2026-03-10");
        url.Should().Contain("timezone=America%2FSao_Paulo");
        await cache.Received(1).SetAsync(Arg.Is("weather:history:-23.55:-46.63:2026-03-10"), Arg.Is(result),
            Arg.Is<TimeSpan?>(TimeSpan.FromDays(365)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cached_history_day_is_returned_without_calling_open_meteo()
    {
        var handler = FakeHandler.Returning(HttpStatusCode.OK, ArchiveJson);
        var (provider, cache) = Create(handler);
        var cached = new WeatherResult(-23.55, -46.63, new DateOnly(2026, 3, 10), 31, 21, 0, 1, "Majoritariamente limpo", "do cache");
        cache.GetAsync<WeatherResult>(Arg.Is<string>(k => k.StartsWith("weather:history:")), Arg.Any<CancellationToken>()).Returns(cached);

        (await provider.GetHistoryAsync(-23.55, -46.63, new DateOnly(2026, 3, 10))).Should().BeSameAs(cached);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_history_day_without_data_is_null_and_not_cached()
    {
        var (provider, cache) = Create(FakeHandler.Returning(HttpStatusCode.OK, """{ "daily": { "time": ["2026-03-10"] } }"""));

        (await provider.GetHistoryAsync(-23.55, -46.63, new DateOnly(2026, 3, 10))).Should().BeNull();
        await cache.DidNotReceiveWithAnyArgs().SetAsync<WeatherResult>(default!, default!, default, default);
    }

    // ── Previsão diária ───────────────────────────────────────────────────────

    [Fact]
    public async Task Daily_forecast_keeps_missing_values_as_null_and_skips_unreadable_dates()
    {
        using var _ = Culture("pt-BR");   // a URL e a chave do cache são invariantes mesmo em pt-BR
        var handler = FakeHandler.Returning(HttpStatusCode.OK, DailyForecastJson);
        var (provider, cache) = Create(handler);

        var days = await provider.GetDailyForecastAsync(-23.5505, -46.6333, 4);

        days.Should().HaveCount(3, "the unreadable date is left out");
        days[0].Should().Be(new WeatherDailyForecast(new DateOnly(2026, 11, 13), 29.4, 18.6, 12.4, 90, 22.3, 63, "Chuva moderada"));
        days[1].Should().Be(new WeatherDailyForecast(new DateOnly(2026, 11, 14), null, 17.0, 0.0, 5, 8.1, null, null));
        // a série de mínimas é mais curta: o último dia fica sem mínima, e um código fora da tabela é descrito pelo número
        days[2].Should().Be(new WeatherDailyForecast(new DateOnly(2026, 11, 16), 27.1, null, 0.4, 10, 9.9, 7, "Código climático 7"));

        var url = handler.Requests.Should().ContainSingle().Subject.ToString();
        url.Should().StartWith("https://forecast.example.test/v1/forecast?latitude=-23.5505&longitude=-46.6333&");
        url.Should().Contain("precipitation_probability_max,wind_speed_10m_max,weathercode");
        url.Should().Contain("&wind_speed_unit=kmh");
        url.Should().EndWith("&forecast_days=4");
        await cache.Received(1).SetAsync(Arg.Is("weather:daily:-23.55:-46.63:4"), Arg.Is<List<WeatherDailyForecast>>(l => l.Count == 3),
            Arg.Is<TimeSpan?>(TimeSpan.FromHours(1)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(16, 16)]
    [InlineData(40, 16)]
    public async Task Daily_forecast_asks_between_1_and_16_days(int asked, int sent)
    {
        var handler = FakeHandler.Returning(HttpStatusCode.OK, DailyForecastJson);
        var (provider, cache) = Create(handler);

        await provider.GetDailyForecastAsync(-23.55, -46.63, asked);

        handler.Requests.Single().ToString().Should().EndWith($"&forecast_days={sent}");
        await cache.Received(1).GetAsync<List<WeatherDailyForecast>>(Arg.Is($"weather:daily:-23.55:-46.63:{sent}"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cached_daily_forecast_is_returned_without_calling_open_meteo()
    {
        var handler = FakeHandler.Returning(HttpStatusCode.OK, DailyForecastJson);
        var (provider, cache) = Create(handler);
        var cached = new List<WeatherDailyForecast> { new(new DateOnly(2026, 11, 13), 30, 20, 0, 0, 5, 0, "Céu limpo") };
        cache.GetAsync<List<WeatherDailyForecast>>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cached);

        (await provider.GetDailyForecastAsync(-23.55, -46.63, 7)).Should().BeSameAs(cached);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "latitude": -23.5 }""")]
    [InlineData("""{ "daily": { "time": [] } }""")]
    [InlineData("""{ "daily": { "time": null, "temperature_2m_max": [29.4] } }""")]
    [InlineData("""{ "daily": { "time": ["13/11/2026"], "temperature_2m_max": [29.4] } }""")]
    [InlineData("null")]
    public async Task A_daily_forecast_without_usable_days_is_empty_and_not_cached(string json)
    {
        var (provider, cache) = Create(FakeHandler.Returning(HttpStatusCode.OK, json));

        (await provider.GetDailyForecastAsync(-23.55, -46.63, 7)).Should().BeEmpty();
        await cache.DidNotReceiveWithAnyArgs().SetAsync<List<WeatherDailyForecast>>(default!, default!, default, default);
    }

    [Fact]
    public async Task Daily_forecast_without_the_optional_series_has_every_value_null()
    {
        var (provider, _) = Create(FakeHandler.Returning(HttpStatusCode.OK, """{ "daily": { "time": ["2026-11-13"] } }"""));

        var days = await provider.GetDailyForecastAsync(-23.55, -46.63, 1);

        days.Should().ContainSingle().Which.Should().Be(
            new WeatherDailyForecast(new DateOnly(2026, 11, 13), null, null, null, null, null, null, null));
    }
}
