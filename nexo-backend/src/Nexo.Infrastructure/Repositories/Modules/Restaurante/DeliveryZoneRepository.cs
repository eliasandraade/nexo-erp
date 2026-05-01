using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class DeliveryZoneRepository : IDeliveryZoneRepository
{
    private readonly NexoDbContext _db;
    public DeliveryZoneRepository(NexoDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeliveryZone>> GetAllAsync(CancellationToken ct = default)
        => await _db.DeliveryZones.AsNoTracking()
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DeliveryZone>> GetAllByStoreIdPublicAsync(
        Guid storeId, Guid tenantId, CancellationToken ct = default)
        => await _db.DeliveryZones.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.TenantId == tenantId)
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(ct);

    public Task<DeliveryZone?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.DeliveryZones.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(DeliveryZone zone) => _db.DeliveryZones.Add(zone);
    public void Remove(DeliveryZone zone) => _db.DeliveryZones.Remove(zone);
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
