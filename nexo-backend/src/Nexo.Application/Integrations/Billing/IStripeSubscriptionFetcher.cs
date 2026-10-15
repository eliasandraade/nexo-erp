namespace Nexo.Application.Integrations.Billing;

/// <summary>What the checkout handler needs from a Stripe subscription (read live from Stripe).</summary>
public sealed record StripeSubscriptionSnapshot(
    string Id, string Status, string PriceId, string? Interval, DateTime PeriodStart, DateTime? PeriodEnd, bool CancelAtPeriodEnd);

public interface IStripeSubscriptionFetcher
{
    Task<StripeSubscriptionSnapshot> GetAsync(string subscriptionId, CancellationToken ct = default);
}
