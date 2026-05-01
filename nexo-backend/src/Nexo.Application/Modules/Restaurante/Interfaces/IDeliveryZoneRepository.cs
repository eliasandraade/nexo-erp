using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IDeliveryZoneRepository
{
    Task<IReadOnlyList<DeliveryZone>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Public: bypasses query filters. Resolves by storeId + tenantId directly.</summary>
    Task<IReadOnlyList<DeliveryZone>> GetAllByStoreIdPublicAsync(
        Guid storeId, Guid tenantId, CancellationToken ct = default);

    Task<DeliveryZone?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(DeliveryZone zone);
    void Remove(DeliveryZone zone);
    Task SaveChangesAsync(CancellationToken ct = default);
}
