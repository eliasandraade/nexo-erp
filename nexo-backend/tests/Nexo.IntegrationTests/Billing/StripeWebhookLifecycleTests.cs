using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Billing;

/// <summary>
/// Ciclo de vida da assinatura pelo webhook real: corpo assinado como o Stripe assina, POST em api/billing/webhook, banco
/// PostgreSQL de verdade e um Stripe simulado no container. Nenhum teste sai do host. O estado "atual no Stripe" é o do
/// fake, e o corpo do evento diz outra coisa quando o teste é sobre eventos fora de ordem.
/// </summary>
[Collection("Integration")]
public class StripeWebhookLifecycleTests
{
    private const string WebhookSecret = "whsec_integration_tests_only";

    private static WebApplicationFactory<Program>? _host;

    private readonly TestWebApplicationFactory _factory;

    public StripeWebhookLifecycleTests(TestWebApplicationFactory factory) => _factory = factory;

    private FakeStripeSubscriptionFetcher Stripe => _factory.StripeSubscriptions;

    /// <summary>Um só host para a classe: cada teste usa empresa e assinatura próprias. O factory pai o descarta no fim.</summary>
    private WebApplicationFactory<Program> Host => _host ??= _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Integrations:Features:StripeEnabled", "true");
        b.UseSetting("Integrations:Stripe:WebhookSecret", WebhookSecret);
        b.UseSetting("Integrations:Stripe:SecretKey", "sk_test_integration_tests_only");
        b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IBillingProvider>();
            s.AddSingleton<IBillingProvider, NoStripeBillingProvider>();
        });
    });

    // ── Eventos fora de ordem ─────────────────────────────────────────────────

    [Fact]
    public async Task A_stale_updated_event_does_not_mark_a_paid_subscription_as_past_due()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();

        var response = await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "past_due", stalePeriod: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.Active, "o Stripe diz que está paga; o corpo do evento é antigo");
        row.PastDueSince.Should().BeNull();
        (await HasAccessAsync(tenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_stale_updated_event_does_not_reopen_a_canceled_subscription()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "canceled");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active", stalePeriod: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_late_payment_failed_does_not_mark_a_paid_subscription_as_past_due()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();

        var response = await DeliverAsync(InvoicePaymentFailedEvent(subId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.Active, "a fatura falhou antes, mas a assinatura já está paga");
        row.PastDueSince.Should().BeNull();
        (await HasAccessAsync(tenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_payment_failure_that_is_still_current_marks_past_due_and_keeps_the_grace()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "past_due");

        (await DeliverAsync(InvoicePaymentFailedEvent(subId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.PastDue);
        row.PastDueSince.Should().NotBeNull();
        (await HasAccessAsync(tenantId)).Should().BeTrue("dentro da carência de 7 dias");
    }

    [Fact]
    public async Task When_stripe_cannot_be_read_the_event_answers_500_is_not_recorded_and_the_redelivery_applies()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        var body = SubscriptionEvent("customer.subscription.updated", subId, status: "past_due");
        Stripe.Fail(subId);

        var failed = await DeliverAsync(body);

        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "o Stripe precisa reenviar");
        (await ProcessedCountAsync(EventId(body))).Should().Be(0);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);

        Stripe.Set(subId, "past_due");
        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task A_payment_failed_that_cannot_read_stripe_answers_500_and_is_not_recorded()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        var body = InvoicePaymentFailedEvent(subId);
        Stripe.Fail(subId);

        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await ProcessedCountAsync(EventId(body))).Should().Be(0);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_failed_checkout_read_leaves_nothing_behind_and_the_redelivery_creates_the_row()
    {
        var tenantId = await NewTenantAsync();
        var subId = NewSubscriptionId();
        var body = CheckoutEvent(tenantId, "build", subId);
        Stripe.Fail(subId);

        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await ProcessedCountAsync(EventId(body))).Should().Be(0);
        (await RowOrNullAsync(tenantId, "build")).Should().BeNull();

        Stripe.Set(subId, "active");
        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
        (await HasAccessAsync(tenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task An_event_for_a_subscription_with_no_local_row_is_accepted_without_reading_stripe()
    {
        var subId = NewSubscriptionId();
        foreach (var body in new[]
                 {
                     SubscriptionEvent("customer.subscription.updated", subId, status: "active"),
                     SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"),
                     InvoicePaymentFailedEvent(subId),
                 })
        {
            (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        }

        Stripe.Reads(subId).Should().Be(0);
    }

    [Fact]
    public async Task A_plan_change_in_the_portal_updates_price_and_plan_of_the_row()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "active", priceId: "price_annual", interval: "year", periodEnd: DateTime.UtcNow.AddDays(360));

        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(subId);
        row.StripePriceId.Should().Be("price_annual", "o MRR e a tela leem o plano da linha");
        row.PlanType.Should().Be(PlanType.Annual);
        row.CurrentPeriodEnd.Should().BeAfter(DateTime.UtcNow.AddDays(300));
    }

    // ── Revogação e renovação pela plataforma ─────────────────────────────────

    [Theory]
    [InlineData("active")]
    [InlineData("past_due")]
    [InlineData("paused")]
    public async Task Revoking_a_row_with_a_live_stripe_subscription_answers_409_and_changes_nothing(string stripeStatus)
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, stripeStatus);
        await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: stripeStatus));
        var before = await RowAsync(subId);
        var platform = await PlatformClientAsync();

        var response = await platform.DeleteAsync($"/api/platform/tenants/{tenantId}/modules/build");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ErrorOfAsync(response)).Should().Contain("Stripe").And.Contain("Cancele");
        var after = await RowAsync(subId);
        after.Status.Should().Be(before.Status, "a cobrança continua: a linha não pode dizer outra coisa");
        after.CanceledAt.Should().BeNull();
        (await ModuleEventCountAsync(tenantId, "revoked")).Should().Be(0);
    }

    [Fact]
    public async Task Revoking_a_grant_without_stripe_keeps_working()
    {
        var tenantId = await NewTenantAsync();
        await AddRowAsync(ModuleSubscription.CreateAdminGrant(tenantId, "build", notes: "cortesia"));
        (await HasAccessAsync(tenantId)).Should().BeTrue();
        var platform = await PlatformClientAsync();

        var response = await platform.DeleteAsync($"/api/platform/tenants/{tenantId}/modules/build");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await RowOrNullAsync(tenantId, "build"))!.Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_a_row_whose_stripe_subscription_already_ended_stays_allowed()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "canceled");
        await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"));
        var platform = await PlatformClientAsync();

        var response = await platform.DeleteAsync($"/api/platform/tenants/{tenantId}/modules/build");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Renewing_a_row_with_a_live_stripe_subscription_answers_409_and_changes_nothing()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "past_due");
        await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "past_due"));
        var platform = await PlatformClientAsync();

        var response = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/modules",
            new { moduleKey = "build", expiresAt = DateTime.UtcNow.AddYears(1), notes = "tentativa" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ErrorOfAsync(response)).Should().Contain("Stripe");
        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.PastDue, "renovar aqui daria acesso sem nenhuma cobrança real");
        row.PlanType.Should().Be(PlanType.Monthly);
        (await ModuleEventCountAsync(tenantId, "renewed")).Should().Be(0);
    }

    [Fact]
    public async Task Granting_over_an_ended_stripe_row_becomes_a_real_grant_that_no_late_event_or_checkout_mistakes_for_paid()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "canceled");
        await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"));
        var platform = await PlatformClientAsync();
        var expiresAt = DateTime.UtcNow.AddMonths(3);

        var response = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/modules",
            new { moduleKey = "build", expiresAt, notes = "cortesia para ex-cliente" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var row = (await RowOrNullAsync(tenantId, "build"))!;
        row.PlanType.Should().Be(PlanType.AdminGrant, "o MRR não pode contar como pagante");
        row.StripeSubscriptionId.Should().BeNull();
        row.StripePriceId.Should().BeNull();
        row.IsLiveInStripe.Should().BeFalse("o checkout não pode tratar como assinatura viva");
        row.Status.Should().Be(SubscriptionStatus.Active);
        row.CanceledAt.Should().BeNull();
        row.CurrentPeriodEnd.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(5));
        row.GrantedById.Should().NotBeNull();
        (await HasAccessAsync(tenantId)).Should().BeTrue();

        // um evento atrasado da assinatura encerrada não acha mais a linha
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowOrNullAsync(tenantId, "build"))!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Renewing_a_grant_without_stripe_keeps_working()
    {
        var tenantId = await NewTenantAsync();
        await AddRowAsync(ModuleSubscription.CreateAdminGrant(tenantId, "build", expiresAt: DateTime.UtcNow.AddDays(1)));
        var platform = await PlatformClientAsync();
        var expiresAt = DateTime.UtcNow.AddYears(1);

        var response = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/modules",
            new { moduleKey = "build", expiresAt, notes = "renovação" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var row = (await RowOrNullAsync(tenantId, "build"))!;
        row.PlanType.Should().Be(PlanType.AdminGrant);
        row.CurrentPeriodEnd.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(5));
    }

    // ── Cancelamento e inadimplência ──────────────────────────────────────────

    [Fact]
    public async Task A_deleted_subscription_cancels_and_closes_access_at_once()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        (await HasAccessAsync(tenantId)).Should().BeTrue("o acesso fica em cache antes do evento");
        Stripe.Set(subId, "canceled");

        (await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.Canceled);
        row.CanceledAt.Should().NotBeNull();
        (await HasAccessAsync(tenantId)).Should().BeFalse("o cache foi invalidado");
    }

    [Fact]
    public async Task A_canceled_subscription_closes_the_writes_at_the_real_module_gate_at_once()
    {
        var (client, tenantId) = await TenantUserAsync();
        var subId = NewSubscriptionId();
        await AddRowAsync(ModuleSubscription.CreateFromStripe(tenantId, "build", subId, "price_monthly", PlanType.Monthly,
            DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(25)));
        Stripe.Set(subId, "active");
        var open = await client.GetAsync("/api/v1/build/budget-templates");
        open.StatusCode.Should().Be(HttpStatusCode.OK, await open.Content.ReadAsStringAsync());

        Stripe.Set(subId, "canceled");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // somente leitura: a consulta segue, e a escrita para na hora
        (await client.GetAsync("/api/v1/build/budget-templates")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/build/budget-templates", new { })).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Cancel_at_period_end_keeps_access_until_the_deleted_event_closes_it()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "active", cancelAtPeriodEnd: true);

        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active", cancelAtPeriodEnd: true))).StatusCode.Should().Be(HttpStatusCode.OK);

        var scheduled = await RowAsync(subId);
        scheduled.Status.Should().Be(SubscriptionStatus.Active);
        scheduled.CancelAtPeriodEnd.Should().BeTrue();
        (await HasAccessAsync(tenantId)).Should().BeTrue("já foi pago até o fim do período");

        Stripe.Set(subId, "canceled");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_period_that_ended_closes_access_even_if_no_event_arrived()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "active", periodEnd: DateTime.UtcNow.AddMinutes(-1), cancelAtPeriodEnd: true);

        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
        (await HasAccessAsync(tenantId)).Should().BeFalse("falha fechado: o período acabou");
    }

    [Fact]
    public async Task Active_to_past_due_to_active_keeps_access_and_clears_the_grace()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();

        Stripe.Set(subId, "past_due");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "past_due"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var late = await RowAsync(subId);
        late.Status.Should().Be(SubscriptionStatus.PastDue);
        late.PastDueSince.Should().NotBeNull();
        (await HasAccessAsync(tenantId)).Should().BeTrue();

        Stripe.Set(subId, "active", periodEnd: DateTime.UtcNow.AddDays(30));
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var paid = await RowAsync(subId);
        paid.Status.Should().Be(SubscriptionStatus.Active);
        paid.PastDueSince.Should().BeNull("pagou: a próxima falha conta uma carência nova");
        (await HasAccessAsync(tenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task Past_due_beyond_the_grace_loses_access_and_new_events_do_not_restart_the_clock()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        var since = DateTime.UtcNow.AddDays(-(ModuleSubscription.PastDueGraceDays + 1));
        await MutateRowAsync(subId, row => row.MarkPastDue(since));
        (await HasAccessAsync(tenantId)).Should().BeFalse("passou da carência de 7 dias");

        Stripe.Set(subId, "past_due");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "past_due"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DeliverAsync(InvoicePaymentFailedEvent(subId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(subId);
        row.PastDueSince.Should().BeCloseTo(since, TimeSpan.FromSeconds(1), "outra tentativa que falha não reabre a carência");
        (await HasAccessAsync(tenantId)).Should().BeFalse();

        Stripe.Set(subId, "active", periodEnd: DateTime.UtcNow.AddDays(30));
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await HasAccessAsync(tenantId)).Should().BeTrue("pagou depois da carência: o acesso volta");
    }

    [Fact]
    public async Task Past_due_to_canceled_ends_access_and_a_later_failure_does_not_reopen_a_grace()
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "past_due");
        await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "past_due"));

        Stripe.Set(subId, "canceled");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(tenantId)).Should().BeFalse();

        (await DeliverAsync(InvoicePaymentFailedEvent(subId))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Canceled);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task An_incomplete_checkout_gives_no_access_until_stripe_says_it_is_paid()
    {
        var tenantId = await NewTenantAsync();
        var subId = NewSubscriptionId();
        Stripe.Set(subId, "incomplete");

        (await DeliverAsync(CheckoutEvent(tenantId, "build", subId))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Suspended);
        (await HasAccessAsync(tenantId)).Should().BeFalse("o primeiro pagamento ainda está pendente");

        Stripe.Set(subId, "active");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
        (await HasAccessAsync(tenantId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("paused")]
    [InlineData("unpaid")]
    [InlineData("um_status_que_o_stripe_ainda_vai_criar")]
    public async Task Statuses_not_known_to_be_paid_fail_closed(string stripeStatus)
    {
        var (tenantId, subId) = await PaidSubscriptionAsync();
        (await HasAccessAsync(tenantId)).Should().BeTrue();
        Stripe.Set(subId, stripeStatus);

        (await DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: stripeStatus))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Suspended);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    // ── Entrega duplicada e simultânea ────────────────────────────────────────

    [Fact]
    public async Task A_replayed_event_answers_200_with_no_second_effect()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        var body = SubscriptionEvent("customer.subscription.updated", subId, status: "active");
        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reads = Stripe.Reads(subId);

        Stripe.Set(subId, "past_due");
        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);

        Stripe.Reads(subId).Should().Be(reads, "o evento já foi processado");
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
    }

    [Fact]
    public async Task Simultaneous_deliveries_of_a_checkout_answer_200_with_a_single_effect()
    {
        var tenantId = await NewTenantAsync();
        var subId = NewSubscriptionId();
        Stripe.Set(subId, "active");
        Stripe.WaitForOverlap(subId, TimeSpan.FromSeconds(2));
        var body = CheckoutEvent(tenantId, "build", subId);
        var signature = Sign(body);

        var responses = await Task.WhenAll(DeliverAsync(body, signature), DeliverAsync(body, signature));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK, "o perdedor não precisa de reenvio");
        Stripe.Reads(subId).Should().Be(1, "só a primeira entrega tem efeito");
        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Simultaneous_deliveries_of_an_update_answer_200_with_a_single_effect()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        Stripe.WaitForOverlap(subId, TimeSpan.FromSeconds(2));
        var body = SubscriptionEvent("customer.subscription.updated", subId, status: "active");
        var signature = Sign(body);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => DeliverAsync(body, signature)));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        Stripe.Reads(subId).Should().Be(1);
        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
    }

    [Fact]
    public async Task When_the_first_delivery_fails_the_simultaneous_one_takes_over_the_event()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        var body = SubscriptionEvent("customer.subscription.updated", subId, status: "past_due");
        var signature = Sign(body);
        Stripe.Set(subId, "past_due");
        var gate = Stripe.HoldNextRead(subId);

        var first = DeliverAsync(body, signature);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        var second = DeliverAsync(body, signature);
        await Task.Delay(300);   // a segunda chega ao registro do evento e espera a primeira terminar
        gate.ReleaseWithFailure();

        (await first).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await second).StatusCode.Should().Be(HttpStatusCode.OK, "a falha da primeira não pode perder o evento");
        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task A_read_in_flight_cannot_overwrite_the_newer_state_of_an_overlapping_event()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "past_due");
        var gate = Stripe.HoldNextRead(subId);

        // A: leu "past_due" do Stripe e ainda não gravou
        var older = DeliverAsync(InvoicePaymentFailedEvent(subId));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30));

        // B: o cliente pagou; o evento novo tem a chance de terminar antes de A
        Stripe.Set(subId, "active", periodEnd: DateTime.UtcNow.AddDays(30));
        var newer = DeliverAsync(SubscriptionEvent("customer.subscription.updated", subId, status: "active"));
        await Task.WhenAny(newer, Task.Delay(TimeSpan.FromSeconds(1)));

        gate.Release();
        (await older).StatusCode.Should().Be(HttpStatusCode.OK);
        (await newer).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowAsync(subId);
        row.Status.Should().Be(SubscriptionStatus.Active, "o último a gravar precisa ter lido depois do anterior gravar");
        row.PastDueSince.Should().BeNull();
    }

    // ── Assinatura e tipos de evento ──────────────────────────────────────────

    [Fact]
    public async Task A_bad_missing_or_expired_signature_answers_400_and_changes_nothing()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        Stripe.Set(subId, "canceled");
        var body = SubscriptionEvent("customer.subscription.deleted", subId, status: "canceled");

        foreach (var signature in new[]
                 {
                     "t=1,v1=deadbeef",
                     Sign(body, secret: "whsec_outro_segredo"),
                     Sign(body, at: DateTimeOffset.UtcNow.AddMinutes(-10)),
                     Sign(body + " "),
                 })
            (await DeliverAsync(body, signature)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var noHeader = await Host.CreateClient().PostAsync("/api/billing/webhook", new StringContent(body, Encoding.UTF8, "application/json"));
        noHeader.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
        (await ProcessedCountAsync(EventId(body))).Should().Be(0);
    }

    [Fact]
    public async Task An_unhandled_event_type_is_recorded_with_no_effect()
    {
        var (_, subId) = await PaidSubscriptionAsync();
        var body = SubscriptionEvent("customer.subscription.trial_will_end", subId, status: "canceled");

        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        (await RowAsync(subId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_checkout_with_no_metadata_is_recorded_with_no_effect()
    {
        var body = Event("checkout.session.completed", """
            { "id":"cs_sem_metadados","object":"checkout.session","customer":"cus_x","subscription":"sub_sem_metadados","metadata":{} }
            """);

        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        Stripe.Reads("sub_sem_metadados").Should().Be(0);
    }

    [Fact]
    public async Task A_checkout_of_a_company_that_does_not_exist_is_recorded_and_activates_nothing()
    {
        var unknownTenant = Guid.NewGuid();
        var subId = NewSubscriptionId();
        var body = CheckoutEvent(unknownTenant, "build", subId);

        (await DeliverAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK, "answering an error would only make Stripe repeat it");

        (await ProcessedCountAsync(EventId(body))).Should().Be(1);
        (await RowOrNullAsync(unknownTenant, "build")).Should().BeNull();
        Stripe.Reads(subId).Should().Be(0);
    }

    [Fact]
    public async Task A_second_checkout_over_a_live_subscription_moves_the_row_to_the_new_one_so_its_events_still_arrive()
    {
        var (tenantId, _) = await PaidSubscriptionAsync();
        var newSubId = NewSubscriptionId();
        Stripe.Set(newSubId, "active", priceId: "price_annual", interval: "year");

        (await DeliverAsync(CheckoutEvent(tenantId, "build", newSubId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await RowOrNullAsync(tenantId, "build");
        row!.StripeSubscriptionId.Should().Be(newSubId, "otherwise updates and cancellations of the new one find no row");
        row.StripePriceId.Should().Be("price_annual");
        row.PlanType.Should().Be(PlanType.Annual);

        // The proof that it follows the new subscription: its cancellation closes the access.
        Stripe.Set(newSubId, "canceled");
        (await DeliverAsync(SubscriptionEvent("customer.subscription.deleted", newSubId, status: "canceled"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await HasAccessAsync(tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_price_with_an_interval_that_is_not_annual_is_recorded_as_monthly()
    {
        var tenantId = await NewTenantAsync();
        var subId = NewSubscriptionId();
        Stripe.Set(subId, "active", interval: "week");

        (await DeliverAsync(CheckoutEvent(tenantId, "build", subId))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(subId)).PlanType.Should().Be(PlanType.Monthly);
    }

    [Fact]
    public async Task The_webhook_does_not_exist_when_stripe_is_disabled()
    {
        var body = SubscriptionEvent("customer.subscription.updated", NewSubscriptionId(), status: "active");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", Sign(body));

        var response = await _factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Eventos assinados como o Stripe assina ────────────────────────────────

    private static string Sign(string payload, string secret = WebhookSecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    private static string Event(string type, string dataObject) => $$"""
        { "id":"evt_{{Guid.NewGuid():N}}","object":"event","api_version":"2025-03-31.basil","created":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},"livemode":false,
          "pending_webhooks":1,"request":{ "id":null,"idempotency_key":null },"type":"{{type}}",
          "data":{ "object":{{dataObject}} } }
        """;

    private static string SubscriptionEvent(string type, string subId, string status, bool cancelAtPeriodEnd = false, bool stalePeriod = false)
    {
        var start = DateTimeOffset.UtcNow.AddDays(stalePeriod ? -60 : -5).ToUnixTimeSeconds();
        var end = DateTimeOffset.UtcNow.AddDays(stalePeriod ? -30 : 25).ToUnixTimeSeconds();
        return Event(type, $$"""
            { "id":"{{subId}}","object":"subscription","status":"{{status}}","cancel_at_period_end":{{(cancelAtPeriodEnd ? "true" : "false")}},
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

    private async Task<HttpResponseMessage> DeliverAsync(string body, string? signature = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature ?? Sign(body));
        return await Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).SendAsync(request);
    }

    // ── Estado ────────────────────────────────────────────────────────────────

    private static string NewSubscriptionId() => $"sub_{Guid.NewGuid():N}";

    private async Task<Guid> NewTenantAsync()
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var digits = string.Concat(Guid.NewGuid().ToString("N").Select(c => (char)('0' + c % 10)));
        var tenant = Tenant.Create("Empresa do webhook", digits[..14], $"webhook-{digits[..10]}@{TestCredentials.TestDomain}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<(Guid TenantId, string SubId)> PaidSubscriptionAsync()
    {
        var tenantId = await NewTenantAsync();
        var subId = NewSubscriptionId();
        await AddRowAsync(ModuleSubscription.CreateFromStripe(tenantId, "build", subId, "price_monthly", PlanType.Monthly,
            DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(25)));
        Stripe.Set(subId, "active");
        return (tenantId, subId);
    }

    private async Task AddRowAsync(ModuleSubscription row)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.ModuleSubscriptions.Add(row);
        await db.SaveChangesAsync();
    }

    private async Task MutateRowAsync(string subId, Action<ModuleSubscription> change)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        change(await db.ModuleSubscriptions.SingleAsync(s => s.StripeSubscriptionId == subId));
        await db.SaveChangesAsync();
    }

    private async Task<ModuleSubscription> RowAsync(string subId)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.ModuleSubscriptions.AsNoTracking().SingleAsync(s => s.StripeSubscriptionId == subId);
    }

    private async Task<ModuleSubscription?> RowOrNullAsync(Guid tenantId, string moduleKey)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.ModuleSubscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.ModuleKey == moduleKey);
    }

    /// <summary>A mesma checagem dos gates, com o cache do host: um evento que não invalida o cache aparece aqui.</summary>
    private async Task<bool> HasAccessAsync(Guid tenantId, string moduleKey = "build")
    {
        using var scope = Host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IModuleAccessService>().HasActiveModuleAsync(tenantId, moduleKey);
    }

    private async Task<int> ProcessedCountAsync(string eventId)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.StripeProcessedEvents.AsNoTracking().CountAsync(e => e.StripeEventId == eventId);
    }

    private async Task<int> ModuleEventCountAsync(Guid tenantId, string eventType)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.ModuleSubscriptionEvents.IgnoreQueryFilters().AsNoTracking().CountAsync(e => e.TenantId == tenantId && e.EventType == eventType);
    }

    private static async Task<string> ErrorOfAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    private async Task<HttpClient> PlatformClientAsync()
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/platform/auth/login", new { email = TestCredentials.PlatformEmail, password = TestCredentials.PlatformPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>O dono de uma empresa nova, pelo cadastro público, com sessão aberta no host do webhook.</summary>
    private async Task<(HttpClient Client, Guid TenantId)> TenantUserAsync()
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"wh-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var registered = await client.PostAsJsonAsync("/api/auth/register", new { name = "Empresa do gate", email, password = TestCredentials.AdminPassword });
        registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());

        Guid tenantId;
        using (var scope = Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            await db.SaveChangesAsync();
            tenantId = user.TenantId;
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(email, TestCredentials.AdminPassword));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return (client, tenantId);
    }

    /// <summary>Nada neste teste chega ao Stripe: o único caminho de cobrança do host falha se for chamado.</summary>
    private sealed class NoStripeBillingProvider : IBillingProvider
    {
        public Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, string companyName, CancellationToken ct = default)
            => throw new InvalidOperationException("O teste do webhook não cria cliente no Stripe.");

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("O teste do webhook não cria checkout no Stripe.");

        public Task<PortalSessionResult> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
            => throw new InvalidOperationException("O teste do webhook não abre o portal do Stripe.");
    }
}
