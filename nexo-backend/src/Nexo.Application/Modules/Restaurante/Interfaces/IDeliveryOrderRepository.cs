using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IDeliveryOrderRepository
{
    Task<RestDeliveryOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RestDeliveryOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<RestDeliveryOrder?> GetByTrackingTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Bypasses tenant/store query filters. Required for the public tracking endpoint
    /// where no auth context is available (ICurrentTenant.IsResolved = false → filter = Guid.Empty).
    /// </summary>
    Task<RestDeliveryOrder?> GetByTrackingTokenPublicAsync(string token, CancellationToken ct = default);

    Task<RestDeliveryOrder?> GetByRestOrderIdAsync(Guid restOrderId, CancellationToken ct = default);
    Task<IReadOnlyList<RestDeliveryOrder>> GetAllAsync(
        DeliveryOrderStatus[]? statuses = null,
        DeliveryChannel[]? channels = null,
        DateOnly? date = null,
        CancellationToken ct = default);
    Task<int> GetNextOrderNumberAsync(CancellationToken ct = default);

    /// <summary>
    /// Portal variant: bypasses query filters, scopes to explicit tenant+store.
    /// Used when no JWT context is resolved (public portal creates DeliveryOrder).
    /// </summary>
    Task<int> GetNextOrderNumberForStoreAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task AddAsync(RestDeliveryOrder order, CancellationToken ct = default);

    /// <summary>Detaches the entity from the EF change tracker so a retry can re-add it.</summary>
    void Detach(RestDeliveryOrder order);

    /// <summary>
    /// Tracks new item as Added. Necessário porque EF Core atribui Modified a entidades
    /// com Guids não-sentinela encontradas em backing fields readonly.
    /// </summary>
    void TrackItem(RestDeliveryOrderItem item);
    void TrackModifier(RestDeliveryOrderItemModifier modifier);

    Task SaveChangesAsync(CancellationToken ct = default);
}
