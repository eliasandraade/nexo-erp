using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Records each Stripe webhook event ID that has been successfully processed.
/// Used to enforce idempotency: the record and the event's effects commit in one transaction,
/// so a recorded event was applied, and a failed one leaves no record and can be delivered again.
/// </summary>
public class StripeProcessedEvent : BaseEntity
{
    private StripeProcessedEvent() { } // EF Core constructor

    public string StripeEventId { get; private set; } = string.Empty;
    public string EventType     { get; private set; } = string.Empty;

    public static StripeProcessedEvent Create(string stripeEventId, string eventType)
        => new()
        {
            StripeEventId = stripeEventId,
            EventType     = eventType,
        };
}
