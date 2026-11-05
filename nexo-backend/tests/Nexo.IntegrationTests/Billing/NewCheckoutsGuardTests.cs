using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Serilog.Core;
using Serilog.Events;

namespace Nexo.IntegrationTests.Billing;

/// <summary>
/// O bloqueio de novos checkouts (Billing:NewCheckoutsEnabled): falha fechado, responde antes de qualquer leitura ou chamada
/// ao Stripe e não encosta no que já existe (webhook, portal, listagem, renovação). Tudo pelo HTTP real, com PostgreSQL de
/// verdade e um Stripe simulado que conta as chamadas. Cada teste usa empresa e assinatura próprias.
/// </summary>
[Collection("Integration")]
public class NewCheckoutsGuardTests
{
    private const string Setting = "Billing:NewCheckoutsEnabled";
    private const string Code = "checkout_disabled";
    private const string WebhookSecret = "whsec_integration_tests_only";
    private const string CheckoutRoute = "/api/billing/checkout";
    private const string AvailabilityRoute = "/api/billing/checkout-availability";
    private const string ModuleGateRoute = "/api/v1/build/budget-templates";

    private static readonly object ValidPlan = new { moduleKey = "build", billingPeriod = "monthly" };

    private readonly TestWebApplicationFactory _factory;

    public NewCheckoutsGuardTests(TestWebApplicationFactory factory) => _factory = factory;

    private FakeStripeSubscriptionFetcher StripeState => _factory.StripeSubscriptions;

    // ── Falha fechado: só o literal true libera ───────────────────────────────

