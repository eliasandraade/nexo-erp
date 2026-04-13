using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IOrderRepository
{
    Task<RestOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RestOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<RestOrder?> GetOpenOrderForTableAsync(Guid tableId, CancellationToken ct = default);
    Task<IReadOnlyList<RestOrder>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    Task AddAsync(RestOrder order, CancellationToken ct = default);
    /// <summary>Explicitly tracks a new order item as Added. Required because EF Core assigns
    /// Modified (not Added) to entities with non-sentinel Guids found in readonly backing fields.</summary>
    void TrackItem(RestOrderItem item);
    /// <summary>Tracks a new modifier snapshot as Added (same pattern as TrackItem).</summary>
    void TrackModifier(RestOrderItemModifier modifier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
