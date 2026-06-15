using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nexo.Api.Controllers;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Entities;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public sealed class BillingControllerTests
{
    private record Sut(
        BillingController Controller,
        IBillingProvider Billing,
        ITenantRepository Tenants,
        IModuleSubscriptionRepository Subscriptions,
        IStripeWebhookService WebhookService,
        IIntegrationFeatureFlags Flags,
        ICurrentTenant CurrentTenant);

    private static Sut Build(
        bool stripeEnabled = true,
        Dictionary<string, string>? priceIds = null)
    {
        var billing         = Substitute.For<IBillingProvider>();
        var tenants         = Substitute.For<ITenantRepository>();
        var subscriptions   = Substitute.For<IModuleSubscriptionRepository>();
        var webhookService  = Substitute.For<IStripeWebhookService>();
        var flags           = Substitute.For<IIntegrationFeatureFlags>();
        var currentTenant   = Substitute.For<ICurrentTenant>();
        var logger          = NullLogger<BillingController>.Instance;

        flags.StripeEnabled.Returns(stripeEnabled);
        currentTenant.Id.Returns(Guid.NewGuid());

        var options = Options.Create(new StripeOptions
        {
            SecretKey     = "sk_test_dummy",
            WebhookSecret = "whsec_dummy",
            PriceIds      = priceIds ?? new Dictionary<string, string>
            {
                ["restaurante_monthly"] = "price_restaurante_monthly",
            },
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "http://localhost:5173",
            })
            .Build();

        var controller = new BillingController(
            billing, tenants, subscriptions, webhookService,
            flags, options, currentTenant, config, logger);

        // Set up HTTP context so Request.Scheme / Request.Host are available
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return new Sut(controller, billing, tenants, subscriptions, webhookService, flags, currentTenant);
    }

    private static Tenant MakeTenant(string? stripeCustomerId = null)
    {
        var t = Tenant.Create("Empresa Teste", "12.345.678/0001-99", "empresa@teste.com");
        if (stripeCustomerId is not null)
            t.SetStripeCustomerId(stripeCustomerId);
        return t;
    }

    // ── GetSubscriptions ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSubscriptions_FlagDisabled_ReturnsNotFound()
    {
        var sut    = Build(stripeEnabled: false);
        var result = await sut.Controller.GetSubscriptions(default);
        Assert.IsType<NotFoundObjectResult>(result);
        await sut.Subscriptions.DidNotReceive().GetByTenantIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_FlagEnabled_ReturnsOk()
    {
        var sut = Build();
        sut.Subscriptions.GetByTenantIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>())
            .Returns([]);
        var result = await sut.Controller.GetSubscriptions(default);
        Assert.IsType<OkObjectResult>(result);
    }

    // ── CreateCheckout ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCheckout_FlagDisabled_ReturnsNotFound()
    {
        var sut    = Build(stripeEnabled: false);
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_MissingModuleKey_ReturnsBadRequest()
    {
        var sut    = Build();
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody(null, "monthly", null, null), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_PriceIdNotConfigured_ReturnsBadRequest()
    {
        var sut    = Build();
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("build", "monthly", null, null), default); // build_monthly not in PriceIds
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_TenantNotFound_ReturnsNotFound()
    {
        var sut = Build();
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_ValidRequest_RedirectsToCheckoutUrl()
    {
        var sut    = Build();
        var tenant = MakeTenant("cus_existing");
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        sut.Billing
            .CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CheckoutSessionResult("cs_test", "https://checkout.stripe.com/pay/cs_test"));

        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);

        var ok    = Assert.IsType<OkObjectResult>(result);
        var url   = ok.Value!.GetType().GetProperty("checkoutUrl")!.GetValue(ok.Value)!.ToString();
        Assert.Equal("https://checkout.stripe.com/pay/cs_test", url);
    }

    // ── Webhook ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_FlagDisabled_ReturnsNotFound()
    {
        var sut = Build(stripeEnabled: false);
        sut.Controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream("{}u8"u8.ToArray());
        var result = await sut.Controller.Webhook(default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_ReturnsBadRequest()
    {
        var sut = Build();
        sut.Controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream("{}"u8.ToArray());
        sut.Controller.ControllerContext.HttpContext.Request.Headers["Stripe-Signature"] = "t=bad,v1=bad";
        sut.WebhookService.HandleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookHandleResult(SignatureValid: false, AlreadyProcessed: false));

        var result = await sut.Controller.Webhook(default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Webhook_ValidSignature_ReturnsOk()
    {
        var sut = Build();
        sut.Controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream("{}"u8.ToArray());
        sut.Controller.ControllerContext.HttpContext.Request.Headers["Stripe-Signature"] = "t=1,v1=ok";
        sut.WebhookService.HandleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: false));

        var result = await sut.Controller.Webhook(default);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_AlreadyProcessed_ReturnsOkImmediately()
    {
        var sut = Build();
        sut.Controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream("{}"u8.ToArray());
        sut.Controller.ControllerContext.HttpContext.Request.Headers["Stripe-Signature"] = "t=1,v1=ok";
        sut.WebhookService.HandleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: true));

        var result = await sut.Controller.Webhook(default);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_MissingSignatureHeader_ReturnsBadRequest()
    {
        var sut = Build();
        sut.Controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream("{}"u8.ToArray());
        // Stripe-Signature not set → empty string
        var result = await sut.Controller.Webhook(default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── CreatePortal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePortal_FlagDisabled_ReturnsNotFound()
    {
        var sut    = Build(stripeEnabled: false);
        var result = await sut.Controller.CreatePortal(null, default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreatePortal_NoStripeCustomerId_ReturnsBadRequest()
    {
        var sut    = Build();
        var tenant = MakeTenant(); // no StripeCustomerId
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var result = await sut.Controller.CreatePortal(null, default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreatePortal_ValidCustomer_ReturnsPortalUrl()
    {
        var sut    = Build();
        var tenant = MakeTenant("cus_existing");
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        sut.Billing
            .CreatePortalSessionAsync("cus_existing", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PortalSessionResult("https://billing.stripe.com/session/test"));

        var result = await sut.Controller.CreatePortal(null, default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var url    = ok.Value!.GetType().GetProperty("portalUrl")!.GetValue(ok.Value)!.ToString();
        Assert.Equal("https://billing.stripe.com/session/test", url);
    }
}
