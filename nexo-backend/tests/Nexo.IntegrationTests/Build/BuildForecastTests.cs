using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Weather;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Orken Build Wave 6, end to end on a real PostgreSQL: productivity forecast (on the fly, stored, history, daily job, flag),
/// weather per activity with a fake provider (and without coordinates), the portfolio dashboard with a bounded number of SQL
/// commands, platform observability (policies, MFA for the retry, audit), the source file URL (company only, never in the
/// portal), the elements externalId/manual-zone filters, manual zone state without a model and tenant isolation.
/// </summary>
[Collection("Integration")]
public class BuildForecastTests
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly InMemoryStorageProvider Storage = new();

    public BuildForecastTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateOnly Today => BusinessClock.Today();
    private static string Day(int offset) => Today.AddDays(offset).ToString("yyyy-MM-dd");
    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private static async Task<JsonElement> Json(HttpResponseMessage r, params HttpStatusCode[] expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        expected.Should().Contain(r.StatusCode, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Code(HttpResponseMessage r, HttpStatusCode expected)
        => (await Json(r, expected)).GetProperty("code").GetString()!;

    private static HttpClient As(WebApplicationFactory<Program> host, HttpClient authed)
    {
        var c = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Authorization = authed.DefaultRequestHeaders.Authorization;
        return c;
    }

    private async Task<(HttpClient Client, Guid TenantId)> NewTenantAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 12)] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = $"Construtora {prefix}", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            tenantId = user.TenantId;
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(user.TenantId, "build"));
            await db.SaveChangesAsync();
        }
        return (await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword), tenantId);
    }

    private static async Task<Guid> ProjectAsync(HttpClient c, string name, string? expectedEnd = null, decimal? lat = null, decimal? lon = null,
        decimal? budget = null)
        => (await Json(await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Previsão", type = "House", startDate = Day(-60), expectedEndDate = expectedEnd ?? Day(120),
            latitude = lat, longitude = lon, budgetEstimated = budget,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name, string start, string end)
        => (await Json(await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new { name, plannedStart = start, plannedEnd = end, weight = 1m }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task ProgressAsync(HttpClient c, Guid stageId, int percent, int dayOffset)
        => await Json(await c.PutAsJsonAsync($"/api/v1/build/stages/{stageId}/progress", new { progressPercent = percent, effectiveDate = Day(dayOffset) }),
            HttpStatusCode.OK);

    private WebApplicationFactory<Program> WeatherHost(FakeWeatherProvider weather)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:WeatherEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IWeatherProvider>();
                s.AddSingleton<IWeatherProvider>(weather);
            });
        });

    private WebApplicationFactory<Program> StorageHost()
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(Storage);
            });
        });

    private static async Task DrainAsync(WebApplicationFactory<Program> host)
    {
        for (var i = 0; i < 50; i++)
        {
            using var scope = host.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None)) return;
        }
    }

    private static MultipartFormDataContent File(byte[] bytes, string fileName, params (string Name, string Value)[] fields)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        foreach (var (n, v) in fields) content.Add(new StringContent(v), n);
        return content;
    }

    private static byte[] Ifc()
    {
        using var s = typeof(BuildForecastTests).Assembly.GetManifestResourceStream("casa-teste.ifc")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Gid(string prefix) => prefix.PadRight(22, '0');

    /// <summary>A fake Open-Meteo: fixed days from today, counting calls.</summary>
    public sealed class FakeWeatherProvider : IWeatherProvider
    {
        public int DailyCalls;
        public Func<DateOnly, IReadOnlyList<WeatherDailyForecast>> Days { get; set; } = today => Enumerable.Range(0, 8)
            .Select(i => new WeatherDailyForecast(today.AddDays(i), 28, 19, i == 2 ? 15 : 0.5, i == 2 ? 85 : 10, i == 3 ? 55 : 12, i == 2 ? 65 : 1,
                i == 2 ? "Chuva forte" : "Céu limpo"))
            .ToList();

        public Task<WeatherResult?> GetCurrentAsync(double latitude, double longitude, CancellationToken ct = default)
            => Task.FromResult<WeatherResult?>(new WeatherResult(latitude, longitude, BusinessClock.Today(), 28, 19, 0.5, 1, "Céu limpo", "28/19"));

        public Task<WeatherResult?> GetHistoryAsync(double latitude, double longitude, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<WeatherResult?>(null);

        public Task<IReadOnlyList<WeatherDailyForecast>> GetDailyForecastAsync(double latitude, double longitude, int days, CancellationToken ct = default)
        {
            Interlocked.Increment(ref DailyCalls);
            return Task.FromResult(Days(BusinessClock.Today()));
        }
    }

    // ── Forecast ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Forecast_uses_recent_productivity_is_stored_with_history_and_drives_the_progress_forecast()
    {
        var (admin, tenantId) = await NewTenantAsync("prev");
        var projectId = await ProjectAsync(admin, "Obra previsão", expectedEnd: Day(40));
        var estrutura = await StageAsync(admin, projectId, "Estrutura", Day(-30), Day(20));
        var alvenaria = await StageAsync(admin, projectId, "Alvenaria", Day(21), Day(45));
        await ProgressAsync(admin, estrutura, 15, -14);
        await ProgressAsync(admin, estrutura, 30, -7);
        await ProgressAsync(admin, estrutura, 45, -1);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/change-orders", new
        {
            title = "Laje extra", costImpact = 1000m, scheduleImpactDays = 12, stageId = alvenaria,
        }), HttpStatusCode.Created, HttpStatusCode.OK);

        // computed on the fly (nothing stored today yet)
        var live = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast"), HttpStatusCode.OK);
        live.GetProperty("stored").GetBoolean().Should().BeFalse();
        live.GetProperty("source").GetString().Should().Be("OnTheFly");
        live.GetProperty("methodology").GetString().Should().Be("productivity/1");
        live.GetProperty("confidence").GetDecimal().Should().BeInRange(0m, 1m);
        var stages = live.GetProperty("stages").EnumerateArray().ToList();
        var est = stages.Single(s => s.GetProperty("stageId").GetGuid() == estrutura);
        est.GetProperty("method").GetString().Should().Be("window");
        est.GetProperty("dataPoints").GetInt32().Should().Be(3);
        est.GetProperty("ratePerWorkingDay").GetDecimal().Should().BeGreaterThan(0m);
        var alv = stages.Single(s => s.GetProperty("stageId").GetGuid() == alvenaria);
        alv.GetProperty("method").GetString().Should().Be("planned");
        var delivery = live.GetProperty("forecastDelivery").GetString();
        delivery.Should().NotBeNullOrEmpty();
        live.GetProperty("expectedEnd").GetString().Should().Be(Day(40));
        live.GetProperty("calendar").GetProperty("workingDays").EnumerateArray().Select(d => d.GetString())
            .Should().Contain("Saturday").And.NotContain("Sunday");
        var factors = live.GetProperty("factors").EnumerateArray().ToList();
        factors.Should().Contain(f => f.GetProperty("kind").GetString() == "pending_change_order"
                                      && f.GetProperty("days").GetInt32() == 12
                                      && f.GetProperty("evidence").GetString()!.Contains("não aplicado"));
        var finishDay = DateOnly.Parse(est.GetProperty("forecastEnd").GetString()!);
        finishDay.DayOfWeek.Should().NotBe(DayOfWeek.Sunday);

        // the Wave 1 progress uses the productivity forecast (BuildPredict on by default)
        var progress = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK);
        progress.GetProperty("methodology").GetString().Should().Be("weighted-stages/1+productivity/1");
        progress.GetProperty("forecastDelivery").GetString().Should().Be(delivery);

        // recompute and store; history kept
        var stored = await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/forecast", null), HttpStatusCode.Created);
        stored.GetProperty("stored").GetBoolean().Should().BeTrue();
        stored.GetProperty("source").GetString().Should().Be("Manual");
        stored.GetProperty("forecastDelivery").GetString().Should().Be(delivery);
        var again = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast"), HttpStatusCode.OK);
        again.GetProperty("id").GetGuid().Should().Be(stored.GetProperty("id").GetGuid());
        again.GetProperty("stages").GetArrayLength().Should().Be(2);
        again.GetProperty("factors").EnumerateArray().Should().Contain(f => f.GetProperty("kind").GetString() == "pending_change_order");
        await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/forecast", null), HttpStatusCode.Created);
        var history = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast/history"), HttpStatusCode.OK);
        history.GetArrayLength().Should().Be(2);
        history[0].GetProperty("source").GetString().Should().Be("Manual");

        // the daily job stores a Scheduled forecast only for projects without one today
        var second = await ProjectAsync(admin, "Obra do job");
        await StageAsync(admin, second, "Fundação", Day(-5), Day(30));
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BuildForecastService>().EnsureDailyForecastsAsync(Today, CancellationToken.None);
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{second}/forecast/history"), HttpStatusCode.OK))
            .EnumerateArray().Should().ContainSingle(h => h.GetProperty("source").GetString() == "Scheduled");
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast/history"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(2, "a forecast already existed today");

        // BuildPredict off: 403 on the forecast, the progress falls back to weighted-stages/1
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/build-settings", new { buildPredictEnabled = false }), HttpStatusCode.OK);
        (await Code(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast"), HttpStatusCode.Forbidden)).Should().Be("build_predict_disabled");
        (await Code(await admin.PostAsync($"/api/v1/build/projects/{projectId}/forecast", null), HttpStatusCode.Forbidden)).Should().Be("build_predict_disabled");
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK))
            .GetProperty("methodology").GetString().Should().Be("weighted-stages/1");
        await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/forecast/history"), HttpStatusCode.OK);

        // another company sees none of it
        var (other, _) = await NewTenantAsync("prevx");
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/forecast")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/v1/build/projects/{projectId}/forecast", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/forecast/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/weather")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/weather-sensitivity", new { weatherSensitive = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Weather ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Weather_shows_the_week_with_risks_for_sensitive_stages_in_the_field_and_feeds_the_forecast()
    {
        var weather = new FakeWeatherProvider();
        await using var host = WeatherHost(weather);
        var (admin, _) = await NewTenantAsync("clima");
        var api = As(host, admin);
        var projectId = await ProjectAsync(api, "Obra clima", lat: -23.55m, lon: -46.63m);
        var laje = await StageAsync(api, projectId, "Concretagem da laje", Day(-5), Day(20));
        var pintura = await StageAsync(api, projectId, "Pintura interna", Day(-5), Day(20));
        await StageAsync(api, projectId, "Cobertura", Day(60), Day(80));
        await ProgressAsync(api, laje, 10, -2);
        await ProgressAsync(api, pintura, 10, -2);

        var w = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/weather"), HttpStatusCode.OK);
        w.GetProperty("available").GetBoolean().Should().BeTrue();
        w.GetProperty("source").GetString().Should().Be("Open-Meteo");
        w.GetProperty("disclaimer").GetString().Should().Contain("não certeza");
        w.GetProperty("today").GetProperty("date").GetString().Should().Be(Day(0));
        var days = w.GetProperty("days").EnumerateArray().ToList();
        days.Should().HaveCount(7);
        var rainy = days.Single(d => d.GetProperty("date").GetString() == Day(2));
        rainy.GetProperty("level").GetString().Should().Be("high");
        rainy.GetProperty("precipitationProbability").GetInt32().Should().Be(85);
        var risks = rainy.GetProperty("risks").EnumerateArray().ToList();
        risks.Should().ContainSingle();
        risks[0].GetProperty("stageId").GetGuid().Should().Be(laje);
        risks[0].GetProperty("kind").GetString().Should().Be("heavy_rain");
        risks[0].GetProperty("message").GetString().Should().StartWith("Chuva forte prevista (15 mm, probabilidade 85%)").And.Contain("Previsão");
        days.Single(d => d.GetProperty("date").GetString() == Day(3)).GetProperty("risks").EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("kind").GetString() == "strong_wind" && r.GetProperty("level").GetString() == "high");
        var stageInfo = w.GetProperty("stages").EnumerateArray().ToList();
        stageInfo.Single(s => s.GetProperty("stageId").GetGuid() == laje).GetProperty("basis").GetString().Should().Be("keyword");
        stageInfo.Single(s => s.GetProperty("name").GetString() == "Cobertura").GetProperty("sensitive").GetBoolean().Should().BeTrue();

        // explicit sensitivity
        var set = await Json(await api.PutAsJsonAsync($"/api/v1/build/stages/{pintura}/weather-sensitivity", new { weatherSensitive = true }),
            HttpStatusCode.OK);
        set.GetProperty("basis").GetString().Should().Be("explicit");
        var after = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/weather"), HttpStatusCode.OK);
        after.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == Day(2))
            .GetProperty("risks").GetArrayLength().Should().Be(2);
        (await Json(await api.PutAsJsonAsync($"/api/v1/build/stages/{pintura}/weather-sensitivity", new { weatherSensitive = (bool?)null }),
            HttpStatusCode.OK)).GetProperty("sensitive").GetBoolean().Should().BeFalse();

        // a weather factor in the forecast, without moving dates
        var f = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/forecast"), HttpStatusCode.OK);
        f.GetProperty("factors").EnumerateArray().Should().Contain(x => x.GetProperty("kind").GetString() == "weather_risk"
                                                                       && x.GetProperty("stageId").GetGuid() == laje);

        // no coordinates → 200 with a reason, provider not called
        var calls = weather.DailyCalls;
        var nowhere = await ProjectAsync(api, "Obra sem local");
        var none = await Json(await api.GetAsync($"/api/v1/build/projects/{nowhere}/weather"), HttpStatusCode.OK);
        none.GetProperty("available").GetBoolean().Should().BeFalse();
        none.GetProperty("reason").GetString().Should().Be("no_coordinates");
        none.GetProperty("days").GetArrayLength().Should().Be(0);
        weather.DailyCalls.Should().Be(calls);

        // weather integration off (base host) → 200 with a reason
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/weather"), HttpStatusCode.OK))
            .GetProperty("reason").GetString().Should().Be("weather_disabled");

        // provider down → 200 provider_unavailable
        weather.Days = _ => throw new HttpRequestException("down");
        (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/weather"), HttpStatusCode.OK))
            .GetProperty("reason").GetString().Should().Be("provider_unavailable");
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_portfolio_numbers_for_two_projects_with_a_bounded_number_of_sql_commands()
    {
        var (admin, tenantId) = await NewTenantAsync("painel");
        var p1 = await ProjectAsync(admin, "A — Residencial", expectedEnd: Day(10), budget: 100_000m);
        var s1 = await StageAsync(admin, p1, "Fundação", Day(-30), Day(5));
        await ProgressAsync(admin, s1, 20, -10);
        await ProgressAsync(admin, s1, 30, -1);
        var p2 = await ProjectAsync(admin, "B — Comercial", expectedEnd: Day(200), budget: 50_000m);
        var s2 = await StageAsync(admin, p2, "Estrutura", Day(-10), Day(30));
        await ProgressAsync(admin, s2, 25, -2);

        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{p1}/non-conformities", new { title = "Armadura exposta", severity = "Critical", stageId = s1 }),
            HttpStatusCode.Created, HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{p1}/non-conformities", new { title = "Prumo", severity = "Low" }),
            HttpStatusCode.Created, HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{p2}/change-orders", new { title = "Mezanino", costImpact = 5000m, scheduleImpactDays = 3, stageId = s2 }),
            HttpStatusCode.Created, HttpStatusCode.OK);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId).Select(u => u.Id).FirstAsync();
            var m = FinancialMovement.CreateDraft(tenantId, user, MovementDirection.Out, MovementNature.Expense, 40_000m, Today, "Concreto",
                "concreto", FinancialContextType.Obra, p1, null, null);
            m.Confirm();
            db.IntMovements.Add(m);
            db.BldMaterialRequests.Add(BuildMaterialRequest.Create(tenantId, p2, null, "Aço CA-50", "kg", 500m, Today.AddDays(-3), null, user, "usuário"));
            await db.SaveChangesAsync();
        }

        async Task<(JsonElement Body, int Sql)> Dashboard()
        {
            var marker = Guid.NewGuid().ToString("N");
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/build/dashboard");
            request.Headers.Add(SqlCommandCounter.Header, marker);
            var body = await Json(await admin.SendAsync(request), HttpStatusCode.OK);
            return (body, _factory.Services.GetRequiredService<SqlCommandCounter>().CountFor(marker));
        }

        var (d, sqlTwo) = await Dashboard();
        d.GetProperty("totalProjects").GetInt32().Should().Be(2, "the legacy fields stay");
        d.GetProperty("totalRealized").GetDecimal().Should().Be(40_000m);
        var projects = d.GetProperty("projects").EnumerateArray().ToList();
        projects.Should().HaveCount(2);
        var a = projects.Single(p => p.GetProperty("projectId").GetGuid() == p1);
        a.GetProperty("actualPercent").GetDecimal().Should().Be(30m);
        a.GetProperty("plannedPercent").GetDecimal().Should().BeGreaterThan(30m);
        a.GetProperty("costRealized").GetDecimal().Should().Be(40_000m);
        a.GetProperty("contractTotal").GetDecimal().Should().Be(100_000m);
        a.GetProperty("cpiLike").GetDecimal().Should().Be(0.75m, "30% of 100 000 earned against 40 000 realized");
        a.GetProperty("openNonConformities").GetProperty("Critical").GetInt32().Should().Be(1);
        a.GetProperty("openNonConformities").GetProperty("Low").GetInt32().Should().Be(1);
        a.GetProperty("daysLate").GetInt32().Should().BeGreaterThan(0, "30% done with the contract ending in 10 days");
        a.GetProperty("forecastMethodology").GetString().Should().Be("weighted-stages/1+productivity/1");
        a.GetProperty("portalPublished").GetBoolean().Should().BeFalse();
        var b = projects.Single(p => p.GetProperty("projectId").GetGuid() == p2);
        b.GetProperty("pendingChangeOrders").GetInt32().Should().Be(1);
        b.GetProperty("lateMaterials").GetInt32().Should().Be(1);
        b.GetProperty("costRealized").GetDecimal().Should().Be(0m);
        b.GetProperty("cpiLike").ValueKind.Should().Be(JsonValueKind.Null);
        var totals = d.GetProperty("totals");
        totals.GetProperty("activeProjects").GetInt32().Should().Be(2);
        totals.GetProperty("openCriticalNonConformities").GetInt32().Should().Be(1);
        totals.GetProperty("contractTotal").GetDecimal().Should().Be(150_000m);
        totals.GetProperty("lateProjects").GetInt32().Should().Be(1);
        var top = d.GetProperty("topRisks").EnumerateArray().ToList();
        top.Should().Contain(r => r.GetProperty("kind").GetString() == "critical_nonconformity" && r.GetProperty("severity").GetString() == "high");
        top.Should().Contain(r => r.GetProperty("kind").GetString() == "cost_performance" && r.GetProperty("projectId").GetGuid() == p1);
        top[0].GetProperty("severity").GetString().Should().Be("high");

        // a third project with data adds no SQL command (no N+1)
        var p3 = await ProjectAsync(admin, "C — Galpão", budget: 10_000m);
        var s3 = await StageAsync(admin, p3, "Terraplenagem", Day(-3), Day(30));
        await ProgressAsync(admin, s3, 10, -1);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{p3}/non-conformities", new { title = "Talude", severity = "High" }),
            HttpStatusCode.Created, HttpStatusCode.OK);
        var (d3, sqlThree) = await Dashboard();
        d3.GetProperty("projects").GetArrayLength().Should().Be(3);
        sqlTwo.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(30);
        sqlThree.Should().Be(sqlTwo, "the number of SQL commands does not depend on the number of projects");
    }

    // ── Platform observability ────────────────────────────────────────────────

    [Fact]
    public async Task Platform_build_overview_and_jobs_need_a_platform_token_and_the_retry_needs_an_admin_with_mfa()
    {
        var (admin, tenantId) = await NewTenantAsync("plat");
        var projectId = await ProjectAsync(admin, "Obra plataforma");
        Guid failedJobId, versionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId).Select(u => u.Id).FirstAsync();
            var model = BuildModel.Create(tenantId, projectId, "Arquitetura", BuildModelDiscipline.Arquitetura, user);
            versionId = Guid.NewGuid();
            var version = BuildModelVersion.Stored(tenantId, projectId, model.Id, versionId, 1, BuildModelSourceFormat.Ifc,
                $"private/tenants/{tenantId}/build/models/{model.Id}/{versionId}/source.ifc", "Casa do Cliente.ifc", 2048, new string('a', 64), user);
            version.MarkFailed("Arquivo ilegível.");
            var job = BuildModelProcessingJob.Enqueue(tenantId, versionId, BuildModelJobKind.Parse);
            job.Fail($"Falha ao ler private/tenants/{tenantId}/build/models/{model.Id}/{versionId}/source.ifc (Casa do Cliente.ifc): esquema inválido", permanent: true);
            db.BldModels.Add(model);
            db.BldModelVersions.Add(version);
            db.BldModelJobs.Add(job);
            await db.SaveChangesAsync();
            failedJobId = job.Id;
        }

        // tenant token / anonymous are refused
        (await admin.GetAsync("/api/platform/build/overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/platform/build/jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/platform/build/jobs/{failedJobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateApiClient().GetAsync("/api/platform/build/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var overview = await Json(await platform.GetAsync("/api/platform/build/overview"), HttpStatusCode.OK);
        var mine = overview.GetProperty("tenants").EnumerateArray().Single(t => t.GetProperty("tenantId").GetGuid() == tenantId);
        mine.GetProperty("projectsTotal").GetInt32().Should().Be(1);
        mine.GetProperty("projectsActive").GetInt32().Should().Be(1);
        mine.GetProperty("jobsFailed").GetInt32().Should().Be(1);
        mine.GetProperty("modelVersionsByStatus").GetProperty("Failed").GetInt32().Should().Be(1);
        mine.GetProperty("storage").GetProperty("modelsBytes").GetInt64().Should().Be(2048);
        mine.GetProperty("storage").GetProperty("photosBytes").ValueKind.Should().Be(JsonValueKind.Null);
        mine.GetProperty("flags").GetProperty("buildPredict").GetBoolean().Should().BeTrue();
        mine.GetProperty("moduleStatus").GetString().Should().NotBeNullOrEmpty();
        var lastError = mine.GetProperty("lastJobError").GetString()!;
        lastError.Should().Contain("esquema inválido").And.NotContain("private/").And.NotContain(".ifc").And.NotContain("Cliente.ifc");

        var jobs = await Json(await platform.GetAsync("/api/platform/build/jobs?status=Failed"), HttpStatusCode.OK);
        var listed = jobs.EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == failedJobId);
        listed.GetProperty("error").GetString().Should().NotContain("private/");
        listed.ToString().Should().NotContain("Casa do Cliente");

        // MFA required for admin acts → a password-only sign-in may read but not retry
        await using (var mfaHost = _factory.WithWebHostBuilder(b => b.UseSetting("Platform:RequireMfaForAdmin", "true")))
        {
            var c = mfaHost.CreateClient();
            var login = await c.PostAsJsonAsync("/api/platform/auth/login", new { email = TestCredentials.PlatformEmail, password = TestCredentials.PlatformPassword });
            var token = (await Json(login, HttpStatusCode.OK)).GetProperty("accessToken").GetString();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await c.GetAsync("/api/platform/build/overview")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await c.PostAsync($"/api/platform/build/jobs/{failedJobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // retry (admin; MFA not required in this host): new pending job, version queued again, audited
        var retried = await Json(await platform.PostAsync($"/api/platform/build/jobs/{failedJobId}/retry", null), HttpStatusCode.OK);
        retried.GetProperty("status").GetString().Should().Be("Pending");
        retried.GetProperty("kind").GetString().Should().Be("Parse");
        var newJobId = retried.GetProperty("id").GetGuid();
        (await Code(await platform.PostAsync($"/api/platform/build/jobs/{failedJobId}/retry", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("job_already_queued");
        (await Code(await platform.PostAsync($"/api/platform/build/jobs/{newJobId}/retry", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("job_not_failed");
        (await platform.PostAsync($"/api/platform/build/jobs/{Guid.NewGuid()}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.BldModelVersions.IgnoreQueryFilters().SingleAsync(v => v.Id == versionId)).Status.Should().Be(BuildModelVersionStatus.Queued);
            var audit = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.ActionType == "build_model_job_retried" && a.EntityId == newJobId.ToString()).ToListAsync();
            audit.Should().ContainSingle();
            audit[0].ActorType.Should().Be("platform");
            audit[0].TenantId.Should().Be(tenantId);
            // leave nothing in the shared queue for the other tests' workers
            await db.BldModelJobs.IgnoreQueryFilters().Where(j => j.VersionId == versionId).ExecuteDeleteAsync();
        }
    }

    // ── Twin gaps: source URL, element filters, manual zone state ─────────────

    [Fact]
    public async Task Source_url_is_for_the_company_only_audited_and_never_in_the_portal()
    {
        var (admin, _) = await NewTenantAsync("fonte");
        await using var host = StorageHost();
        var api = As(host, admin);
        var projectId = await ProjectAsync(api, "Obra fonte");
        var pdf = "%PDF-1.4\n%ãÏÓ\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
        var upload = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models",
            File(pdf, "Planta Cliente Silva.pdf", ("name", "Planta"), ("discipline", "Arquitetura"))), HttpStatusCode.Created);
        var versionId = upload.GetProperty("version").GetProperty("id").GetGuid();

        var source = await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/source"), HttpStatusCode.OK);
        var url = source.GetProperty("url").GetString()!;
        url.Should().Contain("source.pdf").And.Contain("signature");
        source.GetProperty("fileName").GetString().Should().Be("Planta Cliente Silva.pdf");
        source.GetProperty("sourceFormat").GetString().Should().Be("Pdf");
        DateTimeOffset.Parse(source.GetProperty("expiresAt").GetString()!).Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(a => a.ActionType == "build_model_source_read" && a.EntityId == versionId.ToString())).Should().Be(1);
        }

        // storage off → 503; another company → 404
        (await Code(await admin.GetAsync($"/api/v1/build/models/versions/{versionId}/source"), HttpStatusCode.ServiceUnavailable))
            .Should().Be("storage_unavailable");
        var (other, _) = await NewTenantAsync("fontex");
        (await As(host, other).GetAsync($"/api/v1/build/models/versions/{versionId}/source")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // the portal (twin on) never carries the source file
        var company = Slug("fonte");
        await Json(await api.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug = company, displayName = "Construtora Fonte", contactEmail = "contato@" + TestCredentials.TestDomain,
        }), HttpStatusCode.OK);
        var slug = Slug("obra");
        await Json(await api.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug, access = "Public", title = "Obra", visibility = new { model3d = true },
        }), HttpStatusCode.OK);
        await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null), HttpStatusCode.OK);
        var anon = host.CreateClient();
        foreach (var path in new[] { "", "/twin", "/twin/state" })
        {
            var body = await (await anon.GetAsync($"/api/public/build/{company}/{slug}{path}")).Content.ReadAsStringAsync();
            body.Should().NotContain("source.pdf").And.NotContain("Planta Cliente Silva").And.NotContain("private/");
        }
    }

    [Fact]
    public async Task Elements_filter_by_external_id_and_by_manual_zone_and_manual_zones_have_a_state_without_a_model()
    {
        var (admin, _) = await NewTenantAsync("zonas");
        await using var host = StorageHost();
        var api = As(host, admin);
        var projectId = await ProjectAsync(api, "Obra zonas");
        var alvenaria = await StageAsync(api, projectId, "Alvenaria", Day(-30), Day(30));
        var upload = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Ifc(), "casa.ifc")), HttpStatusCode.Created);
        var versionId = upload.GetProperty("version").GetProperty("id").GetGuid();
        await DrainAsync(host);

        var two = await Json(await api.GetAsync(
            $"/api/v1/build/models/versions/{versionId}/elements?externalId={Gid("WallT1")},{Gid("WallT2")},nao-existe"), HttpStatusCode.OK);
        two.GetProperty("total").GetInt32().Should().Be(2);
        two.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("externalId").GetString())
            .Should().BeEquivalentTo([Gid("WallT1"), Gid("WallT2")]);
        var tooMany = string.Join(",", Enumerable.Range(0, 201).Select(i => $"id{i}"));
        (await Code(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements?externalId={tooMany}"), HttpStatusCode.UnprocessableEntity))
            .Should().Be("too_many_ids");

        // manual zones: accepted by the element filter (none of the model's elements belong to them) and painted by zone bindings
        var bloco = (await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/zones", new { kind = "Area", name = "Bloco A" }),
            HttpStatusCode.OK, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var sala = (await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/zones", new { kind = "Room", name = "Sala", parentZoneId = bloco }),
            HttpStatusCode.OK, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var solta = (await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/zones", new { kind = "Area", name = "Garagem" }),
            HttpStatusCode.OK, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements?zoneId={bloco}"), HttpStatusCode.OK))
            .GetProperty("total").GetInt32().Should().Be(0);
        (await Code(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements?zoneId={Guid.NewGuid()}"), HttpStatusCode.UnprocessableEntity))
            .Should().Be("invalid_zone");

        await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/bindings", new { stageId = alvenaria, scope = "Zone", zoneId = bloco }),
            HttpStatusCode.OK, HttpStatusCode.Created);
        await ProgressAsync(api, alvenaria, 50, -3);

        var now = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/twin/zones/state?date={Day(0)}"), HttpStatusCode.OK);
        var zones = now.GetProperty("zones").EnumerateArray().ToList();
        zones.Should().HaveCount(3, "only the manual zones of the project");
        zones.Single(z => z.GetProperty("zoneId").GetGuid() == bloco).GetProperty("state").GetString().Should().Be("InProgress");
        var salaState = zones.Single(z => z.GetProperty("zoneId").GetGuid() == sala);
        salaState.GetProperty("state").GetString().Should().Be("InProgress", "inherits the nearest bound ancestor");
        salaState.GetProperty("stageName").GetString().Should().Be("Alvenaria");
        zones.Single(z => z.GetProperty("zoneId").GetGuid() == solta).GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);
        now.GetProperty("counts").GetProperty("Unbound").GetInt32().Should().Be(1);
        var before = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/twin/zones/state?date={Day(-10)}"), HttpStatusCode.OK);
        before.GetProperty("zones").EnumerateArray().Single(z => z.GetProperty("zoneId").GetGuid() == bloco)
            .GetProperty("state").GetString().Should().Be("NotStarted");

        var (other, _) = await NewTenantAsync("zonasx");
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/twin/zones/state")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(host, other).GetAsync($"/api/v1/build/models/versions/{versionId}/elements?externalId={Gid("WallT1")}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
