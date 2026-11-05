using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Cache;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using StackExchange.Redis;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Sem nenhum módulo com acesso agora (regra única <see cref="ModuleSubscription.HasAccessAt"/>), a empresa fica somente
/// para leitura no servidor: lê e exporta, mas não escreve, e continua podendo entrar e pagar. Com qualquer módulo com
/// acesso, nada muda. Cada teste usa uma empresa própria: o que um bloqueia não interfere nos outros da suíte.
/// Ver docs/engineering/tenant-access-enforcement.md.
/// </summary>
[Collection("Integration")]
public class TenantAccessEnforcementTests
{
    private const string Code = "subscription_required";

    /// <summary>Rotas de escrita com sessão de empresa que seguem abertas sem assinatura. Lista exata de propósito.</summary>
    private static readonly string[] ExemptAuthenticatedWrites =
    [
        "POST api/auth/logout",
        "POST api/auth/switch-store",
        "POST api/auth/impersonation/end",
        "POST api/auth/sessions/revoke-all",
        "POST api/auth/mfa/setup",
        "POST api/auth/mfa/enable",
        "POST api/auth/mfa/disable",
        "POST api/users/{id:guid}/change-password",
        "POST api/users/{id:guid}/admin-reset-password",
        "POST api/users/{id:guid}/resend-invite",
        "POST api/users/{id:guid}/mfa/disable",
        "POST api/billing/checkout",
        "POST api/billing/portal",
    ];

    private readonly TestWebApplicationFactory _factory;

    public TenantAccessEnforcementTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Concessões válidas nunca são bloqueadas ───────────────────────────────

