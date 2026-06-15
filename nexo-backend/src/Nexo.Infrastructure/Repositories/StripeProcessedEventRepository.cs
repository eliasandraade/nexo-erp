using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public sealed class StripeProcessedEventRepository : IStripeProcessedEventRepository
{
    private readonly NexoDbContext _db;

    public StripeProcessedEventRepository(NexoDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(string stripeEventId, CancellationToken ct)
        => await _db.StripeProcessedEvents.AnyAsync(e => e.StripeEventId == stripeEventId, ct);

    public async Task AddAsync(StripeProcessedEvent evt, CancellationToken ct)
        => await _db.StripeProcessedEvents.AddAsync(evt, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
