using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Records each Stripe webhook event ID that has been successfully processed.
/// Used to enforce idempotency: before processing an event, check this table;
/// after processing, insert a record. Prevents duplicate side effects on webhook retries.
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
