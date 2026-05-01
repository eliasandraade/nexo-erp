using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class DeliveryZoneService
{
    private readonly IDeliveryZoneRepository _repo;
    private readonly ICurrentTenant          _currentTenant;
    private readonly IStoreRepository        _stores;

    public DeliveryZoneService(
        IDeliveryZoneRepository repo,
        ICurrentTenant          currentTenant,
        IStoreRepository        stores)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
        _stores        = stores;
    }

    public async Task<IReadOnlyList<DeliveryZoneDto>> GetAllAsync(CancellationToken ct = default)
    {
        var zones = await _repo.GetAllAsync(ct);
        return zones.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DeliveryZoneDto>> GetAllBySlugPublicAsync(
        string slug, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(slug, ct)
            ?? throw new NotFoundException("Store", slug);
        var zones = await _repo.GetAllByStoreIdPublicAsync(store.Id, store.TenantId, ct);
        return zones.Select(Map).ToList();
    }

    /// <summary>
    /// Bulk replace: removes all existing zones for the store and inserts the new set.
    /// Empty list = disable delivery.
    /// </summary>
    public async Task<IReadOnlyList<DeliveryZoneDto>> UpsertAsync(
        UpsertDeliveryZonesRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.GetAllAsync(ct);
        foreach (var z in existing)
            _repo.Remove(z);

        var created = new List<DeliveryZone>();
        foreach (var item in request.Zones)
        {
            var zone = DeliveryZone.Create(_currentTenant.Id, item.Neighborhood, item.Fee);
            _repo.Add(zone);
            created.Add(zone);
        }

        await _repo.SaveChangesAsync(ct);
        return created.Select(Map).ToList();
    }

    private static DeliveryZoneDto Map(DeliveryZone z) => new(z.Id, z.Neighborhood, z.Fee);
}
