using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IStripeProcessedEventRepository
{
    Task<bool> ExistsAsync(string stripeEventId, CancellationToken ct = default);
    Task AddAsync(StripeProcessedEvent evt, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
