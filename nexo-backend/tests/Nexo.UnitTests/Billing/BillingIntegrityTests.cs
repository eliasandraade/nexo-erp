using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Integrations.Stripe;
using Xunit;

namespace Nexo.UnitTests.Billing;

public class BillingIntegrityTests
{
    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Canceled)]
    [InlineData("unpaid", SubscriptionStatus.Suspended)]
    [InlineData("incomplete", SubscriptionStatus.Suspended)]
    [InlineData("paused", SubscriptionStatus.Suspended)]
    [InlineData("something_new", SubscriptionStatus.Suspended)]
    [InlineData(null, SubscriptionStatus.Suspended)]
    public void Stripe_statuses_not_known_to_be_paid_give_no_access(string? stripe, SubscriptionStatus ours)
        => BillingRules.MapStripeStatus(stripe).Should().Be(ours);

    [Theory]
    [InlineData("https://app.orken.com.br/assinatura?ok=1", "https://app.orken.com.br/assinatura?ok=1")]
    [InlineData("https://evil.example/phish", "FALLBACK")]
    [InlineData("https://app.orken.com.br.evil.example/x", "FALLBACK")]
    [InlineData("http://app.orken.com.br/assinatura", "FALLBACK")]
    [InlineData("https://app.orken.com.br:8443/x", "FALLBACK")]
    [InlineData("javascript:alert(1)", "FALLBACK")]
    [InlineData("/assinatura", "FALLBACK")]
    [InlineData(null, "FALLBACK")]
    public void Redirects_stay_on_the_app_origin(string? requested, string expected)
        => BillingRules.SafeRedirect(requested, "FALLBACK", "https://app.orken.com.br").Should().Be(expected);

    [Fact]
    public void Past_due_keeps_access_for_the_grace_period_counted_from_the_first_failure()
    {
        var now = DateTime.UtcNow;
        var sub = ModuleSubscription.CreateFromStripe(Guid.NewGuid(), "build", "sub_1", "price_1", PlanType.Monthly, now.AddDays(-40), now.AddDays(-10));
        var access = ModuleSubscription.HasAccessAt(now).Compile();

        sub.MarkPastDue(now.AddDays(-3));
        access(sub).Should().BeTrue("3 days into the grace");
        sub.MarkPastDue(now);                                   // another retry failed: the clock doesn't restart
        sub.PastDueSince.Should().Be(now.AddDays(-3));

        ModuleSubscription.HasAccessAt(now.AddDays(5)).Compile()(sub).Should().BeFalse("8 days after the first failure");

        sub.SyncFromStripe(SubscriptionStatus.Active, now, now.AddDays(30), false);
        sub.PastDueSince.Should().BeNull("paid again");
        access(sub).Should().BeTrue();
    }

    // ── Webhook (signed like Stripe does) ─────────────────────────────────────

    private const string Secret = "whsec_test_secret";

    private static string Sign(string payload)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var sig = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant();
        return $"t={t},v1={sig}";
    }

    private static string SubscriptionEvent(string id, string type, string subId, string status) => $$$"""
        { "id":"{{{id}}}","object":"event","api_version":"2025-03-31.basil","created":1700000000,"livemode":false,
         "pending_webhooks":1,"request":{ "id":null,"idempotency_key":null} ,"type":"{{{type}}}",
         "data":{ "object":{ "id":"{{{subId}}}","object":"subscription","status":"{{{status}}}","cancel_at_period_end":false,
           "items":{ "object":"list","data":[{ "id":"si_1","object":"subscription_item","current_period_start":1700000000,"current_period_end":1702592000,
             "price":{ "id":"price_1","object":"price","recurring":{ "interval":"month"} } } ]} } } } 
        """;

    private static (StripeWebhookService Service, IModuleAccessService Access, ModuleSubscription Sub) Arrange(
        string subId, IModuleSubscriptionRepository? repo = null, ITenantRepository? tenants = null, IStripeSubscriptionFetcher? fetcher = null)
    {
        var sub = ModuleSubscription.CreateFromStripe(Guid.NewGuid(), "build", subId, "price_1", PlanType.Monthly, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        var subs = repo ?? Substitute.For<IModuleSubscriptionRepository>();
        if (repo is null) subs.GetByStripeSubscriptionIdAsync(subId, Arg.Any<CancellationToken>()).Returns(sub);
        var processed = Substitute.For<IStripeProcessedEventRepository>();
        var access = Substitute.For<IModuleAccessService>();
        var service = new StripeWebhookService(
            Options.Create(new StripeOptions { WebhookSecret = Secret }), subs, processed,
            tenants ?? Substitute.For<ITenantRepository>(), access, fetcher ?? Substitute.For<IStripeSubscriptionFetcher>(),
            Substitute.For<ICacheService>(), NullLogger<StripeWebhookService>.Instance);
        return (service, access, sub);
    }

    private static string CheckoutEvent(string id, Guid tenantId, string subId) => $$$"""
        { "id":"{{{id}}}","object":"event","api_version":"2025-03-31.basil","created":1700000000,"livemode":false,
          "pending_webhooks":1,"request":{ "id":null,"idempotency_key":null } ,"type":"checkout.session.completed",
          "data":{ "object":{ "id":"cs_1","object":"checkout.session","customer":"cus_1","subscription":"{{{subId}}}",
            "metadata":{ "tenantId":"{{{tenantId}}}","moduleKey":"build" } } } }
        """;

    [Fact]
    public void A_late_payment_failure_does_not_reopen_a_grace_for_an_ended_subscription()
    {
        var sub = ModuleSubscription.CreateFromStripe(Guid.NewGuid(), "build", "sub_x", "price_1", PlanType.Monthly, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        sub.Cancel();
        sub.MarkPastDue();
        sub.Status.Should().Be(SubscriptionStatus.Canceled);
        sub.IsActive().Should().BeFalse();
    }

    [Fact]
    public async Task Checkout_over_an_existing_row_follows_the_new_subscription_with_its_real_status()
    {
        var tenantId = Guid.NewGuid();
        var old = ModuleSubscription.CreateFromStripe(tenantId, "build", "sub_old", "price_0", PlanType.Monthly, DateTime.UtcNow.AddDays(-60), DateTime.UtcNow.AddDays(-30));
        old.Cancel();
        var repo = Substitute.For<IModuleSubscriptionRepository>();
        repo.GetByTenantIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new[] { old });
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Tenant.Create("Empresa", "00000000000191", "a@b.dev"));
        var fetcher = Substitute.For<IStripeSubscriptionFetcher>();
        fetcher.GetAsync("sub_new", Arg.Any<CancellationToken>()).Returns(
            new StripeSubscriptionSnapshot("sub_new", "active", "price_2", "year", DateTime.UtcNow, DateTime.UtcNow.AddYears(1), false));

        var (service, access, _) = Arrange("sub_new", repo, tenants, fetcher);
        var body = CheckoutEvent("evt_c1", tenantId, "sub_new");
        await service.HandleAsync(body, Sign(body), default);

        old.StripeSubscriptionId.Should().Be("sub_new", "later updates/cancellations of the new subscription must reach this row");
        old.StripePriceId.Should().Be("price_2");
        old.PlanType.Should().Be(PlanType.Annual);
        old.Status.Should().Be(SubscriptionStatus.Active);
        access.Received().InvalidateCache(tenantId, "build");
    }

    [Fact]
    public async Task A_new_row_from_checkout_takes_the_real_stripe_status()
    {
        var tenantId = Guid.NewGuid();
        var repo = Substitute.For<IModuleSubscriptionRepository>();
        repo.GetByTenantIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Array.Empty<ModuleSubscription>());
        ModuleSubscription? added = null;
        await repo.AddAsync(Arg.Do<ModuleSubscription>(x => added = x), Arg.Any<CancellationToken>());
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Tenant.Create("Empresa", "00000000000191", "a@b.dev"));
        var fetcher = Substitute.For<IStripeSubscriptionFetcher>();
        fetcher.GetAsync("sub_inc", Arg.Any<CancellationToken>()).Returns(
            new StripeSubscriptionSnapshot("sub_inc", "incomplete", "price_2", "month", DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), false));

        var (service, _, _) = Arrange("sub_inc", repo, tenants, fetcher);
        var body = CheckoutEvent("evt_c2", tenantId, "sub_inc");
        await service.HandleAsync(body, Sign(body), default);

        added.Should().NotBeNull();
        added!.Status.Should().Be(SubscriptionStatus.Suspended, "first payment still pending: no access yet");
    }

    [Fact]
    public async Task An_unknown_subscription_status_suspends_access_and_clears_the_access_cache()
    {
        var (service, access, sub) = Arrange("sub_unknown");
        var body = SubscriptionEvent("evt_1", "customer.subscription.updated", "sub_unknown", "paused");

        var result = await service.HandleAsync(body, Sign(body), default);

        result.SignatureValid.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Suspended, "before: anything unknown became Active");
        access.Received(1).InvalidateCache(sub.TenantId, "build");
    }

    [Fact]
    public async Task A_deleted_subscription_cancels_and_a_bad_signature_changes_nothing()
    {
        var (service, access, sub) = Arrange("sub_del");
        var body = SubscriptionEvent("evt_2", "customer.subscription.deleted", "sub_del", "canceled");

        (await service.HandleAsync(body, "t=1,v1=deadbeef", default)).SignatureValid.Should().BeFalse();
        sub.Status.Should().Be(SubscriptionStatus.Active);

        await service.HandleAsync(body, Sign(body), default);
        sub.Status.Should().Be(SubscriptionStatus.Canceled);
        access.Received(1).InvalidateCache(sub.TenantId, "build");
    }
}
