using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Api.Controllers;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
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
        Dictionary<string, string>? priceIds = null,
        string? newCheckouts = null,
        string? jsonConfig = null)
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

        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "http://localhost:5173",
                [BillingRules.NewCheckoutsSetting] = newCheckouts,
            });
        if (jsonConfig is not null)
            configBuilder.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonConfig)));
        var config = configBuilder.Build();

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
    public async Task CreateCheckout_FlagDisabled_WinsOverAnOpenSwitch()
    {
        var sut    = Build(stripeEnabled: false, newCheckouts: "true");
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("talvez")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    [InlineData("1")]
    public async Task CreateCheckout_SwitchNotLiterallyTrue_Answers403BeforeTouchingAnything(string? newCheckouts)
    {
        var sut    = Build(newCheckouts: newCheckouts);
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody(null, null, null, null), default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("checkout_disabled", refused.Value!.GetType().GetProperty("code")!.GetValue(refused.Value));
        await sut.Tenants.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await sut.Subscriptions.DidNotReceiveWithAnyArgs().GetByTenantIdAsync(default, default);
        await sut.Billing.DidNotReceiveWithAnyArgs().GetOrCreateCustomerAsync(default, default!, default!, default);
        await sut.Billing.DidNotReceiveWithAnyArgs().CreateCheckoutSessionAsync(default!, default);
    }

    /// <summary>The literal true, as an appsettings file writes it. A JSON boolean reaches the configuration as "True".</summary>
    [Theory]
    [InlineData("""{ "Billing": { "NewCheckoutsEnabled": true } }""", true)]
    [InlineData("""{ "Billing": { "NewCheckoutsEnabled": false } }""", false)]
    public void GetCheckoutAvailability_ReadsAJsonBoolean(string jsonConfig, bool open)
    {
        var sut    = Build(jsonConfig: jsonConfig);
        var result = sut.Controller.GetCheckoutAvailability();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(open, ok.Value!.GetType().GetProperty("newCheckoutsEnabled")!.GetValue(ok.Value));
    }

    [Fact]
    public void GetCheckoutAvailability_FlagDisabled_ReturnsNotFound()
    {
        var sut = Build(stripeEnabled: false, newCheckouts: "true");
        Assert.IsType<NotFoundObjectResult>(sut.Controller.GetCheckoutAvailability());
    }

    [Fact]
    public async Task CreateCheckout_MissingModuleKey_ReturnsBadRequest()
    {
        var sut    = Build(newCheckouts: "true");
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody(null, "monthly", null, null), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_PriceIdNotConfigured_ReturnsBadRequest()
    {
        var sut    = Build(newCheckouts: "true");
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("build", "monthly", null, null), default); // build_monthly not in PriceIds
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_TenantNotFound_ReturnsNotFound()
    {
        var sut = Build(newCheckouts: "true");
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_ValidRequest_RedirectsToCheckoutUrl()
    {
        var sut    = Build(newCheckouts: "true");
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

    // ── A second purchase of what the company already has ─────────────────────

    private static string? ErrorOf(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value!;
        return value.GetType().GetProperty("error")!.GetValue(value)?.ToString();
    }

    private static ModuleSubscription StripeRow(string module = "restaurante") => ModuleSubscription.CreateFromStripe(
        Guid.NewGuid(), module, "sub_fixture", "price_fixture", PlanType.Monthly, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

    private static async Task<IActionResult> CheckoutWith(Sut sut, ModuleSubscription row)
    {
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(MakeTenant("cus_existing"));
        sut.Subscriptions.GetByTenantIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([row]);
        sut.Billing
            .CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CheckoutSessionResult("cs_fixture", "https://checkout.example/cs_fixture"));
        return await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);
    }

    [Fact]
    public async Task CreateCheckout_ModuleStillBilledInStripe_Answers409AndOpensNoSecondSubscription()
    {
        var sut = Build(newCheckouts: "true");

        var result = await CheckoutWith(sut, StripeRow());

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("Gerenciar assinatura", ErrorOf(result));
        await sut.Billing.DidNotReceive().CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCheckout_LatePaymentStillBillsTheOldCard_Answers409()
    {
        var sut = Build(newCheckouts: "true");
        var row = StripeRow();
        row.MarkPastDue(DateTime.UtcNow.AddDays(-30));   // grace over: no access, and Stripe still bills it

        var result = await CheckoutWith(sut, row);

        Assert.False(row.IsActive());
        Assert.IsType<ConflictObjectResult>(result);
        await sut.Billing.DidNotReceive().CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCheckout_ActiveAdminGrant_Answers409SoAPaymentDoesNotReplaceIt()
    {
        var sut = Build(newCheckouts: "true");

        var result = await CheckoutWith(sut, ModuleSubscription.CreateAdminGrant(Guid.NewGuid(), "restaurante"));

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("já está liberado", ErrorOf(result));
        await sut.Billing.DidNotReceive().CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCheckout_RunningTrial_MayStillBuy()
    {
        var sut = Build(newCheckouts: "true");

        var result = await CheckoutWith(sut, ModuleSubscription.CreateTrial(Guid.NewGuid(), "restaurante"));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_CanceledStripeSubscription_MayBuyAgain()
    {
        var sut = Build(newCheckouts: "true");
        var row = StripeRow();
        row.Cancel();

        var result = await CheckoutWith(sut, row);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateCheckout_SubscriptionOfAnotherModule_DoesNotBlock()
    {
        var sut = Build(newCheckouts: "true");

        var result = await CheckoutWith(sut, StripeRow(module: "build"));

        Assert.IsType<OkObjectResult>(result);
    }

    // ── Stripe does not answer ────────────────────────────────────────────────

    [Fact]
    public async Task CreateCheckout_StripeFails_Answers502WithoutItsDetails()
    {
        var sut = Build(newCheckouts: "true");
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(MakeTenant("cus_existing"));
        sut.Billing
            .CreateCheckoutSessionAsync(Arg.Any<CreateCheckoutRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider detail that must not reach the customer"));

        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);

        Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.DoesNotContain("provider detail", ErrorOf(result));
    }

    [Fact]
    public async Task CreateCheckout_CustomerCreationFails_DoesNotPersistACustomerId()
    {
        var sut = Build(newCheckouts: "true");
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(MakeTenant());
        sut.Billing
            .GetOrCreateCustomerAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider detail"));

        var result = await sut.Controller.CreateCheckout(
            new BillingController.CreateCheckoutBody("restaurante", "monthly", null, null), default);

        Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
        await sut.Tenants.DidNotReceive().SetStripeCustomerIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePortal_StripeFails_Answers502WithoutItsDetails()
    {
        var sut = Build();
        sut.Tenants.GetByIdAsync(sut.CurrentTenant.Id, Arg.Any<CancellationToken>()).Returns(MakeTenant("cus_existing"));
        sut.Billing
            .CreatePortalSessionAsync("cus_existing", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider detail that must not reach the customer"));

        var result = await sut.Controller.CreatePortal(null, default);

        Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.DoesNotContain("provider detail", ErrorOf(result));
    }

    // ── Webhook: what makes Stripe deliver the event again ────────────────────

    private sealed class BrokenBody : MemoryStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new IOException("the connection dropped while reading the body");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException("the connection dropped while reading the body");
    }

    [Fact]
    public async Task Webhook_BodyCannotBeRead_Answers400AndHandlesNothing()
    {
        var sut = Build();
        var http = sut.Controller.ControllerContext.HttpContext;
        http.Request.Body = new BrokenBody();
        http.Request.Headers["Stripe-Signature"] = "t=1,v1=abc";

        var result = await sut.Controller.Webhook(default);

        Assert.IsType<BadRequestObjectResult>(result);
        await sut.WebhookService.DidNotReceive().HandleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_HandlingFails_Answers500SoStripeDeliversAgain()
    {
        var sut = Build();
        var http = sut.Controller.ControllerContext.HttpContext;
        http.Request.Body = new MemoryStream("{}"u8.ToArray());
        http.Request.Headers["Stripe-Signature"] = "t=1,v1=abc";
        sut.WebhookService
            .HandleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database detail"));

        var result = await sut.Controller.Webhook(default);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.DoesNotContain("database detail", ErrorOf(result));
    }
}
