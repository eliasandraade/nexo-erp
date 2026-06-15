namespace Nexo.Application.Integrations.Billing;

public sealed record CreateCheckoutRequest(
    Guid   TenantId,
    string StripeCustomerId,
    string ModuleKey,
    string StripePriceId,
    string SuccessUrl,
    string CancelUrl);

public sealed record CheckoutSessionResult(
    string SessionId,
    string CheckoutUrl);

public sealed record PortalSessionResult(string PortalUrl);

public sealed record SubscriptionDetail(
    string    ModuleKey,
    string    Status,
    string    PlanType,
    DateTime  CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool      CancelAtPeriodEnd,
    string?   StripeSubscriptionId);

/// <summary>
/// Result returned by IStripeWebhookService.HandleAsync.
/// Lets the controller produce the correct HTTP response without touching Stripe SDK types.
/// </summary>
public sealed record WebhookHandleResult(
    bool SignatureValid,
    bool AlreadyProcessed);