    [Fact]
    public async Task Promoting_with_nothing_set_blocks_new_checkouts()
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe);
        ConfiguredValue(host).Should().Be("False", "o padrão vem do appsettings.json");
        var session = await NewTenantAsync(host);

        await ExpectCheckoutDisabledAsync(session.Client);

        stripe.TotalCalls.Should().Be(0);
        (await StripeCustomerOfAsync(host, session.TenantId)).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("talvez")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("sim")]
    public async Task Anything_but_the_literal_true_keeps_checkouts_blocked(string? value)
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe, value: value, setValue: true);
        ConfiguredValue(host).Should().Be(value, "o teste precisa exercitar esse valor, não o do appsettings.json");
        var session = await NewTenantAsync(host);

        await ExpectCheckoutDisabledAsync(session.Client);

        stripe.TotalCalls.Should().Be(0, "nada pode chegar ao Stripe");
        (await StripeCustomerOfAsync(host, session.TenantId)).Should().BeNull("nenhum cliente foi criado no Stripe");
    }

    [Fact]
    public async Task A_blocked_checkout_keeps_the_stripe_customer_the_tenant_already_has()
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe);
        var session = await NewTenantAsync(host, stripeCustomerId: "cus_ja_existente");

        await ExpectCheckoutDisabledAsync(session.Client);

        stripe.TotalCalls.Should().Be(0);
        (await StripeCustomerOfAsync(host, session.TenantId)).Should().Be("cus_ja_existente");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]   // é como o valor booleano true de um arquivo JSON chega à configuração
    public async Task The_literal_true_reaches_the_provider_as_before(string value)
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe, value: value, setValue: true);
        var session = await NewTenantAsync(host);

        using var response = await session.Client.PostAsJsonAsync(CheckoutRoute, ValidPlan);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutUrl").GetString().Should().Be(CountingBillingProvider.CheckoutUrl);
        stripe.Customers.Should().Be(1);
        stripe.Checkouts.Should().Be(1);
    }

    // ── A ordem: Stripe desligado, depois o bloqueio, depois todo o resto ─────

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("false")]
    public async Task With_stripe_disabled_checkout_stays_404_whatever_the_setting_says(string? value)
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe, value: value, setValue: true, stripeEnabled: false);
        var session = await NewTenantAsync(host);

        using var response = await session.Client.PostAsJsonAsync(CheckoutRoute, ValidPlan);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadAsStringAsync()).Should().Contain("Billing não está habilitado").And.NotContain(Code);
        stripe.TotalCalls.Should().Be(0);
        (await session.Client.GetAsync(AvailabilityRoute)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_block_answers_before_validating_the_plan()
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe);
        var session = await NewTenantAsync(host);

        await ExpectCheckoutDisabledAsync(session.Client, new { });
        await ExpectCheckoutDisabledAsync(session.Client, new { moduleKey = "modulo_sem_preco", billingPeriod = "monthly" });

        stripe.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task The_block_answers_before_looking_at_the_current_subscription()
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe);
        var session = await NewTenantAsync(host);
        await PaidSubscriptionAsync(host, session.TenantId);

        // liberado, este pedido responderia 409 ("já tem uma assinatura"): o bloqueio não depende da assinatura
        await ExpectCheckoutDisabledAsync(session.Client);

        stripe.TotalCalls.Should().Be(0);
    }

    // ── Com o bloqueio ativo, os webhooks seguem processados e registrados ────

    [Fact]
    public async Task While_blocked_a_completed_checkout_event_still_creates_the_row_and_is_recorded()
    {
        var (host, stripe, session) = await BlockedAsync();
        var subId = NewSubscriptionId();
        StripeState.Set(subId, "active");
        var body = CheckoutEvent(session.TenantId, "build", subId);

        (await DeliverAsync(host, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(host, EventId(body))).Should().Be(1);
        (await RowAsync(host, subId)).Status.Should().Be(SubscriptionStatus.Active, "um checkout aberto antes do bloqueio ainda precisa dar acesso");
        (await HasAccessAsync(host, session.TenantId)).Should().BeTrue();
        stripe.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task While_blocked_a_subscription_updated_event_is_still_applied_and_recorded()
    {
        var (host, stripe, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        StripeState.Set(subId, "active", cancelAtPeriodEnd: true);
        var body = SubscriptionEvent("customer.subscription.updated", subId, "active");

        (await DeliverAsync(host, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(host, EventId(body))).Should().Be(1);
        (await RowAsync(host, subId)).CancelAtPeriodEnd.Should().BeTrue("o estado vem da releitura no Stripe");
        stripe.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task While_blocked_a_subscription_deleted_event_still_cancels_and_is_recorded()
    {
        var (host, stripe, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        (await HasAccessAsync(host, session.TenantId)).Should().BeTrue();
        StripeState.Set(subId, "canceled");
        var body = SubscriptionEvent("customer.subscription.deleted", subId, "canceled");

        (await DeliverAsync(host, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(host, EventId(body))).Should().Be(1);
        (await RowAsync(host, subId)).Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(host, session.TenantId)).Should().BeFalse();
        stripe.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task While_blocked_a_failed_payment_event_still_marks_past_due_and_is_recorded()
    {
        var (host, stripe, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        StripeState.Set(subId, "past_due");
        var body = InvoicePaymentFailedEvent(subId);

        (await DeliverAsync(host, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(host, EventId(body))).Should().Be(1);
        var row = await RowAsync(host, subId);
        row.Status.Should().Be(SubscriptionStatus.PastDue);
        row.PastDueSince.Should().NotBeNull();
        (await HasAccessAsync(host, session.TenantId)).Should().BeTrue("dentro da carência de 7 dias");
        stripe.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task While_blocked_a_bad_signature_is_still_refused()
    {
        var (host, _, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        var body = SubscriptionEvent("customer.subscription.deleted", subId, "canceled");

        (await DeliverAsync(host, body, Sign(body, secret: "whsec_outro_segredo"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ProcessedCountAsync(host, EventId(body))).Should().Be(0);
        (await RowAsync(host, subId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    // ── Com o bloqueio ativo, a assinatura viva não muda ──────────────────────

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    public async Task While_blocked_a_live_subscription_keeps_state_access_portal_and_listing(string stripeStatus, SubscriptionStatus status)
    {
        var (host, stripe, session) = await BlockedAsync(stripeCustomerId: $"cus_{Guid.NewGuid():N}");
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        StripeState.Set(subId, stripeStatus);
        await DeliverAsync(host, SubscriptionEvent("customer.subscription.updated", subId, stripeStatus));
        var before = await RowAsync(host, subId);
        before.Status.Should().Be(status);

        await ExpectCheckoutDisabledAsync(session.Client);

        var after = await RowAsync(host, subId);
        after.Status.Should().Be(before.Status);
        after.CurrentPeriodStart.Should().Be(before.CurrentPeriodStart);
        after.CurrentPeriodEnd.Should().Be(before.CurrentPeriodEnd);
        after.CancelAtPeriodEnd.Should().Be(before.CancelAtPeriodEnd);
        after.PastDueSince.Should().Be(before.PastDueSince);
        after.StripeSubscriptionId.Should().Be(subId);
        after.StripePriceId.Should().Be(before.StripePriceId);

        (await HasAccessAsync(host, session.TenantId)).Should().BeTrue();
        await session.Client.GetAsync(ModuleGateRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var portal = await session.Client.PostAsJsonAsync("/api/billing/portal", new { });
        portal.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(portal));
        (await portal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("portalUrl").GetString().Should().Be(CountingBillingProvider.PortalUrl);
        stripe.Portals.Should().Be(1);
        stripe.Customers.Should().Be(0);
        stripe.Checkouts.Should().Be(0);

        using var listing = await session.Client.GetAsync("/api/billing/subscriptions");
        listing.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(listing));
        var listed = (await listing.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
        listed.GetProperty("moduleKey").GetString().Should().Be("build");
        listed.GetProperty("status").GetString().Should().Be(status.ToString());
        listed.GetProperty("stripeSubscriptionId").GetString().Should().Be(subId);
    }

    [Fact]
    public async Task While_blocked_a_renewal_event_moves_the_period_forward_and_keeps_access()
    {
        var (host, _, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        var periodEnd = (await RowAsync(host, subId)).CurrentPeriodEnd!.Value;
        var renewedUntil = periodEnd.AddDays(30);
        StripeState.Set(subId, "active", periodStart: periodEnd, periodEnd: renewedUntil);
        var body = SubscriptionEvent("customer.subscription.updated", subId, "active");

        (await DeliverAsync(host, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(host, subId);
        row.Status.Should().Be(SubscriptionStatus.Active);
        row.CurrentPeriodEnd.Should().BeCloseTo(renewedUntil, TimeSpan.FromSeconds(1));
        (await ProcessedCountAsync(host, EventId(body))).Should().Be(1);
        (await HasAccessAsync(host, session.TenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task While_blocked_a_past_due_row_pays_and_recovers_by_event()
    {
        var (host, _, session) = await BlockedAsync();
        var subId = await PaidSubscriptionAsync(host, session.TenantId);
        StripeState.Set(subId, "past_due");
        await DeliverAsync(host, InvoicePaymentFailedEvent(subId));
        StripeState.Set(subId, "active", periodEnd: DateTime.UtcNow.AddDays(30));

        (await DeliverAsync(host, SubscriptionEvent("customer.subscription.updated", subId, "active"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(host, subId);
        row.Status.Should().Be(SubscriptionStatus.Active);
        row.PastDueSince.Should().BeNull();
    }

    // ── A leitura que a tela usa ──────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("talvez", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task The_screen_reads_the_same_switch_the_checkout_obeys(string? value, bool open)
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe, value: value, setValue: true);
        var session = await NewTenantAsync(host);

        using var response = await session.Client.GetAsync(AvailabilityRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newCheckoutsEnabled").GetBoolean().Should().Be(open);
        stripe.TotalCalls.Should().Be(0, "ler o interruptor não chama o Stripe");
    }

    [Fact]
    public async Task The_availability_read_is_for_management_only()
    {
        var host = HostWith(new CountingBillingProvider());
        var seller = await NewTenantAsync(host, role: UserRole.Vendedor);

        (await seller.Client.GetAsync(AvailabilityRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.CreateClient().GetAsync(AvailabilityRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_blocked_checkout_is_logged_with_tenant_and_reason_and_no_personal_data()
    {
        var host = HostWith(new CountingBillingProvider());
        var session = await NewTenantAsync(host);
        const string typedByTheUser = "Dado Pessoal Qualquer";

        using var response = await session.Client.PostAsJsonAsync(CheckoutRoute, new { moduleKey = typedByTheUser, billingPeriod = "monthly" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await _factory.ServerLog.DescribeAsync(response));
        var requestId = response.Headers.GetValues("X-Request-Id").Single();
        var message = _factory.ServerLog.For(requestId);
        var properties = _factory.ServerLog.PropertiesFor(requestId);
        message.Should().Contain("blocked").And.Contain(session.TenantId.ToString()).And.Contain(Code);
        properties.Should().Contain(session.TenantId.ToString()).And.Contain(Code);
        foreach (var text in new[] { message, properties })
            text.Should().NotContain(session.Login).And.NotContain(session.Email).And.NotContain(session.CompanyName).And.NotContain(typedByTheUser);
    }

    [Theory]
    [InlineData(null, "blocked")]
    [InlineData("talvez", "blocked")]
    [InlineData("true", "open")]
    public void The_startup_log_says_whether_new_checkouts_are_open(string? value, string state)
    {
        var sink = new MessagesSink();
        var host = HostWith(new CountingBillingProvider(), value: value, setValue: true, sink: sink);

        _ = host.Services;   // sobe o host

        sink.Messages.Should().ContainSingle(m => m.Contains("New checkouts")).Which.Should().Contain(state);
    }

    // ── Host ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// O host da suíte com o Stripe simulado. Sem <paramref name="setValue"/>, vale o appsettings.json. Com ele, a variável
    /// passa a ter exatamente <paramref name="value"/> (null é ausente): UseSetting não serve aqui, porque valor vazio
    /// cairia no valor do appsettings.json.
    /// </summary>
    private WebApplicationFactory<Program> HostWith(
        CountingBillingProvider stripe, string? value = null, bool setValue = false, bool stripeEnabled = true, ILogEventSink? sink = null)
        => _factory.WithWebHostBuilder(b =>
        {
            if (stripeEnabled) b.UseSetting("Integrations:Features:StripeEnabled", "true");
            b.UseSetting("Integrations:Stripe:PriceIds:build_monthly", "price_teste");
            if (setValue)
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { [Setting] = value }));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IBillingProvider>();
                s.AddSingleton<IBillingProvider>(stripe);
                if (sink is not null) s.AddSingleton(sink);
            });
        });

    private static string? ConfiguredValue(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IConfiguration>()[Setting];

    /// <summary>Um host com o bloqueio ativo e a prova disso: a empresa do teste recebe a recusa pelo HTTP real.</summary>
    private async Task<(WebApplicationFactory<Program> Host, CountingBillingProvider Stripe, TenantSession Session)> BlockedAsync(string? stripeCustomerId = null)
    {
        var stripe = new CountingBillingProvider();
        var host = HostWith(stripe);
        var session = await NewTenantAsync(host, stripeCustomerId: stripeCustomerId);
        await ExpectCheckoutDisabledAsync(session.Client);
        return (host, stripe, session);
    }

    private async Task ExpectCheckoutDisabledAsync(HttpClient client, object? body = null)
    {
        using var response = await client.PostAsJsonAsync(CheckoutRoute, body ?? ValidPlan);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await _factory.ServerLog.DescribeAsync(response));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be(Code);
        json.GetProperty("requestId").GetString().Should().NotBeNullOrWhiteSpace();
        var error = json.GetProperty("error").GetString();
        error.Should().Contain("suporte", "a mensagem indica o caminho");
        error.Should().NotContain("Assine").And.NotContain("R$");
    }

    // ── Estado ────────────────────────────────────────────────────────────────

    private sealed record TenantSession(HttpClient Client, Guid TenantId, string Login, string Email, string CompanyName);

    private static async Task<TenantSession> NewTenantAsync(
        WebApplicationFactory<Program> host, UserRole role = UserRole.Diretoria, string? stripeCustomerId = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var login = $"dono-{tag}";
        var email = $"{login}@{TestCredentials.TestDomain}";
        var companyName = $"Empresa {tag}";
        Guid tenantId;

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
            var tenant = Tenant.Create(companyName, taxId, $"empresa-{tag}@{TestCredentials.TestDomain}");
            if (stripeCustomerId is not null) tenant.SetStripeCustomerId(stripeCustomerId);
            db.Tenants.Add(tenant);
            db.Stores.Add(Store.Create(tenant.Id, $"Loja {tag}", $"loja-{tag}"));
            db.Users.Add(User.Create(tenantId: tenant.Id, fullName: "Dono Fulano", email: email, login: login,
                passwordHash: scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(TestCredentials.AdminPassword), role: role));
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var signedIn = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(login, TestCredentials.AdminPassword));
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return new TenantSession(client, tenantId, login, email, companyName);
    }

    private static string NewSubscriptionId() => $"sub_{Guid.NewGuid():N}";

    private async Task<string> PaidSubscriptionAsync(WebApplicationFactory<Program> host, Guid tenantId)
    {
        var subId = NewSubscriptionId();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.ModuleSubscriptions.Add(ModuleSubscription.CreateFromStripe(tenantId, "build", subId, "price_monthly", PlanType.Monthly,
            DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(25)));
        await db.SaveChangesAsync();
        StripeState.Set(subId, "active");
        return subId;
    }

    private static async Task<ModuleSubscription> RowAsync(WebApplicationFactory<Program> host, string subId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.ModuleSubscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.StripeSubscriptionId == subId);
    }

    private static async Task<string?> StripeCustomerOfAsync(WebApplicationFactory<Program> host, Guid tenantId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.StripeCustomerId).SingleAsync();
    }

    private static async Task<int> ProcessedCountAsync(WebApplicationFactory<Program> host, string eventId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.StripeProcessedEvents.AsNoTracking().CountAsync(e => e.StripeEventId == eventId);
    }

    /// <summary>A mesma checagem dos gates, com o cache do host.</summary>
    private static async Task<bool> HasAccessAsync(WebApplicationFactory<Program> host, Guid tenantId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IModuleAccessService>().HasActiveModuleAsync(tenantId, "build");
    }

    // ── Eventos assinados como o Stripe assina ────────────────────────────────

    private static async Task<HttpResponseMessage> DeliverAsync(WebApplicationFactory<Program> host, string body, string? signature = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature ?? Sign(body));
        return await host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).SendAsync(request);
    }

    private static string Sign(string payload, string secret = WebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    private static string Event(string type, string dataObject) => $$"""
        { "id":"evt_{{Guid.NewGuid():N}}","object":"event","api_version":"2025-03-31.basil","created":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},"livemode":false,
          "pending_webhooks":1,"request":{ "id":null,"idempotency_key":null },"type":"{{type}}",
          "data":{ "object":{{dataObject}} } }
        """;

    private static string SubscriptionEvent(string type, string subId, string status)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds();
        var end = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        return Event(type, $$"""
            { "id":"{{subId}}","object":"subscription","status":"{{status}}","cancel_at_period_end":false,
              "items":{ "object":"list","data":[{ "id":"si_1","object":"subscription_item","current_period_start":{{start}},"current_period_end":{{end}},
                "price":{ "id":"price_do_evento","object":"price","recurring":{ "interval":"month" } } }] } }
            """);
    }

    private static string InvoicePaymentFailedEvent(string subId) => Event("invoice.payment_failed", $$"""
        { "id":"in_{{Guid.NewGuid():N}}","object":"invoice",
          "parent":{ "type":"subscription_details","subscription_details":{ "subscription":"{{subId}}" } } }
        """);

    private static string CheckoutEvent(Guid tenantId, string moduleKey, string subId) => Event("checkout.session.completed", $$"""
        { "id":"cs_{{Guid.NewGuid():N}}","object":"checkout.session","customer":"cus_{{Guid.NewGuid():N}}","subscription":"{{subId}}",
          "metadata":{ "tenantId":"{{tenantId}}","moduleKey":"{{moduleKey}}" } }
        """);

    private static string EventId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    // ── Dublês ────────────────────────────────────────────────────────────────

    /// <summary>O caminho de cobrança do host, contado: dá para ver o que teria saído para o Stripe.</summary>
    private sealed class CountingBillingProvider : IBillingProvider
    {
        public const string CheckoutUrl = "https://checkout.stripe.test/sessao";
        public const string PortalUrl = "https://billing.stripe.test/portal";

        private int _customers;
        private int _checkouts;
        private int _portals;

        public int Customers => Volatile.Read(ref _customers);
        public int Checkouts => Volatile.Read(ref _checkouts);
        public int Portals => Volatile.Read(ref _portals);
        public int TotalCalls => Customers + Checkouts + Portals;

        public Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, string companyName, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _customers);
            return Task.FromResult($"cus_{tenantId:N}");
        }

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _checkouts);
            return Task.FromResult(new CheckoutSessionResult("cs_teste", CheckoutUrl));
        }

        public Task<PortalSessionResult> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _portals);
            return Task.FromResult(new PortalSessionResult(PortalUrl));
        }
    }

    /// <summary>Todas as mensagens de um host. O ServerLog da suíte só guarda avisos e erros, e a linha da subida é informativa.</summary>
    private sealed class MessagesSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IEnumerable<string> Messages => _messages;

        public void Emit(LogEvent logEvent) => _messages.Enqueue(logEvent.RenderMessage());
    }
}
