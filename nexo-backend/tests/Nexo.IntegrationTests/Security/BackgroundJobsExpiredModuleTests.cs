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
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Weather;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Jobs;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Provisioning;
using Nexo.IntegrationTests.Build;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Os jobs de fundo rodam sem login, por todas as empresas, e nenhum portão os alcança. Sem acesso ao módulo agora, a empresa não
/// grava dado de operação e não consome serviço pago: nem provedor de texto, nem clima, nem e-mail. Cada teste prova os dois lados:
/// a empresa vencida fica de fora e a válida, na mesma passada, segue atendida. Ver docs/engineering/tenant-access-enforcement.md.
/// </summary>
[Collection("Integration")]
public class BackgroundJobsExpiredModuleTests
{
    private readonly TestWebApplicationFactory _factory;

    public BackgroundJobsExpiredModuleTests(TestWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string> EveryExpiredState()
    {
        var data = new TheoryData<string>();
        foreach (var state in new[] { "periodo_vencido", "past_due_fora_da_carencia", "canceled", "suspended", "empresa_suspensa" }) data.Add(state);
        return data;
    }

    // ── Snapshot diário ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task The_daily_snapshot_skips_an_expired_build_module(string state)
    {
        var expired = await BuildTenantAsync();
        var valid = await BuildTenantAsync();
        await ExpireAsync(expired.Tenant, "build", state);
        var day = BusinessClock.Today().AddDays(30);   // um dia em que nenhuma alteração gravou snapshot

        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BuildProgressEngine>().EnsureDailySnapshotsAsync(day, CancellationToken.None);

        (await SnapshotsAsync(expired.ProjectId, day)).Should().Be(0);
        (await SnapshotsAsync(valid.ProjectId, day)).Should().Be(1, "a empresa válida segue tendo o ponto do dia");
    }

    // ── Previsão diária ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task The_daily_forecast_skips_an_expired_build_module_and_calls_no_weather_provider(string state)
    {
        var weather = new RecordingWeatherProvider();
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:WeatherEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IWeatherProvider>();
                s.AddSingleton<IWeatherProvider>(weather);
            });
        });
        var expiredSite = NewSite();
        var validSite = NewSite();
        var expired = await BuildTenantAsync(expiredSite);
        var valid = await BuildTenantAsync(validSite);
        await ExpireAsync(expired.Tenant, "build", state);
        var day = BusinessClock.Today().AddDays(31);

        await RunForecastsAsync(host, day);

        (await ForecastsAsync(expired.ProjectId, day)).Should().Be(0);
        weather.Asked(expiredSite).Should().BeFalse("sem acesso não se consome provedor externo");
        (await ForecastsAsync(valid.ProjectId, day)).Should().Be(1, "a empresa válida segue com a previsão do dia");
        weather.Asked(validSite).Should().BeTrue();
    }

    // ── Resumo semanal ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task The_weekly_digest_skips_an_expired_build_module(string state)
    {
        var expired = await PublishedAiTenantAsync();
        var valid = await PublishedAiTenantAsync();
        await ExpireAsync(expired.Tenant, "build", state);

        await BuildWeeklyDigestJob.RunOnceAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, CancellationToken.None);

        (await DigestsAsync(expired)).Should().Be(0);
        (await DigestsAsync(valid)).Should().Be(1, "a empresa válida segue recebendo a sugestão da semana");
    }

    // ── Fila de modelos ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task The_model_queue_leaves_an_expired_build_module_waiting_and_runs_it_when_access_returns(string state)
    {
        var expired = await BuildTenantAsync();
        await ExpireAsync(expired.Tenant, "build", state);
        Guid jobId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            // um arquivo que não existe no armazenamento: se o job fosse tomado, falharia e mudaria de estado
            var model = BuildModel.Create(expired.Tenant.TenantId, expired.ProjectId, "Modelo do job", BuildModelDiscipline.Arquitetura, expired.Tenant.UserId);
            db.BldModels.Add(model);
            var versionId = Guid.NewGuid();
            db.BldModelVersions.Add(BuildModelVersion.Stored(expired.Tenant.TenantId, expired.ProjectId, model.Id, versionId, 1, BuildModelSourceFormat.Glb,
                $"private/tenants/{expired.Tenant.TenantId}/build/models/{versionId}/ausente.glb", "ausente.glb", 10, new string('0', 64), expired.Tenant.UserId));
            var job = BuildModelProcessingJob.Enqueue(expired.Tenant.TenantId, versionId, BuildModelJobKind.Parse);
            db.BldModelJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await DrainQueueAsync();

        var waiting = await JobAsync(jobId);
        waiting.Status.Should().Be(BuildModelJobStatus.Pending);
        waiting.Attempts.Should().Be(0, "esperar pela assinatura não gasta tentativa");

        await RestoreAsync(expired.Tenant, "build", state);
        await DrainQueueAsync();
        (await JobAsync(jobId)).Attempts.Should().BePositive("com o acesso de volta o job sai da fila");
    }

    // ── E-mails do Service ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task Service_emails_stay_pending_on_an_expired_module_and_go_out_when_access_returns(string state)
    {
        var (tenant, email, appointmentId) = await BookingWithEmailAsync();
        await ExpireAsync(tenant, "service", state);
        var sender = new CapturingEmail();

        await RunEmailsAsync(sender);

        sender.Sent.Should().NotContain(email, "sem acesso o provedor de e-mail não é consumido");
        (await PendingConfirmationAsync(appointmentId)).Should().BeTrue("nada foi reivindicado: o e-mail fica pendente");

        await RestoreAsync(tenant, "service", state);
        await RunEmailsAsync(sender);
        sender.Sent.Should().Contain(email);
        (await PendingConfirmationAsync(appointmentId)).Should().BeFalse();
    }

    // ── Títulos vencidos ──────────────────────────────────────────────────────

    [Fact]
    public async Task Overdue_marking_leaves_out_a_company_with_no_module_with_access_and_catches_up_later()
    {
        var readOnly = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var active = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var frozen = await PastDueTitleAsync(readOnly);
        var kept = await PastDueTitleAsync(active);
        await ExpireAsync(readOnly, "varejo", "canceled");

        await MarkOverdueAsync();

        (await TitleStatusAsync(frozen)).Should().Be(TransactionStatus.Pending);
        (await TitleStatusAsync(kept)).Should().Be(TransactionStatus.Overdue);

        await RestoreAsync(readOnly, "varejo", "canceled");
        await MarkOverdueAsync();
        (await TitleStatusAsync(frozen)).Should().Be(TransactionStatus.Overdue, "a próxima passada recupera o atraso");
    }

    [Fact]
    public async Task Overdue_marking_still_runs_for_a_company_with_another_module_active()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "varejo", "build");
        var title = await PastDueTitleAsync(tenant);
        await ExpireAsync(tenant, "varejo", "canceled");

        await MarkOverdueAsync();

        (await TitleStatusAsync(title)).Should().Be(TransactionStatus.Overdue, "os títulos são do núcleo: a empresa segue operando pelo outro módulo");
    }

    [Fact]
    public async Task Overdue_marking_lists_a_past_due_title_of_a_read_only_company_as_overdue_all_the_same()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "varejo");
        await PastDueTitleAsync(tenant);
        var client = await LoginAsync(tenant);
        await ExpireAsync(tenant, "varejo", "canceled");
        await InvalidateTenantCacheAsync(tenant);

        await MarkOverdueAsync();

        var list = await client.GetAsync("/api/financial/receivables?status=overdue");
        var raw = await list.Content.ReadAsStringAsync();
        list.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().Contain("\"Overdue\"", "a leitura calcula o atraso pela data, com ou sem o job");
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private sealed record BuildTenant(SyntheticAccount Tenant, HttpClient Admin, Guid ProjectId);

    /// <summary>Guarda de onde pediram o clima: as outras obras do banco também passam por este job.</summary>
    private sealed class RecordingWeatherProvider : IWeatherProvider
    {
        private readonly BuildForecastTests.FakeWeatherProvider _inner = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(double, double), byte> _asked = new();

        public bool Asked((decimal Latitude, decimal Longitude) site) => _asked.ContainsKey(((double)site.Latitude, (double)site.Longitude));

        private void Record(double latitude, double longitude) => _asked[(latitude, longitude)] = 1;

        public Task<WeatherResult?> GetCurrentAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            Record(latitude, longitude);
            return _inner.GetCurrentAsync(latitude, longitude, ct);
        }

        public Task<WeatherResult?> GetHistoryAsync(double latitude, double longitude, DateOnly date, CancellationToken ct = default)
        {
            Record(latitude, longitude);
            return _inner.GetHistoryAsync(latitude, longitude, date, ct);
        }

        public Task<IReadOnlyList<WeatherDailyForecast>> GetDailyForecastAsync(double latitude, double longitude, int days, CancellationToken ct = default)
        {
            Record(latitude, longitude);
            return _inner.GetDailyForecastAsync(latitude, longitude, days, ct);
        }
    }

    private static (decimal Latitude, decimal Longitude) NewSite()
        => (-20m - Random.Shared.Next(1, 90_000) / 10_000m, -40m - Random.Shared.Next(1, 90_000) / 10_000m);

    private sealed class CapturingEmail : IEmailService
    {
        public readonly HashSet<string> Sent = [];
        public bool CanDeliver => true;
        public Task SendVerificationEmailAsync(string toEmail, string toName, string verificationUrl, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EmailSendResult> SendAsync(string toEmail, string subject, string html, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add(toEmail);
            return Task.FromResult(EmailSendResult.Sent);
        }
    }

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).AddDays(offset).ToString("yyyy-MM-dd");
    private static string NewSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private async Task<HttpClient> LoginAsync(SyntheticAccount account)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await call;
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    /// <summary>Tira o acesso sem apagar a linha. "empresa_suspensa" mantém o módulo válido e suspende a empresa.</summary>
    private async Task ExpireAsync(SyntheticAccount tenant, string module, string state)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        if (state == "empresa_suspensa")
        {
            (await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId)).SetStatus(TenantStatus.Suspended);
            await db.SaveChangesAsync();
            return;
        }
        var row = await db.ModuleSubscriptions.SingleAsync(s => s.TenantId == tenant.TenantId && s.ModuleKey == module);
        var yesterday = DateTime.UtcNow.AddDays(-1);
        switch (state)
        {
            case "periodo_vencido":
                db.Entry(row).Property(x => x.CurrentPeriodEnd).CurrentValue = yesterday;
                break;
            case "past_due_fora_da_carencia":
                row.MarkPastDue(DateTime.UtcNow.AddDays(-(ModuleSubscription.PastDueGraceDays + 1)));
                break;
            case "canceled":
                row.Cancel();
                break;
            case "suspended":
                row.SyncFromStripe(SubscriptionStatus.Suspended, yesterday, DateTime.UtcNow.AddDays(20), false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
        await db.SaveChangesAsync();
    }

    private async Task RestoreAsync(SyntheticAccount tenant, string module, string state)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        if (state == "empresa_suspensa")
        {
            (await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId)).SetStatus(TenantStatus.Active);
        }
        else
        {
            var row = await db.ModuleSubscriptions.SingleAsync(s => s.TenantId == tenant.TenantId && s.ModuleKey == module);
            db.Entry(row).Property(x => x.Status).CurrentValue = SubscriptionStatus.Active;
            db.Entry(row).Property(x => x.CurrentPeriodEnd).CurrentValue = null;
            db.Entry(row).Property(x => x.PastDueSince).CurrentValue = null;
        }
        await db.SaveChangesAsync();
    }

    private async Task InvalidateTenantCacheAsync(SyntheticAccount tenant)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        await cache.RemoveAsync($"tenant:{tenant.TenantId}:info");
        await cache.RemoveAsync($"tenant:{tenant.TenantId}:modules");
    }

    // Build

    private async Task<BuildTenant> BuildTenantAsync((decimal Latitude, decimal Longitude)? site = null)
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "build");
        var admin = await LoginAsync(tenant);
        var created = await Ok(admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra do job", clientName = "Cliente", type = "House", startDate = Day(-30), expectedEndDate = Day(200),
            latitude = site?.Latitude, longitude = site?.Longitude,
        }), HttpStatusCode.Created);
        var projectId = created.GetProperty("id").GetGuid();
        await Ok(admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name = "Fundação", plannedStart = Day(-5), plannedEnd = Day(30), weight = 1m,
        }), HttpStatusCode.Created);
        return new BuildTenant(tenant, admin, projectId);
    }

    private async Task<int> SnapshotsAsync(Guid projectId, DateOnly day)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().BldProgressSnapshots.IgnoreQueryFilters()
            .CountAsync(s => s.ProjectId == projectId && s.Date == day);
    }

    private static async Task RunForecastsAsync(WebApplicationFactory<Program> host, DateOnly day)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BuildForecastService>().EnsureDailyForecastsAsync(day, CancellationToken.None);
    }

    private async Task<int> ForecastsAsync(Guid projectId, DateOnly day)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().BldForecasts.IgnoreQueryFilters()
            .CountAsync(f => f.ProjectId == projectId && f.Date == day);
    }

    private async Task<BuildTenant> PublishedAiTenantAsync()
    {
        var built = await BuildTenantAsync();
        var company = NewSlug("constr");
        await Ok(built.Admin.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug = company, displayName = "Construtora", contactEmail = "contato@" + TestCredentials.TestDomain, contactPhone = "11 4000-0000",
        }));
        await Ok(built.Admin.PutAsJsonAsync($"/api/v1/build/projects/{built.ProjectId}/portal", new { slug = NewSlug("obra"), access = "Public", title = "Sua obra" }));
        await Ok(built.Admin.PostAsync($"/api/v1/build/projects/{built.ProjectId}/portal/publish", null));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var settings = await db.BldTenantSettings.FirstOrDefaultAsync(s => s.TenantId == built.Tenant.TenantId);
        if (settings is null)
        {
            settings = BuildTenantSettings.Create(built.Tenant.TenantId);
            db.BldTenantSettings.Add(settings);
        }
        db.Entry(settings).Property(x => x.BuildAiEnabled).CurrentValue = true;
        await db.SaveChangesAsync();
        return built;
    }

    private async Task<int> DigestsAsync(BuildTenant built)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().BldAiSuggestions.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == built.Tenant.TenantId && s.ProjectId == built.ProjectId && s.Capability == BuildAiCapability.WeeklyDigest);
    }

    private async Task DrainQueueAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None)) return;
        }
    }

    private async Task<BuildModelProcessingJob> JobAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().BldModelJobs.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.Id == id);
    }

    // Service

    private async Task<(SyntheticAccount Tenant, string Email, Guid AppointmentId)> BookingWithEmailAsync()
    {
        const string allWeek =
            "[{\"weekday\":0,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]},{\"weekday\":1,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
            "{\"weekday\":2,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]},{\"weekday\":3,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
            "{\"weekday\":4,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]},{\"weekday\":5,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
            "{\"weekday\":6,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}]";
        var tenant = await SyntheticTenant.CreateAsync(_factory, "service");
        var admin = await LoginAsync(tenant);
        var slug = NewSlug("svc");
        await Ok(admin.PatchAsJsonAsync($"/api/stores/{tenant.StoreId}/public-slug", new { publicSlug = slug }));
        await Ok(admin.PutAsJsonAsync("/api/v1/service/settings/preset", new { presetKey = "salao-beleza" }));
        await Ok(admin.PutAsJsonAsync("/api/v1/service/settings/public-booking", new
        {
            publicBookingEnabled = true, bookingDaysAhead = 14, minLeadMinutes = 30, slotIntervalMinutes = 60,
            showPrices = true, autoConfirmAppointments = false, timeZoneId = "UTC",
        }));
        await Ok(admin.PutAsJsonAsync("/api/v1/service/settings/customer-emails", new { customerEmailsEnabled = true, reminderHoursBefore = 24 }));
        var professional = (await Ok(admin.PostAsJsonAsync("/api/v1/service/professionals", new { name = "Pro " + Guid.NewGuid().ToString("N")[..6], workingHoursJson = allWeek }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var catalog = (await Ok(admin.PostAsJsonAsync("/api/v1/service/catalog", new { name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price = 80m }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var pub = AuthClientFactory.CreateAnonymousClient(_factory);
        var availability = await pub.GetFromJsonAsync<JsonElement>($"/api/public/service/{slug}/availability?catalogItemId={catalog}&professionalId={professional}");
        var slot = availability.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetString()!)
            .First(s => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind) > DateTime.UtcNow.AddDays(3));
        var email = $"cliente{Guid.NewGuid():N}@teste.dev";
        var created = await Ok(pub.PostAsJsonAsync($"/api/public/service/{slug}/appointments", new
        {
            customerName = "Cliente", phone = "1197" + Random.Shared.Next(1_000_000, 9_999_999), email, catalogItemId = catalog, professionalId = professional, startsAt = slot,
        }), HttpStatusCode.Created);
        return (tenant, email, created.GetProperty("appointmentId").GetGuid());
    }

    private async Task RunEmailsAsync(CapturingEmail sender)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        for (var i = 0; i < 20; i++)
            if (await ServiceCustomerEmailsJob.RunOnceAsync(db, sender, "https://app.test", DateTime.UtcNow, NullLogger.Instance, default) == 0 && i > 0)
                break;
    }

    private async Task<bool> PendingConfirmationAsync(Guid appointmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().SvcAppointments.IgnoreQueryFilters()
            .Where(a => a.Id == appointmentId).Select(a => a.ConfirmationEmailPending).SingleAsync();
    }

    // Títulos

    private async Task<Guid> PastDueTitleAsync(SyntheticAccount tenant)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await scope.ServiceProvider.GetRequiredService<DefaultFinancialAccountProvisioner>().EnsureAsync(tenant.TenantId);
        await db.SaveChangesAsync();
        var account = await db.FinancialAccounts.IgnoreQueryFilters().FirstAsync(a => a.TenantId == tenant.TenantId);
        var title = FinancialTransaction.Create(tenant.TenantId, account.Id, TransactionType.Receivable, FinancialCategory.OtherIncome,
            100m, "Título do job", DateTime.UtcNow.AddDays(-10), tenant.UserId, storeId: tenant.StoreId);
        db.FinancialTransactions.Add(title);
        await db.SaveChangesAsync();
        return title.Id;
    }

    private async Task MarkOverdueAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await OverdueTitlesJob.MarkOverdueAsync(scope.ServiceProvider.GetRequiredService<NexoDbContext>(), BusinessClock.TodayStartUtc());
    }

    private async Task<TransactionStatus> TitleStatusAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.Id == id).Select(t => t.Status).SingleAsync();
    }
}
