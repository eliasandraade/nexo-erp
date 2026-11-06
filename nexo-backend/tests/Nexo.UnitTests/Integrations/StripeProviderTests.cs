using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Stripe;
using Stripe;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// What the app really sends to Stripe and reads back, through the SDK down to a fake HTTP handler. Nothing leaves the process and
/// no real key exists here. The webhook activates a module from the metadata of these requests: without them a paid checkout
/// activates nothing. The SDK keeps its client in a static, restored after each test.
/// </summary>
public sealed class StripeProviderTests : IDisposable
{
    private const string PlaceholderKey = "unit-test-placeholder";

    private readonly string _previousKey = StripeConfiguration.ApiKey;
    private readonly FakeStripe _stripe = new();

    public void Dispose()
    {
        StripeConfiguration.StripeClient = null;
        StripeConfiguration.ApiKey = _previousKey;
    }

    private sealed class FakeStripe : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Query, string Body)> Requests { get; } = [];
        public Func<string, (HttpStatusCode Status, string Json)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var uri = request.RequestUri!;
            Requests.Add((request.Method, uri.AbsolutePath, Uri.UnescapeDataString(uri.Query), Uri.UnescapeDataString(body)));
            var (status, json) = Respond(uri.AbsolutePath);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private void UseFakeStripe() => StripeConfiguration.StripeClient = new StripeClient(
        PlaceholderKey,
        httpClient: new SystemNetHttpClient(new HttpClient(_stripe), maxNetworkRetries: 0));

    private StripeProvider Provider()
    {
        // The constructor sets the global key, and setting it drops the client: the fake has to come after.
        var provider = new StripeProvider(
            Options.Create(new StripeOptions { SecretKey = PlaceholderKey, WebhookSecret = PlaceholderKey }),
            NullLogger<StripeProvider>.Instance);
        UseFakeStripe();
        return provider;
    }

    // ── Customer ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tenant_that_already_has_a_customer_keeps_it()
    {
        var tenantId = Guid.NewGuid();
        var provider = Provider();
        _stripe.Respond = _ => (HttpStatusCode.OK,
            """{"object":"search_result","data":[{"id":"cus_found","object":"customer"}],"has_more":false,"url":"/v1/customers/search"}""");

        var id = await provider.GetOrCreateCustomerAsync(tenantId, "a@b.dev", "Empresa", CancellationToken.None);

        id.Should().Be("cus_found");
        var search = _stripe.Requests.Should().ContainSingle().Subject;
        search.Path.Should().Be("/v1/customers/search");
        search.Query.Should().Contain($"metadata['tenantId']:'{tenantId}'", "the customer of another company must never be found");
    }

    [Fact]
    public async Task A_tenant_without_a_customer_gets_one_tagged_with_it()
    {
        var tenantId = Guid.NewGuid();
        var provider = Provider();
        _stripe.Respond = path => path.EndsWith("/search", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"object":"search_result","data":[],"has_more":false,"url":"/v1/customers/search"}""")
            : (HttpStatusCode.OK, """{"id":"cus_new","object":"customer"}""");

        var id = await provider.GetOrCreateCustomerAsync(tenantId, "a@b.dev", "Empresa Teste", CancellationToken.None);

        id.Should().Be("cus_new");
        _stripe.Requests.Should().HaveCount(2);
        var created = _stripe.Requests[1];
        created.Method.Should().Be(HttpMethod.Post);
        created.Path.Should().Be("/v1/customers");
        created.Body.Should().Contain($"metadata[tenantId]={tenantId}").And.Contain("a@b.dev");
    }

    // ── Checkout ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_checkout_is_a_subscription_that_carries_tenant_and_module_for_the_webhook()
    {
        var tenantId = Guid.NewGuid();
        var provider = Provider();
        _stripe.Respond = _ => (HttpStatusCode.OK,
            """{"id":"cs_fixture","object":"checkout.session","url":"https://checkout.example/cs_fixture"}""");

        var result = await provider.CreateCheckoutSessionAsync(new CreateCheckoutRequest(
            tenantId, "cus_fixture", "restaurante", "price_fixture", "https://app.example/ok", "https://app.example/volta"), CancellationToken.None);

        result.SessionId.Should().Be("cs_fixture");
        result.CheckoutUrl.Should().Be("https://checkout.example/cs_fixture");

        var sent = _stripe.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().Be("/v1/checkout/sessions");
        sent.Body.Should().Contain("mode=subscription")
            .And.Contain("customer=cus_fixture")
            .And.Contain("line_items[0][price]=price_fixture")
            .And.Contain("line_items[0][quantity]=1")
            .And.Contain($"metadata[tenantId]={tenantId}")
            .And.Contain("metadata[moduleKey]=restaurante")
            .And.Contain($"subscription_data[metadata][tenantId]={tenantId}")
            .And.Contain("subscription_data[metadata][moduleKey]=restaurante")
            .And.Contain("https://app.example/ok")
            .And.Contain("https://app.example/volta");
    }

    [Fact]
    public async Task A_refused_checkout_surfaces_as_a_failure_and_never_as_a_session()
    {
        var provider = Provider();
        _stripe.Respond = _ => (HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","message":"No such price"}}""");

        var act = () => provider.CreateCheckoutSessionAsync(new CreateCheckoutRequest(
            Guid.NewGuid(), "cus_fixture", "restaurante", "price_missing", "https://app.example/ok", "https://app.example/volta"), CancellationToken.None);

        await act.Should().ThrowAsync<StripeException>();
    }

    // ── Portal ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_portal_opens_for_that_customer_and_returns_to_the_app()
    {
        var provider = Provider();
        _stripe.Respond = _ => (HttpStatusCode.OK,
            """{"id":"bps_fixture","object":"billing_portal.session","url":"https://portal.example/bps_fixture"}""");

        var result = await provider.CreatePortalSessionAsync("cus_fixture", "https://app.example/assinatura", CancellationToken.None);

        result.PortalUrl.Should().Be("https://portal.example/bps_fixture");
        var sent = _stripe.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().Be("/v1/billing_portal/sessions");
        sent.Body.Should().Contain("customer=cus_fixture").And.Contain("return_url=https://app.example/assinatura");
    }

    // ── Reading a subscription (what the webhook writes to the row) ───────────

    private const string SubscriptionJson =
        """
        {"id":"sub_fixture","object":"subscription","status":"past_due","cancel_at_period_end":true,
         "items":{"object":"list","data":[{"id":"si_fixture","object":"subscription_item",
           "current_period_start":1767225600,"current_period_end":1769904000,
           "price":{"id":"price_annual","object":"price","recurring":{"interval":"year"}}}]}}
        """;

    [Fact]
    public async Task A_subscription_is_read_with_the_period_of_its_item()
    {
        UseFakeStripe();
        _stripe.Respond = _ => (HttpStatusCode.OK, SubscriptionJson);

        var snapshot = await new StripeSubscriptionFetcher().GetAsync("sub_fixture");

        _stripe.Requests.Should().ContainSingle().Which.Path.Should().Be("/v1/subscriptions/sub_fixture");
        snapshot.Should().Be(new StripeSubscriptionSnapshot(
            "sub_fixture", "past_due", "price_annual", "year",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CancelAtPeriodEnd: true));
    }

    [Fact]
    public async Task A_subscription_without_items_reads_as_having_no_price()
    {
        UseFakeStripe();
        _stripe.Respond = _ => (HttpStatusCode.OK,
            """{"id":"sub_fixture","object":"subscription","status":"canceled","cancel_at_period_end":false,"items":{"object":"list","data":[]}}""");

        var snapshot = await new StripeSubscriptionFetcher().GetAsync("sub_fixture");

        snapshot.Status.Should().Be("canceled");
        snapshot.PriceId.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_read_throws_so_the_webhook_answers_an_error_and_Stripe_delivers_again()
    {
        UseFakeStripe();
        _stripe.Respond = _ => (HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"down"}}""");

        var act = () => new StripeSubscriptionFetcher().GetAsync("sub_fixture");

        await act.Should().ThrowAsync<StripeException>();
    }
}
