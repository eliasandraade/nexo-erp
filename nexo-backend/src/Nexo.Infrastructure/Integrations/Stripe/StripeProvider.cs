using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.BillingPortal;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using StripeCheckout = Stripe.Checkout;

namespace Nexo.Infrastructure.Integrations.Stripe;

public sealed class StripeProvider : IBillingProvider
{
    private readonly StripeOptions             _options;
    private readonly ILogger<StripeProvider>   _logger;

    public StripeProvider(IOptions<StripeOptions> options, ILogger<StripeProvider> logger)
    {
        _options = options.Value;
        _logger  = logger;

        StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public async Task<string> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string companyName,
        CancellationToken ct)
    {
        var service  = new CustomerService();
        var existing = await service.SearchAsync(new CustomerSearchOptions
        {
            Query = $"metadata['tenantId']:'{tenantId}'",
        }, cancellationToken: ct);

        if (existing.Data.Count > 0)
            return existing.Data[0].Id;

        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email    = email,
            Name     = companyName,
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
        }, cancellationToken: ct);

        _logger.LogInformation("[Billing] Created Stripe customer {CustomerId} for tenant {TenantId}",
            customer.Id, tenantId);

        return customer.Id;
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CreateCheckoutRequest request,
        CancellationToken ct)
    {
        var service = new StripeCheckout.SessionService();
        var session = await service.CreateAsync(new StripeCheckout.SessionCreateOptions
        {
            Customer           = request.StripeCustomerId,
            Mode               = "subscription",
            PaymentMethodTypes = new List<string> { "card" },
            LineItems          =
            [
                new StripeCheckout.SessionLineItemOptions
                {
                    Price    = request.StripePriceId,
                    Quantity = 1,
                },
            ],
            SuccessUrl = request.SuccessUrl,
            CancelUrl  = request.CancelUrl,
            Metadata   = new Dictionary<string, string>
            {
                ["tenantId"]  = request.TenantId.ToString(),
                ["moduleKey"] = request.ModuleKey,
            },
            SubscriptionData = new StripeCheckout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["tenantId"]  = request.TenantId.ToString(),
                    ["moduleKey"] = request.ModuleKey,
                },
            },
        }, cancellationToken: ct);

        return new CheckoutSessionResult(session.Id, session.Url);
    }

    public async Task<PortalSessionResult> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct)
    {
        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Customer  = stripeCustomerId,
            ReturnUrl = returnUrl,
        }, cancellationToken: ct);

        return new PortalSessionResult(session.Url);
    }
}
