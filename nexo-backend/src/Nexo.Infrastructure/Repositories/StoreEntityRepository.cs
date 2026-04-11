using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Query;

namespace Nexo.Infrastructure.Repositories;

/// <summary>
/// Store repository — bypasses Global Query Filters so it can be used in
/// auth flows (login, switch-store) before the store context is resolved.
/// </summary>
public class StoreEntityRepository : IStoreRepository
{
    private readonly NexoDbContext _context;

    public StoreEntityRepository(NexoDbContext context) => _context = context;

    public async Task<Store?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Store>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        => await _context.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status == StoreStatus.Active)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Store>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
        => await _context.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.ModuleSubscription)
            .Where(s => s.TenantId == tenantId
                     && s.Status == StoreStatus.Active
                     && ids.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Store store, CancellationToken ct = default)
        => await _context.Stores.AddAsync(store, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
