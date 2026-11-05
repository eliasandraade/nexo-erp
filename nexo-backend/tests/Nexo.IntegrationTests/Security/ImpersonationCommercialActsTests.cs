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
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O acesso da plataforma a uma empresa (impersonação) serve para ver e ajudar: não para decidir pelo dono. Durante ele,
/// nada do contrato da empresa com o Stripe (checkout, portal) e nenhuma decisão que fica registrada em nome da Diretoria
/// (decisões fiscais seladas, aditivo que muda o valor do contrato com o cliente da empresa). Cada rota bloqueada é testada,
/// e o dono continua podendo. O suporte operacional segue aberto. Cada teste usa uma empresa própria.
/// </summary>
[Collection("Integration")]
public class ImpersonationCommercialActsTests
{
    private readonly TestWebApplicationFactory _factory;

    public ImpersonationCommercialActsTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Stripe ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_impersonation_cannot_open_the_stripe_checkout()
    {
        var stripe = new FakeBillingProvider();
        var host = HostWithStripe(stripe);
        var (tenantId, email) = await NewTenantAsync();
        var imp = On(host, await ImpersonateAsync(tenantId));
        var body = new { moduleKey = "restaurante", billingPeriod = "monthly" };

        await ExpectForbiddenAsync(imp.PostAsJsonAsync("/api/billing/checkout", body));

        stripe.Calls.Should().BeEmpty("nada pode chegar ao Stripe em nome da empresa");
        (await StripeCustomerOfAsync(tenantId)).Should().BeNull("nenhum cliente foi criado no Stripe");

        var owner = On(host, await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword));
        var own = await owner.PostAsJsonAsync("/api/billing/checkout", body);
        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        stripe.Calls.Should().Contain(nameof(IBillingProvider.CreateCheckoutSessionAsync), "o dono continua assinando");
    }

    [Fact]
    public async Task An_impersonation_cannot_open_the_stripe_portal()
    {
        var stripe = new FakeBillingProvider();
        var host = HostWithStripe(stripe);
        var (tenantId, email) = await NewTenantAsync();
        await SetStripeCustomerAsync(tenantId, "cus_teste_impersonacao");
        var imp = On(host, await ImpersonateAsync(tenantId));

        // o portal mostra cartão e faturas, troca o plano e cancela a assinatura
        await ExpectForbiddenAsync(imp.PostAsJsonAsync("/api/billing/portal", new { }));
        stripe.Calls.Should().BeEmpty();

        // ver a situação da assinatura continua liberado: é o que o suporte precisa
        (await imp.GetAsync("/api/billing/subscriptions")).StatusCode.Should().Be(HttpStatusCode.OK);

        var owner = On(host, await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword));
        var own = await owner.PostAsJsonAsync("/api/billing/portal", new { });
        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        stripe.Calls.Should().Contain(nameof(IBillingProvider.CreatePortalSessionAsync));
    }

    // ── Decisões fiscais reservadas à Diretoria ──────────────────────────────

    [Theory]
    [InlineData("approve")]
    [InlineData("execution")]
    public async Task An_impersonation_cannot_seal_a_tax_decision(string act)
    {
        var (tenantId, email) = await NewTenantAsync();
        var imp = await ImpersonateAsync(tenantId);
        var owner = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
        var url = $"/api/tax/opportunities/{Guid.NewGuid()}/{act}";
        object body = act == "approve"
            ? new { acknowledgeHighRisk = true }
            : new { outcome = "Verified", fiscalDocumentReference = "NFE-1", authorizationProtocol = "PROT-1", notes = "conferido" };

        await ExpectForbiddenAsync(imp.PostAsJsonAsync(url, body));

        // a oportunidade não existe: para o dono a rota responde 404, ou seja, ele continua chegando à decisão
        (await owner.PostAsJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_impersonation_cannot_change_the_tax_profile()
    {
        var (tenantId, email) = await NewTenantAsync();
        var imp = await ImpersonateAsync(tenantId);
        var owner = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
        var body = new
        {
            validFrom = "2026-01-01", regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
            reason = "Troca de regime tributário",
        };

        await ExpectForbiddenAsync(imp.PostAsJsonAsync("/api/tax/profile/versions", body));

        var profile = await (await imp.GetAsync("/api/tax/profile")).Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null, "nenhuma versão do perfil foi criada; e a leitura segue liberada");

        var own = await owner.PostAsJsonAsync("/api/tax/profile/versions", body);
        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_impersonation_cannot_attest_tax_compliance()
    {
        var (tenantId, email) = await NewTenantAsync();
        var imp = await ImpersonateAsync(tenantId);
        var owner = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
        var body = new
        {
            status = "Verified", periodFrom = "2026-06-01", periodTo = "2026-06-30", scope = "NF-e de saída do período",
            obligationsChecked = new[] { "Emissão de NF-e" }, evidence = "Relatório de conferência", assessedBy = "Contabilidade",
            assessedAt = (string?)null, notes = (string?)null, limitations = (string?)null, pnctParticipation = false,
        };

        await ExpectForbiddenAsync(imp.PostAsJsonAsync("/api/tax/compliance-assessments", body));

        var assessments = await (await imp.GetAsync("/api/tax/compliance-assessments")).Content.ReadFromJsonAsync<JsonElement>();
        assessments.GetArrayLength().Should().Be(0, "nenhuma conferência foi registrada; e a leitura segue liberada");

        var own = await owner.PostAsJsonAsync("/api/tax/compliance-assessments", body);
        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
    }

    // ── Aditivo: o valor do contrato da empresa com o cliente dela ────────────

    [Theory]
    [InlineData("approve", "Approved")]
    [InlineData("reject", "Rejected")]
    public async Task An_impersonation_cannot_decide_a_change_order(string decision, string decided)
    {
        var (tenantId, email) = await NewTenantAsync("build");
        var owner = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
        var changeOrder = await SentChangeOrderAsync(owner);
        var imp = await ImpersonateAsync(tenantId);

        await ExpectForbiddenAsync(imp.PostAsync($"/api/v1/build/change-orders/{changeOrder}/{decision}", null));

        var read = await imp.GetAsync($"/api/v1/build/change-orders/{changeOrder}");
        read.StatusCode.Should().Be(HttpStatusCode.OK, "a leitura segue liberada");
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Sent", "o aditivo continua à espera da decisão");

        var own = await owner.PostAsync($"/api/v1/build/change-orders/{changeOrder}/{decision}", null);
        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be(decided);
    }

    // ── O que continua aberto ────────────────────────────────────────────────

    /// <summary>O acesso existe para ajudar: acertar cadastro, configuração e lançamentos que o cliente pediu. Nada disto decide pelo dono.</summary>
    [Fact]
    public async Task Operational_support_stays_open_during_an_impersonation()
    {
        var (tenantId, _) = await NewTenantAsync("build");
        var imp = await ImpersonateAsync(tenantId);

        (await imp.PostAsJsonAsync("/api/products", new
        {
            code = $"SUP-{Guid.NewGuid():N}"[..12], name = "Produto do suporte", unit = "Un", salePrice = 10m, costPrice = 5m, trackStock = false,
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await imp.PostAsJsonAsync("/api/financial/accounts", new
        {
            code = $"S{Guid.NewGuid():N}"[..8], name = "Conta do suporte", accountType = "Cash",
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await imp.PutAsJsonAsync("/api/settings", new
        {
            inventory = new { enableLowStockAlerts = true, enableZeroStockAlerts = true },
            pos = new { allowValueDiscount = true, allowPercentDiscount = true, requireManagerAuth = false, maxDiscountPercent = 10m },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await imp.PutAsJsonAsync("/api/fiscal/profile", new
        {
            legalName = "Empresa Suporte Ltda", tradeName = "Loja", cnpj = "11.222.333/0001-81", stateRegistration = "ISENTO",
            municipalRegistration = (string?)null, taxRegime = "LucroPresumido", street = "Rua", number = "1", complement = (string?)null,
            district = "Centro", cityName = "São Paulo", cityIbgeCode = "3550308", state = "SP", zipCode = "01001000",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // preparar e enviar um aditivo não decide nada: a decisão é do dono ou do cliente dele
        var changeOrder = await SentChangeOrderAsync(imp);
        (await imp.PostAsync($"/api/v1/build/change-orders/{changeOrder}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task ExpectForbiddenAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{response.RequestMessage?.RequestUri}: {text}");
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().Should().Be("impersonation_forbidden");
    }

    private static async Task<Guid> SentChangeOrderAsync(HttpClient client)
    {
        var project = await client.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra do teste", clientName = "Cliente", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        });
        project.StatusCode.Should().Be(HttpStatusCode.Created, await project.Content.ReadAsStringAsync());
        var projectId = (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/change-orders", new
        {
            title = "Fundação mais profunda", description = "Solo mais fraco.", reason = "SiteCondition", scheduleImpactDays = 0,
            items = new object[] { new { name = "Concreto extra", kind = "Material", quantity = 5m, unit = "m³", unitCost = 1000m } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sent = await client.PostAsync($"/api/v1/build/change-orders/{id}/send", null);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>O host da suíte com o Stripe ligado e um provedor falso: nada sai do teste, e dá para ver o que teria saído.</summary>
    private WebApplicationFactory<Program> HostWithStripe(FakeBillingProvider stripe) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Integrations:Features:StripeEnabled", "true");
        b.UseSetting("Integrations:Stripe:PriceIds:restaurante_monthly", "price_teste");
        b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBillingProvider>();
            services.AddSingleton<IBillingProvider>(stripe);
        });
    });

    /// <summary>Um cliente do outro host com a mesma sessão (mesma chave de assinatura dos tokens).</summary>
    private static HttpClient On(WebApplicationFactory<Program> host, HttpClient session)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = session.DefaultRequestHeaders.Authorization;
        return client;
    }

    private sealed class FakeBillingProvider : IBillingProvider
    {
        public List<string> Calls { get; } = [];

        public Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, string companyName, CancellationToken ct = default)
        {
            Calls.Add(nameof(GetOrCreateCustomerAsync));
            return Task.FromResult("cus_teste");
        }

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
        {
            Calls.Add(nameof(CreateCheckoutSessionAsync));
            return Task.FromResult(new CheckoutSessionResult("cs_teste", "https://checkout.stripe.test/cs_teste"));
        }

        public Task<PortalSessionResult> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
        {
            Calls.Add(nameof(CreatePortalSessionAsync));
            return Task.FromResult(new PortalSessionResult("https://billing.stripe.test/portal"));
        }
    }

    private async Task<string?> StripeCustomerOfAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.StripeCustomerId).SingleAsync();
    }

    private async Task SetStripeCustomerAsync(Guid tenantId, string customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId)).SetStripeCustomerId(customerId);
        await db.SaveChangesAsync();
    }

    /// <summary>Uma empresa nova (cadastro) com o dono ativo e os módulos pedidos.</summary>
    private async Task<(Guid TenantId, string Email)> NewTenantAsync(params string[] modules)
    {
        var email = $"com-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Empresa Atos Comerciais", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
        user.Activate();
        foreach (var module in modules)
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(user.TenantId, module));
        await db.SaveChangesAsync();
        return (user.TenantId, email);
    }

    private async Task<HttpClient> ImpersonateAsync(Guid tenantId)
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

        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate",
            new { reason = "Suporte: conferir cadastro da empresa" });
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var imp = _factory.CreateApiClient();
        imp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return imp;
    }
}
