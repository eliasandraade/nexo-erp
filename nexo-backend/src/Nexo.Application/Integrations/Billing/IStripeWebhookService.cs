namespace Nexo.Application.Integrations.Billing;

public interface IStripeWebhookService
{
    /// <summary>
    /// Validates Stripe webhook signature, enforces idempotency, and processes the event.
    /// All Stripe SDK types are contained within the Infrastructure implementation —
    /// this interface is SDK-agnostic.
    /// </summary>
    Task<WebhookHandleResult> HandleAsync(
        string rawBody,
        string signature,
        CancellationToken ct = default);
}
