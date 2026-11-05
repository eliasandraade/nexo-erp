using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IStripeProcessedEventRepository
{
    /// <summary>
    /// Records the event inside the current transaction; false when it is already recorded. A delivery of the same event
    /// still in progress makes this wait until that transaction ends, so false means it really committed.
    /// </summary>
    Task<bool> TryRecordAsync(StripeProcessedEvent evt, CancellationToken ct = default);
}
