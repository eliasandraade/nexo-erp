using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Application.Integrations.Weather;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Provisioning;
using Nexo.IntegrationTests.Build;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Depois do vencimento, a empresa fica em somente leitura, incluindo consultas e exportação (decisão do dono).
/// Um módulo com linha de assinatura e sem acesso agora está vencido: GET passa e escrita responde 402. Sem linha nenhuma, o
/// tenant nunca teve o módulo e a resposta segue 403. Vale também com outro módulo ativo: é a empresa de produção com
/// varejo Canceled e build Active. Ver docs/engineering/tenant-access-enforcement.md.
/// </summary>
[Collection("Integration")]
public class ExpiredModuleReadOnlyTests
{
    private const string Code = "subscription_required";
    /// <summary>Um módulo com acesso que não tem portão: tira o middleware da frente e deixa só os portões responderem.</summary>
    private const string UngatedModule = "modulo-sem-portao";

    /// <summary>Uma rota de cada portão por módulo: leitura e escrita no mesmo caminho.</summary>
    private static readonly (string Module, string Path)[] GatedPaths =
    [
        ("varejo", "/api/sales"),                   // RequireModuleAttribute
        ("varejo", "/api/varejo/price-lists"),      // RequireModuleFilter
        ("restaurante", "/api/restaurante/areas"),  // RequireModuleFilter
        ("build", "/api/v1/build/projects"),        // RequireModuleFilter
        ("service", "/api/v1/service/catalog"),     // RequireServiceModuleAttribute
    ];

    private static readonly string[] AllModules = ["varejo", "restaurante", "build", "service"];
    private static readonly string[] ExpiredStates = ["trial_vencido", "periodo_vencido", "past_due_fora_da_carencia", "canceled", "suspended"];

    public static TheoryData<string, string> EveryModuleInEveryExpiredState()
    {
        var data = new TheoryData<string, string>();
        foreach (var module in AllModules)
            foreach (var state in ExpiredStates)
                data.Add(module, state);
        return data;
    }

    private readonly TestWebApplicationFactory _factory;

    public ExpiredModuleReadOnlyTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Módulo vencido: lê e não escreve ──────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryModuleInEveryExpiredState))]
    public async Task An_expired_module_reads_and_refuses_writes(string module, string state)
    {
        var tenant = await NewTenantAsync(t => [Expired(t, module, state)]);
        var client = await LoginAsync(tenant);

        await ExpectReadOnlyAsync(client, module);
    }

    /// <summary>Aqui o middleware deixa passar, porque a empresa tem acesso a outro módulo: quem responde é o portão.</summary>
    [Theory]
    [MemberData(nameof(EveryModuleInEveryExpiredState))]
    public async Task An_expired_module_reads_and_refuses_writes_while_another_module_is_active(string module, string state)
    {
        var tenant = await NewTenantAsync(t => [Expired(t, module, state), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant);

        await ExpectReadOnlyAsync(client, module);
    }

    [Fact]
    public async Task The_production_tenant_reads_varejo_and_only_writes_on_build()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, "build")]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/sales").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync("/api/varejo/price-lists").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/sales", new { }));
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/varejo/price-lists", new { name = "Lista", isDefault = false }));

        // o módulo com acesso e o núcleo seguem como sempre
        await client.GetAsync("/api/v1/build/projects").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.PostAsJsonAsync("/api/v1/build/projects", NewProject()).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Fact]
    public async Task Each_block_at_a_gate_is_logged_with_tenant_and_route_and_no_personal_data()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, "build")]);
        var client = await LoginAsync(tenant);
        var id = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync($"/api/varejo/price-lists/{id}/activate", new { });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var requestId = response.Headers.GetValues("X-Request-Id").Single();
        var log = _factory.ServerLog.For(requestId);
        log.Should().Contain("blocked").And.Contain(tenant.TenantId.ToString()).And.Contain("varejo");
        foreach (var text in new[] { log, _factory.ServerLog.PropertiesFor(requestId) })
            text.Should().NotContain(tenant.Login).And.NotContain(tenant.Email).And.NotContain(id.ToString());
    }

    // ── Todas as rotas com portão ─────────────────────────────────────────────

    [Fact]
    public async Task Every_gated_write_route_answers_402_when_its_module_expired_and_the_company_has_another_active()
    {
        var tenant = await NewTenantAsync(t => [.. AllModules.Select(m => Expired(t, m, "canceled")), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant);

        var routes = GatedRoutes(m => m is "POST" or "PUT" or "PATCH" or "DELETE").ToList();
        routes.Count.Should().BeGreaterThan(100, "a varredura precisa cobrir de fato as rotas com portão");

        var escaped = new List<string>();
        foreach (var (method, endpoint) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), TenantAccessEnforcementTests.PathOf(endpoint.RoutePattern, Guid.NewGuid()))
            {
                Content = TenantAccessEnforcementTests.BodyFor(endpoint),
            };
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.PaymentRequired)
                escaped.Add($"{method} {endpoint.RoutePattern.RawText} => {(int)response.StatusCode}");
        }

        escaped.Should().BeEmpty("toda escrita em módulo vencido responde 402");
    }

    [Fact]
    public async Task No_gated_read_route_is_refused_by_the_gate_when_its_module_expired()
    {
        var tenant = await NewTenantAsync(t => [.. AllModules.Select(m => Expired(t, m, "canceled")), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant);

        var routes = GatedRoutes(m => m == "GET").ToList();
        routes.Count.Should().BeGreaterThan(100, "a varredura precisa cobrir de fato as rotas com portão");

        var refused = new List<string>();
        foreach (var (method, endpoint) in routes)
        {
            using var response = await client.GetAsync(TenantAccessEnforcementTests.PathOf(endpoint.RoutePattern, Guid.NewGuid()));
            var body = await response.Content.ReadAsStringAsync();
            // 403 com corpo próprio é regra de produto (build_predict_disabled, por exemplo). A recusa dos portões é vazia ou a do filtro.
            if (response.StatusCode == HttpStatusCode.PaymentRequired
                || (response.StatusCode == HttpStatusCode.Forbidden && (body.Length == 0 || body.Contains("Module not available"))))
                refused.Add($"{method} {endpoint.RoutePattern.RawText} => {(int)response.StatusCode}");
        }

        refused.Should().BeEmpty("módulo vencido continua legível");
    }

    // ── Exportação de verdade ─────────────────────────────────────────────────

    [Fact]
    public async Task A_real_sale_stays_consultable_and_exportable_after_the_module_expires()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "varejo");
        await ProvisionAccountsAsync(tenant);
        var client = await LoginAsync(tenant);
        var cash = (await (await client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(100m, "Abertura")))
            .Content.ReadFromJsonAsync<CashSessionDto>())!;
        var sale = await PaidSaleAsync(client, cash.Id);
        await RevokeAsync(tenant, "varejo");

        (await client.GetAsync("/api/sales/paged?page=1&pageSize=10")).StatusCode.Should().Be(HttpStatusCode.OK);
        await client.GetAsync($"/api/sales/{sale.Id}").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectPdfAsync(client.GetAsync($"/api/sales/{sale.Id}/receipt.pdf"));
        await ExpectPdfAsync(client.GetAsync($"/api/cash/sessions/{cash.Id}/close-report.pdf"));

        var today = BusinessClock.Today().ToString("yyyy-MM-dd");
        using var csv = await client.GetAsync($"/api/reports/sales/export?from={today}&to={today}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK, await csv.Content.ReadAsStringAsync());
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2, "o cabeçalho e a venda");

        await client.GetAsync($"/api/reports/sales?from={today}&to={today}").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync($"/api/sales/{sale.Id}/cancel", new CancelSaleRequest("Sem acesso")));
    }

    [Fact]
    public async Task A_real_build_budget_stays_exportable_as_pdf_after_the_module_expires()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "build");
        var client = await LoginAsync(tenant);
        var created = await client.PostAsJsonAsync("/api/v1/build/budgets", new { name = "Casa", marginPercent = 10m });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var budget = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new { name = "Concreto", category = "Material", kind = "Material", quantity = 1m, unit = "m3", unitCost = 500m })
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await RevokeAsync(tenant, "build");

        await ExpectPdfAsync(client.GetAsync($"/api/v1/build/budgets/{budget}/pdf"));
        await client.GetAsync($"/api/v1/build/budgets/{budget}").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsync($"/api/v1/build/budgets/{budget}/approve", null));
    }

    [Fact]
    public async Task The_restaurant_report_works_after_the_module_expires()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "restaurante", "trial_vencido")]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/restaurante/reports/summary").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync("/api/restaurante/financeiro/summary").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ── Nunca teve o módulo ───────────────────────────────────────────────────

    [Theory]
    [InlineData("varejo", "/api/sales")]
    [InlineData("varejo", "/api/varejo/price-lists")]
    [InlineData("restaurante", "/api/restaurante/areas")]
    [InlineData("service", "/api/v1/service/catalog")]
    public async Task A_module_the_tenant_never_had_answers_403_on_reads_and_writes(string module, string path)
    {
        var tenant = await NewTenantAsync(t => [ModuleSubscription.CreateAdminGrant(t, "build")]);
        tenant.TenantId.Should().NotBeEmpty(module);
        var client = await LoginAsync(tenant);

        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await client.PostAsJsonAsync(path, new { }).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    [Fact]
    public async Task Another_expired_module_does_not_open_a_module_the_tenant_never_had()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/v1/build/projects").ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await client.PostAsJsonAsync("/api/v1/build/projects", NewProject()).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    /// <summary>
    /// Sem módulo nenhum com acesso, o middleware responde antes do portão, como já era: a escrita é 402 mesmo no módulo que a
    /// empresa nunca teve. É o que a varredura Every_tenant_write_route_answers_402_without_access exige. A leitura segue 403.
    /// </summary>
    [Theory]
    [InlineData("sem_linha_nenhuma")]
    [InlineData("outro_modulo_vencido")]
    public async Task Without_any_module_with_access_the_middleware_answers_writes_first(string situation)
    {
        var tenant = await NewTenantAsync(t => situation == "sem_linha_nenhuma" ? [] : [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/v1/build/projects").ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/v1/build/projects", NewProject()));
    }

    // ── Módulo válido e concessões de produção: nada muda ─────────────────────

    [Theory]
    [InlineData("admin_sem_fim")]
    [InlineData("admin_fim_futuro")]
    [InlineData("trial_no_prazo")]
    [InlineData("past_due_na_carencia")]
    public async Task A_valid_grant_keeps_reading_and_writing_on_its_module(string state)
    {
        var tenant = await NewTenantAsync(t => [state switch
        {
            "admin_sem_fim" => ModuleSubscription.CreateAdminGrant(t, "restaurante"),
            "admin_fim_futuro" => ModuleSubscription.CreateAdminGrant(t, "restaurante", expiresAt: DateTime.UtcNow.AddDays(30)),
            "trial_no_prazo" => ModuleSubscription.CreateTrial(t, "restaurante", days: 3),
            _ => PastDue(ModuleSubscription.CreateAdminGrant(t, "restaurante"), ModuleSubscription.PastDueGraceDays - 1),
        }]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/restaurante/areas").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        // a segunda escrita já lê os caches do portão: o veredito não muda
        await client.PostAsJsonAsync("/api/restaurante/areas", new { name = Unique("Salão") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
        await client.PostAsJsonAsync("/api/restaurante/areas", new { name = Unique("Salão") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Theory]
    [InlineData("/api/sales")]
    [InlineData("/api/varejo/price-lists")]
    public async Task A_valid_module_is_not_answered_by_the_gate(string path)
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var client = await LoginAsync(tenant);

        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        using var write = await client.PostAsJsonAsync(path, new { });
        write.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired).And.NotBe(HttpStatusCode.Forbidden);
    }

    // ── Administração legítima ────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blocking_a_user_works_in_read_only_and_creating_one_answers_402(bool anotherModuleActive)
    {
        var tenant = await NewTenantAsync(t => anotherModuleActive
            ? [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]
            : [Expired(t, "varejo", "canceled")]);
        var leaving = await SyntheticTenant.AddUserAsync(_factory, tenant, UserRole.Vendedor);
        var director = await LoginAsync(tenant);
        var leavingClient = await LoginAsync(leaving);
        await leavingClient.GetAsync("/api/categories").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await director.PutAsJsonAsync($"/api/users/{leaving.UserId}", UserBody(leaving, "vendedor", "blocked")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await leavingClient.GetAsync("/api/categories").ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        var login = $"novo{Guid.NewGuid():N}"[..20];
        var create = director.PostAsJsonAsync("/api/users", new { fullName = "Pessoa Nova", email = $"{login}@{TestCredentials.TestDomain}", login,
            password = TestCredentials.SameTenantManagerPassword, role = "vendedor" });
        if (anotherModuleActive) (await create).StatusCode.Should().Be(HttpStatusCode.Created, "a empresa tem outro módulo com acesso");
        else await ExpectBlockedAsync(create);
    }

    [Fact]
    public async Task Exempting_the_user_update_does_not_widen_who_may_do_it()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var manager = await SyntheticTenant.AddUserAsync(_factory, tenant, UserRole.Gerente);
        var client = await LoginAsync(manager);

        await client.PutAsJsonAsync($"/api/users/{tenant.UserId}", UserBody(tenant, "vendedor", "blocked")).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    [Fact]
    public async Task A_director_edits_the_own_profile_in_read_only()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);

        var updated = await client.PutAsJsonAsync($"/api/users/{tenant.UserId}", new { fullName = "Novo Nome", email = tenant.Email, role = "diretoria", phone = (string?)null, notes = (string?)null, status = "active" });

        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fullName").GetString().Should().Be("Novo Nome");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_checkout_stays_exempt_from_the_402(bool anotherModuleActive)
    {
        var tenant = await NewTenantAsync(t => anotherModuleActive
            ? [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]
            : [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);

        using var checkout = await client.PostAsJsonAsync("/api/billing/checkout", new { moduleKey = "restaurante", billingPeriod = "monthly" });

        checkout.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired, await checkout.Content.ReadAsStringAsync());
        (await client.GetAsync("/api/billing/subscriptions")).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
    }

    // ── Sessão ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_session_lists_the_read_only_modules_apart_from_the_active_ones()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), Expired(t, "service", "trial_vencido"), ModuleSubscription.CreateAdminGrant(t, "build")]);

        var client = _factory.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(tenant.Login, tenant.Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var fromLogin = body.GetProperty("session");
        ModulesOf(fromLogin, "activeModules").Should().Equal("build");
        ModulesOf(fromLogin, "readOnlyModules").Should().BeEquivalentTo("varejo", "service");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        ModulesOf(me, "activeModules").Should().Equal("build");
        ModulesOf(me, "readOnlyModules").Should().BeEquivalentTo("varejo", "service");

        var switched = await client.PostAsJsonAsync("/api/auth/switch-store", new { storeId = tenant.StoreId, refreshToken = body.GetProperty("refreshToken").GetString() });
        switched.StatusCode.Should().Be(HttpStatusCode.OK, await switched.Content.ReadAsStringAsync());
        ModulesOf((await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session"), "readOnlyModules").Should().BeEquivalentTo("varejo", "service");
    }

    [Fact]
    public async Task A_tenant_with_a_valid_grant_or_nothing_has_no_read_only_module()
    {
        foreach (var rows in new Func<Guid, ModuleSubscription[]>[] { t => [ModuleSubscription.CreateAdminGrant(t, "varejo")], _ => [] })
        {
            var tenant = await NewTenantAsync(rows);
            var client = await LoginAsync(tenant);
            ModulesOf(await client.GetFromJsonAsync<JsonElement>("/api/auth/me"), "readOnlyModules").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task The_impersonation_session_lists_the_read_only_modules_too()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);

        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenant.TenantId}/impersonate", new { reason = "Suporte: conferir o somente leitura" });

        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        ModulesOf((await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session"), "readOnlyModules").Should().Equal("varejo");
    }

    // ── O valor volta a ser válido: a escrita volta sem esperar cache ─────────

    [Theory]
    [InlineData("varejo", "/api/sales")]
    [InlineData("varejo", "/api/varejo/price-lists")]
    [InlineData("restaurante", "/api/restaurante/areas")]
    [InlineData("build", "/api/v1/build/projects")]
    [InlineData("service", "/api/v1/service/catalog")]
    public async Task A_platform_grant_reopens_the_writes_and_a_revoke_closes_them_at_once(string module, string path)
    {
        var tenant = await NewTenantAsync(t => [Expired(t, module, "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant);
        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);

        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);   // o vencido fica nos caches dos portões
        await ExpectBlockedAsync(client.PostAsJsonAsync(path, new { }));

        await platform.PostAsJsonAsync($"/api/platform/tenants/{tenant.TenantId}/modules", new { moduleKey = module }).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        using (var reopened = await client.PostAsJsonAsync(path, new { }))
            reopened.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired).And.NotBe(HttpStatusCode.Forbidden);
        ModulesOf(await client.GetFromJsonAsync<JsonElement>("/api/auth/me"), "readOnlyModules").Should().BeEmpty();

        await platform.DeleteAsync($"/api/platform/tenants/{tenant.TenantId}/modules/{module}").ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync(path, new { }));
        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Theory]
    [InlineData("build", "/api/v1/build/projects")]
    [InlineData("varejo", "/api/sales")]
    public async Task A_stripe_webhook_reopens_the_writes_at_once(string module, string path)
    {
        const string secret = "whsec_teste_somente_leitura";
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StripeEnabled", "true");
            b.UseSetting("Integrations:Stripe:WebhookSecret", secret);
        });
        var subscription = $"sub_{Guid.NewGuid():N}";
        var tenant = await NewTenantAsync(t =>
        [
            Canceled(ModuleSubscription.CreateFromStripe(t, module, subscription, "price_teste", PlanType.Monthly, DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-1))),
            ModuleSubscription.CreateAdminGrant(t, UngatedModule),
        ]);
        var client = await LoginAsync(tenant, host);
        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync(path, new { }));

        _factory.StripeSubscriptions.Set(subscription, "active");
        var body = TenantAccessEnforcementTests.SubscriptionUpdatedEvent(subscription, "active");
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        webhook.Headers.Add("Stripe-Signature", TenantAccessEnforcementTests.Sign(body, secret));
        (await host.CreateClient().SendAsync(webhook)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var reopened = await client.PostAsJsonAsync(path, new { });
        reopened.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired).And.NotBe(HttpStatusCode.Forbidden);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/sales")]
    [InlineData("/api/varejo/price-lists")]
    public async Task The_gate_adds_no_query_per_request_with_the_cache_warm(string path)
    {
        var expired = await LoginAsync(await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]));
        var valid = await LoginAsync(await NewTenantAsync(t => [ModuleSubscription.CreateAdminGrant(t, "varejo")]));

        foreach (var client in new[] { expired, valid })
            await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await SqlCountOfAsync(expired, path)).Should().Be(await SqlCountOfAsync(valid, path), "o portão decide com o que já está em cache");
    }

    [Fact]
    public async Task A_tenant_cache_entry_from_before_this_change_is_read_again()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);
        using (var scope = _factory.Services.CreateScope())
        {
            // o formato de antes, sem a lista de vencidos: o que estiver no Redis na hora do deploy
            await scope.ServiceProvider.GetRequiredService<ICacheService>().SetAsync($"tenant:{tenant.TenantId}:info",
                new { slug = "antigo", status = "Active", activeModules = new List<string>() }, TimeSpan.FromMinutes(5));
        }

        await client.GetAsync("/api/sales").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Fact]
    public async Task With_redis_down_the_expired_module_still_reads_and_refuses_writes()
    {
        var host = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ICacheService>();
            s.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            s.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                StackExchange.Redis.ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=100,connectRetry=0"));
            s.AddScoped<ICacheService, Nexo.Infrastructure.Cache.RedisCacheService>();
        }));
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled"), ModuleSubscription.CreateAdminGrant(t, UngatedModule)]);
        var client = await LoginAsync(tenant, host);

        await client.GetAsync("/api/sales").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/sales", new { }));
    }

    // ── Interruptor ───────────────────────────────────────────────────────────

    /// <summary>O interruptor desliga a regra da empresa inteira. Os portões já recusavam antes dela e seguem recusando.</summary>
    [Fact]
    public async Task With_the_switch_off_a_write_on_an_expired_module_is_still_refused()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Billing:EnforceTenantAccess", "false"));
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant, host);

        await client.GetAsync("/api/sales").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/sales", new { }));
        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    // ── GET com efeito colateral ──────────────────────────────────────────────

    [Fact]
    public async Task Reading_the_restaurant_settings_of_an_expired_module_records_nothing()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "restaurante", "canceled")]);
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/restaurante/settings").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.FoodServiceSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenant.TenantId)).Should().BeFalse("a leitura não grava");
    }

    [Fact]
    public async Task Reading_the_restaurant_settings_with_the_module_valid_still_creates_the_defaults()
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory, "restaurante");
        var client = await LoginAsync(tenant);

        await client.GetAsync("/api/restaurante/settings").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.FoodServiceSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenant.TenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task An_expired_build_module_does_not_call_the_weather_provider()
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
        var client = await LoginAsync(tenant, host);
        var created = await client.PostAsJsonAsync("/api/v1/build/projects", NewProject(latitude: -23.55m, longitude: -46.63m));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var project = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{project}/weather");
        before.GetProperty("available").GetBoolean().Should().BeTrue("com o módulo válido a previsão vem do provedor");
        var callsWithAccess = weather.DailyCalls;
        callsWithAccess.Should().BePositive();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.ModuleSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.TenantId)).Cancel();
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IModuleAccessService>().InvalidateCache(tenant.TenantId, "build");
            await scope.ServiceProvider.GetRequiredService<ICacheService>().RemoveAsync($"tenant:{tenant.TenantId}:info");
            await scope.ServiceProvider.GetRequiredService<ICacheService>().RemoveAsync($"tenant:{tenant.TenantId}:modules");
        }

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{project}/weather");
        after.GetProperty("available").GetBoolean().Should().BeFalse();
        after.GetProperty("reason").GetString().Should().Be("module_read_only");
        await client.GetAsync($"/api/v1/build/projects/{project}/forecast").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync($"/api/v1/build/projects/{project}/forecast/history").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        weather.DailyCalls.Should().Be(callsWithAccess, "somente leitura não consome provedor externo");
    }

    [Theory]
    [InlineData("/api/financial/ledger?regime=accrual")]
    [InlineData("/api/financial/cash-flow")]
    public async Task Reading_the_finances_of_a_read_only_company_generates_no_recurring_title(string path)
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        await ProvisionAccountsAsync(tenant);
        Guid recurrenceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var month = BusinessClock.Today();
            var recurrence = FinancialRecurrence.Create(tenant.TenantId, TransactionType.Payable, FinancialCategory.OperatingExpense, "Aluguel", 100m, 5,
                new DateOnly(month.Year, month.Month, 1), null, tenant.UserId);
            db.FinancialRecurrences.Add(recurrence);
            await db.SaveChangesAsync();
            recurrenceId = recurrence.Id;
        }
        var client = await LoginAsync(tenant);

        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.FinancialTransactions.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.TenantId)).Should().BeFalse("somente leitura não gera título");
            (await db.FinancialRecurrences.IgnoreQueryFilters().SingleAsync(r => r.Id == recurrenceId)).GeneratedThrough.Should().BeNull();
        }

        // o que ficou pendente é gerado quando a assinatura volta
        await (await AuthClientFactory.LoginAsPlatformAsync(_factory)).PostAsJsonAsync($"/api/platform/tenants/{tenant.TenantId}/modules", new { moduleKey = "varejo" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.FinancialTransactions.IgnoreQueryFilters().CountAsync(t => t.TenantId == tenant.TenantId)).Should().BePositive();
        }
    }

    // ── Rotas de escrita que só leem ──────────────────────────────────────────

    [Fact]
    public async Task The_tax_routes_that_only_read_reach_their_action_and_the_ones_that_record_stay_blocked()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);
        var id = Guid.NewGuid();

        (await client.PostAsJsonAsync($"/api/tax/calculations/{id}/verify", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/tax/calculations/coverage", new { })).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);

        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/tax/calculations", new { }));
        await ExpectBlockedAsync(client.PostAsJsonAsync($"/api/tax/calculations/from-sale/{id}", new { }));
        await ExpectBlockedAsync(client.PostAsJsonAsync($"/api/tax/calculations/{id}/opportunities/detect", new { }));
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/tax/simulations", new { }));
        // grava a recusa na auditoria e não consulta dado da empresa
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/tax/simulations/supplier-comparison", new { }));
    }

    [Fact]
    public async Task The_manager_checks_stay_blocked_because_they_only_authorize_a_write()
    {
        var tenant = await NewTenantAsync(t => [Expired(t, "varejo", "canceled")]);
        var client = await LoginAsync(tenant);
        var credentials = new { login = tenant.Login, password = tenant.Password };

        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/users/validate-manager", credentials));
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/auth/verify-manager", credentials));
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private async Task ExpectReadOnlyAsync(HttpClient client, string module)
    {
        foreach (var (_, path) in GatedPaths.Where(p => p.Module == module))
        {
            await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
            await ExpectBlockedAsync(client.PostAsJsonAsync(path, new { }));
            // a segunda já lê os caches dos portões
            await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
            await ExpectBlockedAsync(client.PostAsJsonAsync(path, new { }));
        }
    }

    private async Task ExpectBlockedAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired, await _factory.ServerLog.DescribeAsync(response));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(Code);
        body.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("details").GetArrayLength().Should().Be(0);
        body.GetProperty("requestId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private async Task ExpectPdfAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        response.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(response));
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync(), 0, 5).Should().Be("%PDF-");
    }

    private async Task<int> SqlCountOfAsync(HttpClient client, string path)
    {
        var marker = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(SqlCommandCounter.Header, marker);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return _factory.Services.GetRequiredService<SqlCommandCounter>().CountFor(marker);
    }

    private IEnumerable<(string Method, RouteEndpoint Endpoint)> GatedRoutes(Func<string, bool> method)
    {
        foreach (var endpoint in _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (!IsGated(endpoint) || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null) continue;
            foreach (var m in (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Where(method))
                yield return (m, endpoint);
        }
    }

    private static bool IsGated(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<Nexo.Api.Filters.RequireModuleAttribute>() is not null
           || endpoint.Metadata.GetMetadata<Nexo.Api.Attributes.RequireModuleAttribute>() is not null
           || endpoint.Metadata.GetMetadata<Nexo.Api.Attributes.RequireServiceModuleAttribute>() is not null;

    private static string[] ModulesOf(JsonElement session, string property)
        => session.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(m => m.GetString()!).ToArray()
            : throw new InvalidOperationException($"A sessão não traz a lista {property}: {session}");

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    private static object NewProject(decimal? latitude = null, decimal? longitude = null)
    {
        var today = BusinessClock.Today();
        return new
        {
            name = Unique("Obra"), clientName = "Cliente", type = "House",
            startDate = today.AddDays(-10).ToString("yyyy-MM-dd"), expectedEndDate = today.AddDays(90).ToString("yyyy-MM-dd"),
            latitude, longitude,
        };
    }

    private static object UserBody(SyntheticAccount user, string role, string status)
        => new { fullName = user.FullName, email = user.Email, role, phone = (string?)null, notes = (string?)null, status };

    private static ModuleSubscription Canceled(ModuleSubscription s) { s.Cancel(); return s; }

    private static ModuleSubscription PastDue(ModuleSubscription s, int daysAgo) { s.MarkPastDue(DateTime.UtcNow.AddDays(-daysAgo)); return s; }

    private static ModuleSubscription Expired(Guid t, string module, string state)
    {
        var grant = ModuleSubscription.CreateAdminGrant(t, module);
        switch (state)
        {
            case "trial_vencido":
                return ModuleSubscription.CreateTrial(t, module, days: -1);
            case "periodo_vencido":
                return ModuleSubscription.CreateAdminGrant(t, module, expiresAt: DateTime.UtcNow.AddDays(-1));
            case "past_due_fora_da_carencia":
                return PastDue(grant, ModuleSubscription.PastDueGraceDays + 1);
            case "canceled":
                return Canceled(grant);
            case "suspended":
                // o período ainda não acabou: é o status que tira o acesso
                grant.SyncFromStripe(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(20), false);
                return grant;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private async Task<SyntheticAccount> NewTenantAsync(Func<Guid, ModuleSubscription[]> rows)
    {
        var tenant = await SyntheticTenant.CreateAsync(_factory);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.ModuleSubscriptions.AddRange(rows(tenant.TenantId));
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task ProvisionAccountsAsync(SyntheticAccount tenant)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DefaultFinancialAccountProvisioner>().EnsureAsync(tenant.TenantId);
        await scope.ServiceProvider.GetRequiredService<NexoDbContext>().SaveChangesAsync();
    }

    private Task<HttpClient> LoginAsync(SyntheticAccount account) => LoginAsync(account, _factory);

    private static async Task<HttpClient> LoginAsync(SyntheticAccount account, WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>A plataforma revoga: a linha fica Canceled, e o módulo vira vencido na hora.</summary>
    private async Task RevokeAsync(SyntheticAccount tenant, string module)
    {
        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);
        await platform.DeleteAsync($"/api/platform/tenants/{tenant.TenantId}/modules/{module}").ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
    }

    private static async Task<SaleDto> PaidSaleAsync(HttpClient c, Guid cashSessionId)
    {
        const decimal price = 50m;
        var code = $"RO-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: price, CostPrice: 1m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product.Id, 10, "ManualEntry", "Somente leitura"))).EnsureSuccessStatusCode();
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: cashSessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(product.Id, 1, price))).EnsureSuccessStatusCode();
        var confirm = await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm", new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", price)]));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return (await confirm.Content.ReadFromJsonAsync<SaleDto>())!;
    }
}