    [Theory]
    [InlineData("admin_active_sem_fim")]
    [InlineData("admin_active_fim_futuro")]
    [InlineData("admin_active_e_outro_canceled")]
    [InlineData("lifetime")]
    [InlineData("trial_no_prazo")]
    [InlineData("past_due_na_carencia")]
    [InlineData("admin_trialing_no_prazo")]
    [InlineData("admin_past_due_na_carencia")]
    public async Task A_valid_grant_keeps_writing(string state)
    {
        var tenant = await NewTenantAsync(state);
        tenant.Rows.Any(r => r.HasAccessAtTime(DateTime.UtcNow)).Should().BeTrue("este estado tem acesso pela regra única");
        if (state.StartsWith("admin_"))
            tenant.Rows.Should().OnlyContain(r => r.PlanType == PlanType.AdminGrant, "são as concessões que existem hoje em produção");
        var client = await tenant.LoginAsync(_factory);

        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
        // a segunda escrita já lê a entrada do cache: o veredito não muda
        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
        await client.GetAsync("/api/categories").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ── Sem acesso: somente leitura, e pagar continua possível ────────────────

    [Theory]
    [InlineData("trial_vencido")]
    [InlineData("active_periodo_vencido")]
    [InlineData("past_due_fora_da_carencia")]
    [InlineData("todos_canceled")]
    [InlineData("todos_suspended")]
    [InlineData("sem_assinatura")]
    [InlineData("admin_trialing_vencido")]
    [InlineData("admin_past_due_fora_da_carencia")]
    public async Task Without_access_the_tenant_reads_but_does_not_write(string state)
    {
        var tenant = await NewTenantAsync(state);
        tenant.Rows.Any(r => r.HasAccessAtTime(DateTime.UtcNow)).Should().BeFalse("este estado não tem acesso pela regra única");
        var client = await tenant.LoginAsync(_factory);

        // o núcleo que não tinha gate nenhum: tudo legível
        foreach (var path in new[]
                 {
                     "/api/products", "/api/categories", "/api/stock", "/api/customers", "/api/suppliers",
                     "/api/cash/sessions", "/api/financial/accounts", "/api/fiscal/readiness", "/api/varejo/purchases",
                 })
            await client.GetAsync(path).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        var name = Unique("Bloqueada");
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name }));
        // a segunda já lê a entrada do cache
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name }));
        await ExpectBlockedAsync(client.PutAsJsonAsync($"/api/categories/{Guid.NewGuid()}", new { name }));
        await ExpectBlockedAsync(client.DeleteAsync($"/api/varejo/purchases/{Guid.NewGuid()}/items/{Guid.NewGuid()}"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.Categories.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant.Id && c.Name == name))
                .Should().BeFalse("a escrita recusada não grava nada");
        }

        // pagar: chega à ação de cobrança (aqui o Stripe está desligado, e é ela quem responde)
        var checkout = await client.PostAsJsonAsync("/api/billing/checkout", new { moduleKey = "restaurante", billingPeriod = "monthly" });
        checkout.StatusCode.Should().Be(HttpStatusCode.NotFound, await _factory.ServerLog.DescribeAsync(checkout));
        (await checkout.Content.ReadAsStringAsync()).Should().Contain("Billing não está habilitado");
        (await client.PostAsJsonAsync("/api/billing/portal", new { })).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
        (await client.GetAsync("/api/billing/subscriptions")).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task A_tenant_without_access_really_reaches_a_checkout_session()
    {
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StripeEnabled", "true");
            b.UseSetting("Billing:NewCheckoutsEnabled", "true");
            b.UseSetting("Integrations:Stripe:PriceIds:restaurante_monthly", "price_teste");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IBillingProvider>();
                s.AddScoped<IBillingProvider, FakeBillingProvider>();
            });
        });
        var tenant = await NewTenantAsync("trial_vencido");
        var client = await tenant.LoginAsync(host);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));

        var checkout = await client.PostAsJsonAsync("/api/billing/checkout", new { moduleKey = "restaurante", billingPeriod = "monthly" });

        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(checkout));
        (await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutUrl").GetString().Should().Be(FakeBillingProvider.Url);
    }

    // ── Todas as rotas: nenhuma escrita do tenant escapa, nenhuma isenta é bloqueada ─────────

    [Fact]
    public async Task Every_tenant_write_route_answers_402_without_access()
    {
        var tenant = await NewTenantAsync("sem_assinatura");
        var client = await tenant.LoginAsync(_factory);

        var routes = WriteRoutes().Where(r => IsTenantRoute(r.Template) && !IsAnonymous(r.Endpoint) && !ExemptAuthenticatedWrites.Contains(r.Key)).ToList();
        routes.Count.Should().BeGreaterThan(100, "a varredura precisa cobrir de fato as rotas da API");

        var escaped = new List<string>();
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route.Endpoint.RoutePattern, Guid.NewGuid()))
            {
                Content = BodyFor(route.Endpoint),
            };
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.PaymentRequired)
                escaped.Add($"{route.Key} => {(int)response.StatusCode}");
        }

        escaped.Should().BeEmpty("toda escrita de empresa sem acesso responde 402, exceto as isenções listadas");
    }

    [Fact]
    public void Each_exempt_route_exists()
    {
        var known = WriteRoutes().Select(r => r.Key).ToHashSet();

        ExemptAuthenticatedWrites.Where(e => !known.Contains(e)).Should().BeEmpty("uma isenção que não bate com nenhuma rota é erro de digitação ou rota renomeada");
        AnonymousWrites().Select(r => r.Key).Should().Contain(new[]
        {
            "POST api/auth/login", "POST api/auth/refresh", "POST api/auth/mfa/verify", "POST api/auth/register",
            "POST api/auth/forgot-password", "POST api/auth/reset-password", "POST api/auth/resend-verification",
            "POST api/billing/webhook",
        });
    }

    [Fact]
    public async Task Exempt_routes_stay_open_with_the_session_of_a_tenant_without_access()
    {
        var tenant = await NewTenantAsync("todos_canceled");

        foreach (var route in WriteRoutes().Where(r => ExemptAuthenticatedWrites.Contains(r.Key)))
        {
            // sessão nova a cada rota: encerrar sessões é uma das isentas
            var client = await tenant.LoginAsync(_factory);
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route.Endpoint.RoutePattern, tenant.UserId))
            {
                Content = JsonContent.Create(new { }),
            };
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired, route.Key);
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, route.Key);
        }
    }

    [Fact]
    public async Task Anonymous_routes_stay_open_even_when_the_caller_sends_the_session_of_a_tenant_without_access()
    {
        // quem está logado e abre o cardápio público, ou renova a sessão, manda o token mesmo assim
        var tenant = await NewTenantAsync("sem_assinatura");
        var client = await tenant.LoginAsync(_factory);

        var routes = AnonymousWrites().Where(r => IsTenantRoute(r.Template)).ToList();
        routes.Should().Contain(r => r.Template.StartsWith("api/public/"));
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), PathOf(route.Endpoint.RoutePattern, Guid.NewGuid()))
            {
                Content = JsonContent.Create(new { }),
            };
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired, route.Key);
        }
    }

    [Fact]
    public async Task Casing_and_a_trailing_slash_do_not_change_the_answer()
    {
        var tenant = await NewTenantAsync("sem_assinatura");
        var client = await tenant.LoginAsync(_factory);

        await ExpectBlockedAsync(client.PostAsJsonAsync("/API/CATEGORIES", new { name = Unique("Categoria") }));
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories/", new { name = Unique("Categoria") }));
        (await client.PostAsJsonAsync("/API/BILLING/PORTAL/", new { })).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
        (await client.PostAsJsonAsync("/api/Auth/Logout", new { refreshToken = "" })).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Reads_of_any_kind_and_requests_outside_the_tenant_api_are_not_blocked()
    {
        var tenant = await NewTenantAsync("sem_assinatura");
        var client = await tenant.LoginAsync(_factory);

        using (var head = new HttpRequestMessage(HttpMethod.Head, "/api/categories"))
            (await client.SendAsync(head)).StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
        // conectar ao tempo real é um POST que não escreve dado nenhum
        (await client.PostAsync("/hubs/restaurant/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        // rota que não existe continua 404, não vira cobrança
        (await client.PostAsJsonAsync("/api/rota-que-nao-existe", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_session_the_answer_is_still_401()
    {
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/categories", new { name = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── A assinatura volta: a escrita volta sem esperar o cache expirar ───────

    [Fact]
    public async Task A_platform_grant_and_revoke_apply_at_once()
    {
        var tenant = await NewTenantAsync("todos_canceled");
        var client = await tenant.LoginAsync(_factory);
        var platform = await PlatformClientAsync();

        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));

        await platform.PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/modules", new { moduleKey = "varejo" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);

        await platform.DeleteAsync($"/api/platform/tenants/{tenant.Id}/modules/varejo").ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));
    }

    [Fact]
    public async Task A_stripe_webhook_reopens_the_writes_at_once()
    {
        const string secret = "whsec_teste_acesso";
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StripeEnabled", "true");
            b.UseSetting("Integrations:Stripe:WebhookSecret", secret);
        });
        var tenant = await NewTenantAsync("stripe_cancelada");
        var client = await tenant.LoginAsync(host);
        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));

        // o webhook grava o estado atual da assinatura no Stripe, não o corpo do evento
        _factory.StripeSubscriptions.Set(tenant.Rows[0].StripeSubscriptionId!, "active");
        var body = SubscriptionUpdatedEvent(tenant.Rows[0].StripeSubscriptionId!, "active");
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        webhook.Headers.Add("Stripe-Signature", Sign(body, secret));
        (await host.CreateClient().SendAsync(webhook)).StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    // ── Interruptor, cache fora do ar, impersonação, log ──────────────────────

    [Fact]
    public async Task With_the_switch_off_nothing_is_blocked_but_the_log_says_what_would_be()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Billing:EnforceTenantAccess", "false"));
        var tenant = await NewTenantAsync("trial_vencido");
        var client = await tenant.LoginAsync(host);

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await _factory.ServerLog.DescribeAsync(response));
        var log = _factory.ServerLog.For(response.Headers.GetValues("X-Request-Id").Single());
        log.Should().Contain("would be blocked").And.Contain(tenant.Id.ToString()).And.Contain("/api/categories");
    }

    [Fact]
    public async Task An_invalid_switch_value_keeps_the_rule_on()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Billing:EnforceTenantAccess", "talvez"));
        var tenant = await NewTenantAsync("trial_vencido");
        var client = await tenant.LoginAsync(host);

        await ExpectBlockedAsync(client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));
    }

    [Theory]
    [InlineData("trial_vencido", false)]
    [InlineData("admin_active_sem_fim", true)]
    public async Task With_redis_down_the_answer_comes_from_the_database(string state, bool writes)
    {
        // o serviço real de produção, apontado para um Redis que não responde
        var host = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ICacheService>();
            s.RemoveAll<IConnectionMultiplexer>();
            s.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=100,connectRetry=0"));
            s.AddScoped<ICacheService, RedisCacheService>();
        }));
        var tenant = await NewTenantAsync(state);
        var client = await tenant.LoginAsync(host);

        await client.GetAsync("/api/categories").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var write = client.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") });
        if (writes) await write.ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
        else await ExpectBlockedAsync(write);
    }

    [Fact]
    public async Task An_impersonation_follows_the_same_rule_and_can_always_leave()
    {
        var blocked = await NewTenantAsync("trial_vencido");
        var imp = await ImpersonateAsync(blocked.Id);

        await imp.GetAsync("/api/categories").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await ExpectBlockedAsync(imp.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }));
        await imp.PostAsync("/api/auth/impersonation/end", null).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        var valid = await NewTenantAsync("admin_active_sem_fim");
        var impValid = await ImpersonateAsync(valid.Id);
        await impValid.PostAsJsonAsync("/api/categories", new { name = Unique("Categoria") }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Fact]
    public async Task Each_block_is_logged_with_tenant_and_route_and_no_personal_data()
    {
        var tenant = await NewTenantAsync("sem_assinatura");
        var client = await tenant.LoginAsync(_factory);
        var id = Guid.NewGuid();

        using var response = await client.PutAsJsonAsync($"/api/categories/{id}", new { name = "Dado Pessoal Qualquer" });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var requestId = response.Headers.GetValues("X-Request-Id").Single();
        var log = _factory.ServerLog.For(requestId);
        log.Should().Contain("blocked").And.Contain(tenant.Id.ToString()).And.Contain("/api/categories/{id:guid}");
        // a mensagem e as propriedades estruturadas: o caminho bruto, com o id, não entra em nenhuma das duas
        foreach (var text in new[] { log, _factory.ServerLog.PropertiesFor(requestId) })
            text.Should().NotContain(tenant.Login).And.NotContain(tenant.Email).And.NotContain(id.ToString()).And.NotContain("Dado Pessoal");
        _factory.ServerLog.PropertiesFor(requestId).Should().Contain("/api/categories/{id:guid}");
    }

    // ── Lista de trials vencidos da plataforma ────────────────────────────────

    [Fact]
    public async Task The_platform_list_shows_an_expired_trial()
    {
        var expired = await NewTenantAsync("trial_vencido");

        var entry = await TrialExpiredEntryAsync(expired.Id);

        entry.Should().NotBeNull("um trial vencido tem de aparecer na lista");
        entry!.Value.GetProperty("expiredReason").GetString().Should().Be("trial");
        entry.Value.GetProperty("expiredDaysAgo").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        entry.Value.GetProperty("trialEndsAt").GetDateTime().Should().BeCloseTo(expired.Rows[0].CurrentPeriodEnd!.Value, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("trial_no_prazo")]
    [InlineData("trial_vencido_com_outro_modulo_valido")]
    [InlineData("admin_active_sem_fim")]
    public async Task The_platform_list_leaves_out_who_still_has_access(string state)
    {
        var tenant = await NewTenantAsync(state);

        (await TrialExpiredEntryAsync(tenant.Id)).Should().BeNull();
    }

    [Fact]
    public async Task The_platform_list_still_shows_an_active_subscription_past_its_period()
    {
        var tenant = await NewTenantAsync("active_periodo_vencido");

        var entry = await TrialExpiredEntryAsync(tenant.Id);

        entry.Should().NotBeNull();
        entry!.Value.GetProperty("expiredReason").GetString().Should().Be("subscription:varejo");
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

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

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    private static ModuleSubscription Canceled(ModuleSubscription s) { s.Cancel(); return s; }

    private static ModuleSubscription Suspended(ModuleSubscription s)
    {
        // o período ainda não acabou: é o status que tira o acesso
        s.SyncFromStripe(SubscriptionStatus.Suspended, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(20), false);
        return s;
    }

    private static ModuleSubscription PastDueSince(ModuleSubscription s, int daysAgo) { s.MarkPastDue(DateTime.UtcNow.AddDays(-daysAgo)); return s; }

    private static ModuleSubscription Trialing(ModuleSubscription s, int daysLeft)
    {
        s.SyncFromStripe(SubscriptionStatus.Trialing, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(daysLeft), false);
        return s;
    }

    private static ModuleSubscription FromStripe(Guid t, string module, PlanType plan, DateTime? end) => ModuleSubscription.CreateFromStripe(
        t, module, $"sub_{Guid.NewGuid():N}", "price_teste", plan, DateTime.UtcNow.AddDays(-40), end);

    private static ModuleSubscription[] RowsFor(string state, Guid t) => state switch
    {
        "admin_active_sem_fim" => [ModuleSubscription.CreateAdminGrant(t, "varejo")],
        "admin_active_fim_futuro" => [ModuleSubscription.CreateAdminGrant(t, "varejo", expiresAt: DateTime.UtcNow.AddDays(30))],
        "admin_active_e_outro_canceled" => [ModuleSubscription.CreateAdminGrant(t, "varejo"), Canceled(ModuleSubscription.CreateAdminGrant(t, "build"))],
        "lifetime" => [FromStripe(t, "restaurante", PlanType.Lifetime, null)],
        "trial_no_prazo" => [ModuleSubscription.CreateTrial(t, "varejo", days: 3)],
        "past_due_na_carencia" => [PastDueSince(FromStripe(t, "restaurante", PlanType.Monthly, DateTime.UtcNow.AddDays(-1)), ModuleSubscription.PastDueGraceDays - 1)],
        "trial_vencido" => [ModuleSubscription.CreateTrial(t, "varejo", days: -1)],
        "trial_vencido_com_outro_modulo_valido" => [ModuleSubscription.CreateTrial(t, "varejo", days: -1), ModuleSubscription.CreateAdminGrant(t, "build")],
        "active_periodo_vencido" => [ModuleSubscription.CreateAdminGrant(t, "varejo", expiresAt: DateTime.UtcNow.AddDays(-1))],
        "past_due_fora_da_carencia" => [PastDueSince(FromStripe(t, "restaurante", PlanType.Monthly, DateTime.UtcNow.AddDays(-1)), ModuleSubscription.PastDueGraceDays + 1)],
        "todos_canceled" => [Canceled(ModuleSubscription.CreateAdminGrant(t, "varejo")), Canceled(ModuleSubscription.CreateAdminGrant(t, "build"))],
        "todos_suspended" => [Suspended(ModuleSubscription.CreateAdminGrant(t, "varejo")), Suspended(ModuleSubscription.CreateAdminGrant(t, "build"))],
        "stripe_cancelada" => [Canceled(FromStripe(t, "restaurante", PlanType.Monthly, DateTime.UtcNow.AddDays(-1)))],
        "sem_assinatura" => [],
        // a regra não olha o plano: os mesmos estados em linhas de concessão
        "admin_trialing_no_prazo" => [Trialing(ModuleSubscription.CreateAdminGrant(t, "varejo"), daysLeft: 3)],
        "admin_trialing_vencido" => [Trialing(ModuleSubscription.CreateAdminGrant(t, "varejo"), daysLeft: -1)],
        "admin_past_due_na_carencia" => [PastDueSince(ModuleSubscription.CreateAdminGrant(t, "varejo"), ModuleSubscription.PastDueGraceDays - 1)],
        "admin_past_due_fora_da_carencia" => [PastDueSince(ModuleSubscription.CreateAdminGrant(t, "varejo"), ModuleSubscription.PastDueGraceDays + 1)],
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private sealed record TestTenant(Guid Id, Guid UserId, string Login, string Email, string Password, ModuleSubscription[] Rows)
    {
        public async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host)
        {
            var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(Login, Password));
            login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
            return client;
        }
    }

    private async Task<TestTenant> NewTenantAsync(string state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tag = Guid.NewGuid().ToString("N")[..12];
        var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var tenant = Tenant.Create($"Acesso {tag}", taxId, $"acesso-{tag}@{TestCredentials.TestDomain}");
        db.Tenants.Add(tenant);
        db.Stores.Add(Store.Create(tenant.Id, $"Loja {tag}", $"loja-{tag}"));
        var login = $"dono-{tag}";
        var email = $"{login}@{TestCredentials.TestDomain}";
        var password = TestCredentials.AdminPassword;
        var user = User.Create(tenantId: tenant.Id, fullName: "Dono Fulano", email: email, login: login,
            passwordHash: hasher.Hash(password), role: UserRole.Diretoria);
        db.Users.Add(user);
        var rows = RowsFor(state, tenant.Id);
        db.ModuleSubscriptions.AddRange(rows);
        await db.SaveChangesAsync();
        return new TestTenant(tenant.Id, user.Id, login, email, password, rows);
    }

    private async Task<HttpClient> PlatformClientAsync()
    {
        var email = $"ops-{Guid.NewGuid():N}"[..14] + "@platform.test";
        var password = "Px!" + Guid.NewGuid().ToString("N")[..20];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var platform = _factory.CreateApiClient();
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return platform;
    }

    private async Task<HttpClient> ImpersonateAsync(Guid tenantId)
    {
        var platform = await PlatformClientAsync();
        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate",
            new { reason = "Suporte: conferir o bloqueio de assinatura" });
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var imp = _factory.CreateApiClient();
        imp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return imp;
    }

    private async Task<JsonElement?> TrialExpiredEntryAsync(Guid tenantId)
    {
        var platform = await PlatformClientAsync();
        using var response = await platform.GetAsync("/api/platform/tenants/trial-expired");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(response));
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in list.EnumerateArray())
            if (item.GetProperty("id").GetGuid() == tenantId) return item;
        return null;
    }

    // ── Rotas reais do host ───────────────────────────────────────────────────

    private sealed record WriteRoute(string Method, string Template, RouteEndpoint Endpoint)
    {
        public string Key => $"{Method} {Template}";
    }

    private IEnumerable<WriteRoute> WriteRoutes()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE"))
                yield return new WriteRoute(method, (endpoint.RoutePattern.RawText ?? "").TrimStart('/'), endpoint);
        }
    }

    private IEnumerable<WriteRoute> AnonymousWrites() => WriteRoutes().Where(r => IsAnonymous(r.Endpoint));

    private static bool IsAnonymous(Endpoint endpoint) => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    /// <summary>Rotas de empresa: a API, menos a plataforma e o contrato da Orken Platform, que têm identidade própria.</summary>
    private static bool IsTenantRoute(string template)
        => template.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
           && !template.StartsWith("api/platform/", StringComparison.OrdinalIgnoreCase)
           && !template.StartsWith("api/orken/v1/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Uma rota [Consumes] só casa com esse tipo de conteúdo: com outro, o roteamento responde 415 e nada é gravado.</summary>
    private static HttpContent BodyFor(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Any(t => t.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) == true
            ? new MultipartFormDataContent()
            : JsonContent.Create(new { });

    private static string PathOf(RoutePattern pattern, Guid id)
    {
        var path = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (var part in segment.Parts)
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => SampleValue(parameter, id),
                    _ => "",
                });
        }
        return path.ToString();
    }

    private static string SampleValue(RoutePatternParameterPart parameter, Guid id)
    {
        var policies = string.Join(",", parameter.ParameterPolicies.Select(p => p.Content));
        if (policies.Contains("guid", StringComparison.OrdinalIgnoreCase)) return id.ToString();
        if (policies.Contains("int", StringComparison.OrdinalIgnoreCase) || policies.Contains("long", StringComparison.OrdinalIgnoreCase)) return "1";
        return "x";
    }

    // ── Webhook assinado como o Stripe assina ─────────────────────────────────

    private static string Sign(string payload, string secret)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant();
        return $"t={t},v1={signature}";
    }

    private static string SubscriptionUpdatedEvent(string subscriptionId, string status)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        return $$$"""
            { "id":"evt_{{{Guid.NewGuid():N}}}","object":"event","api_version":"2025-03-31.basil","created":{{{start}}},"livemode":false,
             "pending_webhooks":1,"request":{ "id":null,"idempotency_key":null} ,"type":"customer.subscription.updated",
             "data":{ "object":{ "id":"{{{subscriptionId}}}","object":"subscription","status":"{{{status}}}","cancel_at_period_end":false,
               "items":{ "object":"list","data":[{ "id":"si_1","object":"subscription_item","current_period_start":{{{start}}},"current_period_end":{{{end}}},
                 "price":{ "id":"price_1","object":"price","recurring":{ "interval":"month"} } } ]} } } }
            """;
    }

    private sealed class FakeBillingProvider : IBillingProvider
    {
        public const string Url = "https://checkout.stripe.test/sessao";

        public Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, string companyName, CancellationToken ct = default)
            => Task.FromResult($"cus_{tenantId:N}");

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
            => Task.FromResult(new CheckoutSessionResult("cs_teste", Url));

        public Task<PortalSessionResult> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => Task.FromResult(new PortalSessionResult("https://portal.stripe.test/sessao"));
    }
}
