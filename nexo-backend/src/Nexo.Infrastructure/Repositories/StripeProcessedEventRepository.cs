using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public sealed class StripeProcessedEventRepository : IStripeProcessedEventRepository
{
    private readonly NexoDbContext _db;

    public StripeProcessedEventRepository(NexoDbContext db) => _db = db;

    public async Task<bool> TryRecordAsync(StripeProcessedEvent evt, CancellationToken ct)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO nexo.stripe_processed_events (id, stripe_event_id, event_type, created_at, updated_at)
            VALUES ({evt.Id}, {evt.StripeEventId}, {evt.EventType}, {evt.CreatedAt}, {evt.UpdatedAt})
            ON CONFLICT (stripe_event_id) DO NOTHING
            """, ct) == 1;
}
