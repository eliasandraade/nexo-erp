using Stripe;
using Nexo.Application.Integrations.Billing;

namespace Nexo.Infrastructure.Integrations.Stripe;

/// <summary>Reads a subscription from Stripe (v52: period dates live on the subscription item).</summary>
public sealed class StripeSubscriptionFetcher : IStripeSubscriptionFetcher
{
    public async Task<StripeSubscriptionSnapshot> GetAsync(string subscriptionId, CancellationToken ct = default)
    {
        var sub  = await new SubscriptionService().GetAsync(subscriptionId, cancellationToken: ct);
        var item = sub.Items?.Data?.FirstOrDefault();
        return new StripeSubscriptionSnapshot(
            sub.Id, sub.Status, item?.Price?.Id ?? string.Empty, item?.Price?.Recurring?.Interval,
            item?.CurrentPeriodStart ?? DateTime.UtcNow, item?.CurrentPeriodEnd, sub.CancelAtPeriodEnd);
    }
}
