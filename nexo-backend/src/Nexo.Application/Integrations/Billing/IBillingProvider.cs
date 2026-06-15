namespace Nexo.Application.Integrations.Billing;

public interface IBillingProvider
{
    /// <summary>
    /// Looks up or creates a Stripe Customer for the given tenant.
    /// Returns the Stripe customer ID (cus_...).
    /// </summary>
    Task<string> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string companyName,
        CancellationToken ct = default);

    /// <summary>Creates a Stripe Checkout Session for subscribing to a module.</summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CreateCheckoutRequest request,
        CancellationToken ct = default);

    /// <summary>Creates a Stripe Customer Portal session for managing subscriptions.</summary>
    Task<PortalSessionResult> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct = default);
}
