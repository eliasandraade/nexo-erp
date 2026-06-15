using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public sealed class ModuleSubscriptionRepository : IModuleSubscriptionRepository
{
    private readonly NexoDbContext _db;

    public ModuleSubscriptionRepository(NexoDbContext db) => _db = db;

    public async Task<IReadOnlyList<ModuleSubscription>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
        => await _db.ModuleSubscriptions
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);

    public async Task<ModuleSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken ct)
        => await _db.ModuleSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

    public async Task AddAsync(ModuleSubscription subscription, CancellationToken ct)
        => await _db.ModuleSubscriptions.AddAsync(subscription, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
