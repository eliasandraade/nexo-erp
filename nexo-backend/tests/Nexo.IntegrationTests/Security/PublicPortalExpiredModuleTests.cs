using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Weather;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Build;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Os portais públicos respondem a quem não tem login, então nenhum portão de módulo os alcança: cada portal confere sozinho se a
/// empresa tem acesso agora. Sem acesso, o portal continua legível e não recebe escrita. Ver docs/engineering/tenant-access-enforcement.md.
/// </summary>
[Collection("Integration")]
public class PublicPortalExpiredModuleTests
{
    private const string ReadOnlyCode = "portal_read_only";
    private const string SessionHeader = "X-Build-Portal-Session";

    private static readonly string[] ExpiredStates = ["trial_vencido", "periodo_vencido", "past_due_fora_da_carencia", "canceled", "suspended"];

    private const string AllWeek =
        "[{\"weekday\":0,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":1,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":2,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":3,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":4,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":5,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}," +
        "{\"weekday\":6,\"windows\":[{\"start\":\"06:00\",\"end\":\"22:00\"}]}]";

    /// <summary>
    /// Toda escrita anônima sob api/public/, com o que a protege. Uma rota nova não entra sem decidir aqui: ou confere o acesso,
    /// ou diz por que não escreve dado de operação.
    /// </summary>
    private static readonly Dictionary<string, string> PublicWrites = new()
    {
        ["POST api/public/service/{slug}/appointments"] = "confere o módulo",
        ["POST api/public/service/{slug}/appointments/{token}/cancel"] = "confere o módulo",
        ["POST api/public/orders"] = "confere o módulo",
        ["POST api/public/coupons/validate"] = "só leitura: confere um cupom e não grava nada",
        ["POST api/public/build/{companySlug}/{projectSlug}/session"] = "abre a sessão que dá acesso à leitura de um portal com senha",
        ["DELETE api/public/build/{companySlug}/{projectSlug}/session"] = "encerra a sessão do próprio visitante",
        ["POST api/public/build/{companySlug}/{projectSlug}/change-orders/{publicId:guid}/decision"] = "confere o módulo",
        ["POST api/public/build/{companySlug}/{projectSlug}/selections/{publicId:guid}/choice"] = "confere o módulo",
        ["POST api/public/build/{companySlug}/{projectSlug}/handover/accept"] = "confere o módulo",
        ["POST api/public/build/{companySlug}/{projectSlug}/assistance"] = "confere o módulo",
        ["POST api/public/build/{companySlug}/{projectSlug}/assistance/{publicId:guid}/photos"] = "confere o módulo",
        ["POST api/public/build/{companySlug}/{projectSlug}/assistance/{publicId:guid}/feedback"] = "confere o módulo",
    };

    private readonly TestWebApplicationFactory _factory;

    public PublicPortalExpiredModuleTests(TestWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string> EveryExpiredState()
    {
        var data = new TheoryData<string>();
        foreach (var state in ExpiredStates) data.Add(state);
        return data;
    }

    // ── A lista de rotas ──────────────────────────────────────────────────────

    [Fact]
    public void Every_anonymous_public_write_was_decided_here()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        var found = (
            from endpoint in source.Endpoints.OfType<RouteEndpoint>()
            where endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            let template = (endpoint.RoutePattern.RawText ?? "").TrimStart('/')
            where template.StartsWith("api/public/", StringComparison.OrdinalIgnoreCase)
            from method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []
            where method is not ("GET" or "HEAD" or "OPTIONS")
            select $"{method} {template}").ToList();

        found.Should().BeEquivalentTo(PublicWrites.Keys, "uma escrita pública nova precisa decidir o que faz com módulo vencido");
    }

    // ── Orken Service ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task An_expired_service_module_refuses_a_booking_and_writes_nothing(string state)
    {
        var portal = await ServicePortalAsync();
        var slot = await SlotAsync(portal);
        var phone = NewPhone();
        await ExpireAsync(portal.Tenant, "service", state);

        using var response = await BookAsync(portal, slot, phone);

        await ExpectReadOnlyAsync(response);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == portal.Tenant.TenantId && c.Phone == phone)).Should().Be(0);
        (await db.SvcAppointments.IgnoreQueryFilters().CountAsync(a => a.TenantId == portal.Tenant.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task An_expired_service_module_keeps_the_portal_readable_and_says_booking_is_off()
    {
        var portal = await ServicePortalAsync();
        var token = await BookedTokenAsync(portal);
        await ExpireAsync(portal.Tenant, "service", "canceled");
        var pub = Anonymous();

        var header = await pub.GetFromJsonAsync<JsonElement>($"/api/public/service/{portal.Slug}");
        header.GetProperty("isBookingEnabled").GetBoolean().Should().BeFalse("quem visita não pode ver a oferta de um agendamento que será recusado");
        await ExpectOkAsync(pub.GetAsync($"/api/public/service/{portal.Slug}/catalog"));
        await ExpectOkAsync(pub.GetAsync($"/api/public/service/{portal.Slug}/professionals"));
        await ExpectOkAsync(pub.GetAsync($"/api/public/service/{portal.Slug}/availability?catalogItemId={portal.Catalog}&professionalId={portal.Professional}"));

        var view = await pub.GetFromJsonAsync<JsonElement>($"/api/public/service/{portal.Slug}/appointments/{token}");
        view.GetProperty("status").GetString().Should().Be("Scheduled");
        view.GetProperty("canCancel").GetBoolean().Should().BeFalse();
        view.GetProperty("onlineChangesPaused").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_expired_service_module_refuses_a_cancellation_and_the_booking_stays()
    {
        var portal = await ServicePortalAsync();
        var token = await BookedTokenAsync(portal);
        await ExpireAsync(portal.Tenant, "service", "canceled");

        using var response = await Anonymous().PostAsync($"/api/public/service/{portal.Slug}/appointments/{token}/cancel", null);

        await ExpectReadOnlyAsync(response);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcAppointments.IgnoreQueryFilters().SingleAsync(a => a.TenantId == portal.Tenant.TenantId))
            .Status.Should().Be(SvcAppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task The_refusal_does_not_tell_the_visitor_about_the_subscription_and_logs_no_personal_data()
    {
        var portal = await ServicePortalAsync();
        var slot = await SlotAsync(portal);
        var phone = NewPhone();
        await ExpireAsync(portal.Tenant, "service", "canceled");

        using var response = await BookAsync(portal, slot, phone);

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainEquivalentOf("assinatura").And.NotContainEquivalentOf("subscription").And.NotContain("402");
        var requestId = response.Headers.GetValues("X-Request-Id").Single();
        foreach (var text in new[] { _factory.ServerLog.For(requestId), _factory.ServerLog.PropertiesFor(requestId) })
            text.Should().NotContain(phone);
    }

    [Fact]
    public async Task A_valid_service_module_keeps_booking_and_cancelling()
    {
        var portal = await ServicePortalAsync();
        var token = await BookedTokenAsync(portal);
        var pub = Anonymous();

        var view = await pub.GetFromJsonAsync<JsonElement>($"/api/public/service/{portal.Slug}/appointments/{token}");
        view.GetProperty("canCancel").GetBoolean().Should().BeTrue();
        view.GetProperty("onlineChangesPaused").GetBoolean().Should().BeFalse();
        await ExpectOkAsync(pub.PostAsync($"/api/public/service/{portal.Slug}/appointments/{token}/cancel", null));
    }

    [Fact]
    public async Task Another_active_module_does_not_open_the_service_portal()
    {
        var portal = await ServicePortalAsync("service", "build");
        var slot = await SlotAsync(portal);
        await ExpireAsync(portal.Tenant, "service", "canceled");

        using var response = await BookAsync(portal, slot, NewPhone());

        await ExpectReadOnlyAsync(response);
    }

    [Fact]
    public async Task Booking_comes_back_when_the_module_is_active_again()
    {
        var portal = await ServicePortalAsync();
        var slot = await SlotAsync(portal);
        await ExpireAsync(portal.Tenant, "service", "canceled");
        await ExpectReadOnlyAsync(await BookAsync(portal, slot, NewPhone()));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var row = await db.ModuleSubscriptions.SingleAsync(s => s.TenantId == portal.Tenant.TenantId && s.ModuleKey == "service");
            db.Entry(row).Property(x => x.Status).CurrentValue = SubscriptionStatus.Active;
            await db.SaveChangesAsync();
        }

        using var response = await BookAsync(portal, slot, NewPhone());
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_suspended_company_takes_no_public_booking_even_with_a_valid_module()
    {
        var portal = await ServicePortalAsync();
        var slot = await SlotAsync(portal);
        await SuspendAsync(portal.Tenant);

        using var response = await BookAsync(portal, slot, NewPhone());

        await ExpectReadOnlyAsync(response);
    }

    [Fact]
    public async Task An_inactive_store_takes_no_public_booking()
    {
        var portal = await ServicePortalAsync();
        var slot = await SlotAsync(portal);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.Stores.IgnoreQueryFilters().SingleAsync(s => s.Id == portal.Tenant.StoreId)).Deactivate();
            await db.SaveChangesAsync();
        }

        using var response = await BookAsync(portal, slot, NewPhone());

        await ExpectReadOnlyAsync(response);
    }

    // ── Orken Build ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task An_expired_build_module_refuses_a_change_order_decision_and_changes_nothing(string state)
    {
        var portal = await BuildPortalAsync();
        var (id, publicId) = await SentChangeOrderAsync(portal);
        await ExpireAsync(portal.Tenant, "build", state);

        using var response = await portal.Session.PostAsJsonAsync($"{portal.Url}/change-orders/{publicId}/decision",
            new { decision = "approve", signerName = "Maria" });

        await ExpectReadOnlyAsync(response);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.BldChangeOrders.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).Status.Should().Be(BuildChangeOrderStatus.SentToClient);
    }

    [Fact]
    public async Task An_expired_build_module_refuses_every_client_write()
    {
        var portal = await BuildPortalAsync();
        await ExpireAsync(portal.Tenant, "build", "canceled");
        var someId = Guid.NewGuid();

        var writes = new (string Path, object? Body)[]
        {
            ($"{portal.Url}/change-orders/{someId}/decision", new { decision = "approve", signerName = "Maria" }),
            ($"{portal.Url}/selections/{someId}/choice", new { optionId = Guid.NewGuid(), signerName = "Maria" }),
            ($"{portal.Url}/handover/accept", new { signerName = "Maria" }),
            ($"{portal.Url}/assistance", new { category = "Elétrica", title = "Tomada", description = "Não funciona" }),
            ($"{portal.Url}/assistance/{someId}/feedback", new { rating = 5 }),
        };
        foreach (var (path, body) in writes)
        {
            using var response = await portal.Session.PostAsJsonAsync(path, body);
            await ExpectReadOnlyAsync(response, path);
        }

        // o envio de foto recusa antes de ler o arquivo
        using var upload = new MultipartFormDataContent { { new ByteArrayContent([0xFF, 0xD8, 0xFF]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "foto.jpg" } };
        using var photo = await portal.Session.PostAsync($"{portal.Url}/assistance/{someId}/photos", upload);
        await ExpectReadOnlyAsync(photo, "envio de foto");
    }

    [Fact]
    public async Task An_expired_build_module_keeps_the_portal_readable_and_hides_the_actions()
    {
        var portal = await BuildPortalAsync();
        await SentChangeOrderAsync(portal);
        await OpenSelectionAsync(portal);
        await ExpireAsync(portal.Tenant, "build", "canceled");

        var bootstrap = await Ok(portal.Session.GetAsync(portal.Url));
        bootstrap.GetProperty("readOnly").GetBoolean().Should().BeTrue();
        bootstrap.GetProperty("project").GetProperty("name").GetString().Should().NotBeNullOrEmpty();

        foreach (var path in new[] { "timeline", "progress", "measurements", "documents" })
            await Ok(portal.Session.GetAsync($"{portal.Url}/{path}"));

        var orders = await Ok(portal.Session.GetAsync($"{portal.Url}/change-orders"));
        orders.GetProperty("items").EnumerateArray().Single().GetProperty("canDecide").GetBoolean().Should().BeFalse();
        var selections = await Ok(portal.Session.GetAsync($"{portal.Url}/selections"));
        selections.GetProperty("items").EnumerateArray().Single().GetProperty("canChoose").GetBoolean().Should().BeFalse();
        (await Ok(portal.Session.GetAsync($"{portal.Url}/assistance"))).GetProperty("canOpen").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_password_portal_still_opens_a_session_to_read_after_the_module_expires()
    {
        var portal = await BuildPortalAsync();
        await ExpireAsync(portal.Tenant, "build", "canceled");
        var anonymous = Anonymous();

        // sem sessão a resposta segue 401 antes de qualquer outra regra: nada da configuração do portal é revelado
        using (var noSession = await anonymous.PostAsJsonAsync($"{portal.Url}/handover/accept", new { signerName = "Maria" }))
            noSession.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var session = await Ok(anonymous.PostAsJsonAsync($"{portal.Url}/session", new { password = portal.Password }));
        var client = Anonymous();
        client.DefaultRequestHeaders.Add(SessionHeader, session.GetProperty("token").GetString());
        (await Ok(client.GetAsync(portal.Url))).GetProperty("readOnly").GetBoolean().Should().BeTrue();
        using var close = await client.DeleteAsync($"{portal.Url}/session");
        close.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_valid_build_module_keeps_the_client_deciding()
    {
        var portal = await BuildPortalAsync();
        var (id, publicId) = await SentChangeOrderAsync(portal);

        (await Ok(portal.Session.GetAsync(portal.Url))).GetProperty("readOnly").GetBoolean().Should().BeFalse();
        var orders = await Ok(portal.Session.GetAsync($"{portal.Url}/change-orders"));
        orders.GetProperty("items").EnumerateArray().Single().GetProperty("canDecide").GetBoolean().Should().BeTrue();

        await Ok(portal.Session.PostAsJsonAsync($"{portal.Url}/change-orders/{publicId}/decision", new { decision = "approve", signerName = "Maria" }));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.BldChangeOrders.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).Status.Should().NotBe(BuildChangeOrderStatus.SentToClient);
    }

    [Fact]
    public async Task A_public_portal_keeps_answering_that_a_decision_needs_a_password()
    {
        var portal = await BuildPortalAsync(password: null);
        await ExpireAsync(portal.Tenant, "build", "canceled");

        using var response = await Anonymous().PostAsJsonAsync($"{portal.Url}/change-orders/{Guid.NewGuid()}/decision",
            new { decision = "approve", signerName = "Maria" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("portal_decision_requires_password");
    }

    [Fact]
    public async Task A_suspended_company_takes_no_decision_through_the_build_portal()
    {
        var portal = await BuildPortalAsync();
        var (_, publicId) = await SentChangeOrderAsync(portal);
        await SuspendAsync(portal.Tenant);

        using var response = await portal.Session.PostAsJsonAsync($"{portal.Url}/change-orders/{publicId}/decision",
            new { decision = "approve", signerName = "Maria" });

        await ExpectReadOnlyAsync(response);
    }

    [Fact]
    public async Task A_read_only_portal_does_not_call_the_weather_provider()
    {
        var weather = new BuildForecastTests.FakeWeatherProvider();
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:WeatherEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IWeatherProvider>();
                s.AddSingleton<IWeatherProvider>(weather);
            });
        });
        var tenant = await SyntheticTenant.CreateAsync(_factory, "build");
        var admin = await LoginAsync(tenant);
        var created = await Ok(admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra pública", clientName = "Cliente", type = "House", startDate = Day(-30), expectedEndDate = Day(200), latitude = -23.55m, longitude = -46.63m,
        }), HttpStatusCode.Created);
        var url = await PublishAsync(admin, created.GetProperty("id").GetGuid(), password: null, visibility: new { weather = true, overallProgress = true });
        var pub = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await Ok(pub.GetAsync(url))).TryGetProperty("weather", out var before).Should().BeTrue();
        before.ValueKind.Should().Be(JsonValueKind.Object, "com o módulo válido a previsão vem do provedor");
        var callsWithAccess = weather.CurrentCalls;
        callsWithAccess.Should().BePositive();

        await ExpireAsync(tenant, "build", "canceled");

        var after = await Ok(pub.GetAsync(url));
        (after.TryGetProperty("weather", out var w) && w.ValueKind != JsonValueKind.Null).Should().BeFalse();
        weather.CurrentCalls.Should().Be(callsWithAccess, "portal somente leitura não consome provedor externo");
    }

    // ── Orken Menu ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryExpiredState))]
    public async Task An_expired_restaurant_module_shows_the_menu_and_takes_no_order(string state)
    {
        var (tenant, slug, product) = await MenuPortalAsync();
        await ExpireAsync(tenant, "restaurante", state);
        var pub = Anonymous();

        var menu = await Ok(pub.GetAsync($"/api/public/menu/{slug}"));
        menu.GetProperty("acceptingOrders").GetBoolean().Should().BeFalse();

        using var order = await PlaceOrderAsync(pub, slug, product);
        order.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.RestDeliveryOrders.IgnoreQueryFilters().CountAsync(o => o.TenantId == tenant.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task A_suspended_company_takes_no_order_even_with_a_valid_module()
    {
        var (tenant, slug, product) = await MenuPortalAsync();
        await SuspendAsync(tenant);
        var pub = Anonymous();

        (await Ok(pub.GetAsync($"/api/public/menu/{slug}"))).GetProperty("acceptingOrders").GetBoolean().Should().BeFalse();
        using var order = await PlaceOrderAsync(pub, slug, product);
        order.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private sealed record ServicePortal(SyntheticAccount Tenant, string Slug, Guid Professional, Guid Catalog);
    private sealed record PortalOfBuild(SyntheticAccount Tenant, HttpClient Admin, Guid ProjectId, string Url, string? Password, HttpClient Session);

    private HttpClient Anonymous() => AuthClientFactory.CreateAnonymousClient(_factory);

    private static string NewPhone() => "1198" + Random.Shared.Next(1_000_000, 9_999_999);
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

    private static async Task ExpectOkAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ExpectReadOnlyAsync(HttpResponseMessage response, string? because = null)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{because} {raw}");
        JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString().Should().Be(ReadOnlyCode, because);
    }

    /// <summary>Os mesmos estados de vencido da regra única: a linha do módulo fica, e o acesso acaba agora.</summary>
    private async Task ExpireAsync(SyntheticAccount tenant, string module, string state)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var row = await db.ModuleSubscriptions.SingleAsync(s => s.TenantId == tenant.TenantId && s.ModuleKey == module);
        var yesterday = DateTime.UtcNow.AddDays(-1);
        switch (state)
        {
            case "trial_vencido":
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

    private async Task SuspendAsync(SyntheticAccount tenant)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId)).SetStatus(TenantStatus.Suspended);
        await db.SaveChangesAsync();
    }

    // Service

    private async Task<ServicePortal> ServicePortalAsync(params string[] modules)
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, modules.Length == 0 ? ["service"] : modules);
        var admin = await LoginAsync(tenant);
        var slug = NewSlug("svc");
        await Ok(admin.PatchAsJsonAsync($"/api/stores/{tenant.StoreId}/public-slug", new { publicSlug = slug }));
        await Ok(admin.PutAsJsonAsync("/api/v1/service/settings/preset", new { presetKey = "salao-beleza" }));
        await Ok(admin.PutAsJsonAsync("/api/v1/service/settings/public-booking", new
        {
            publicBookingEnabled = true, bookingDaysAhead = 14, minLeadMinutes = 30, slotIntervalMinutes = 60,
            showPrices = true, autoConfirmAppointments = false, timeZoneId = "UTC",
        }));
        var professional = await Ok(admin.PostAsJsonAsync("/api/v1/service/professionals", new { name = "Pro " + Guid.NewGuid().ToString("N")[..6], workingHoursJson = AllWeek }), HttpStatusCode.Created);
        var catalog = await Ok(admin.PostAsJsonAsync("/api/v1/service/catalog", new { name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price = 80m }), HttpStatusCode.Created);
        return new ServicePortal(tenant, slug, professional.GetProperty("id").GetGuid(), catalog.GetProperty("id").GetGuid());
    }

    private async Task<string> SlotAsync(ServicePortal portal)
    {
        var availability = await Anonymous().GetFromJsonAsync<JsonElement>(
            $"/api/public/service/{portal.Slug}/availability?catalogItemId={portal.Catalog}&professionalId={portal.Professional}");
        return availability.GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("startsAt").GetString()!)
            .First(s => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind) > DateTime.UtcNow.AddDays(1));
    }

    private Task<HttpResponseMessage> BookAsync(ServicePortal portal, string slot, string phone)
        => Anonymous().PostAsJsonAsync($"/api/public/service/{portal.Slug}/appointments", new
        {
            customerName = "Cliente Portal", phone, catalogItemId = portal.Catalog, professionalId = portal.Professional, startsAt = slot,
        });

    private async Task<string> BookedTokenAsync(ServicePortal portal)
    {
        var slot = await SlotAsync(portal);
        var created = await Ok(BookAsync(portal, slot, NewPhone()), HttpStatusCode.Created);
        return created.GetProperty("manageToken").GetString()!;
    }

    // Build

    private static readonly object ClientSections = new
    {
        overallProgress = true, changeOrders = true, selections = true, postConstruction = true, measurements = true, documents = true, clientUpdates = true,
    };

    private async Task<PortalOfBuild> BuildPortalAsync(string? password = "generate")
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "build");
        var admin = await LoginAsync(tenant);
        var created = await Ok(admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra pública", clientName = "Cliente", type = "House", startDate = Day(-30), expectedEndDate = Day(200),
        }), HttpStatusCode.Created);
        var projectId = created.GetProperty("id").GetGuid();
        var secret = password is null ? null : "Px!" + Guid.NewGuid().ToString("N")[..12];
        var url = await PublishAsync(admin, projectId, secret, ClientSections);

        var session = Anonymous();
        if (secret is not null)
        {
            var opened = await Ok(session.PostAsJsonAsync($"{url}/session", new { password = secret }));
            session.DefaultRequestHeaders.Add(SessionHeader, opened.GetProperty("token").GetString());
        }
        return new PortalOfBuild(tenant, admin, projectId, url, secret, session);
    }

    private static async Task<string> PublishAsync(HttpClient admin, Guid projectId, string? password, object visibility)
    {
        var company = NewSlug("constr");
        await Ok(admin.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug = company, displayName = "Construtora", contactEmail = "contato@" + TestCredentials.TestDomain, contactPhone = "11 4000-0000",
        }));
        var slug = NewSlug("obra");
        await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new { slug, access = "Public", title = "Sua obra", visibility }));
        if (password is not null)
        {
            await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }));
            await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new { slug, access = "Password", title = "Sua obra", visibility }));
        }
        await Ok(admin.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null));
        return $"/api/public/build/{company}/{slug}";
    }

    private static async Task<(Guid Id, Guid PublicId)> SentChangeOrderAsync(PortalOfBuild portal)
    {
        var order = await Ok(portal.Admin.PostAsJsonAsync($"/api/v1/build/projects/{portal.ProjectId}/change-orders", new
        {
            title = "Fundação mais profunda", description = "Solo mais fraco que o previsto.", reason = "SiteCondition",
            items = new object[] { new { name = "Concreto extra", kind = "Material", quantity = 5m, unit = "m³", unitCost = 1000m } },
        }), HttpStatusCode.Created);
        var id = order.GetProperty("id").GetGuid();
        await Ok(portal.Admin.PostAsync($"/api/v1/build/change-orders/{id}/send", null));
        return (id, order.GetProperty("publicId").GetGuid());
    }

    private static async Task OpenSelectionAsync(PortalOfBuild portal)
    {
        var selection = await Ok(portal.Admin.PostAsJsonAsync($"/api/v1/build/projects/{portal.ProjectId}/selections",
            new { title = "Piso da sala", deadline = Day(10) }), HttpStatusCode.Created);
        await Ok(portal.Admin.PostAsJsonAsync($"/api/v1/build/selections/{selection.GetProperty("id").GetGuid()}/options",
            new { name = "Cerâmica padrão", priceDelta = 0m, isStandard = true }));
    }

    // Menu

    private async Task<(SyntheticAccount Tenant, string Slug, Guid Product)> MenuPortalAsync()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "restaurante");
        var admin = await LoginAsync(tenant);
        var slug = NewSlug("menu");
        await Ok(admin.PatchAsJsonAsync($"/api/stores/{tenant.StoreId}/public-slug", new { publicSlug = slug }));
        var code = $"MENU-{Guid.NewGuid():N}"[..20];
        var product = await Ok(admin.PostAsJsonAsync("/api/products", new { code, name = code, unit = "Un", salePrice = 25m, costPrice = 5m, trackStock = false, menuVisible = true }), HttpStatusCode.Created);
        return (tenant, slug, product.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> PlaceOrderAsync(HttpClient pub, string slug, Guid product)
        => pub.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug = slug, orderType = "Takeaway", customerName = "Ana", customerPhone = "11911112222",
            items = new[] { new { productId = product, quantity = 1 } },
        });
}
