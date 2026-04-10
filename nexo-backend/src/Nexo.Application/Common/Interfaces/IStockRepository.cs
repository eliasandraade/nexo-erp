using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IStockRepository
{
    Task<StockItem?> GetByProductIdAsync(Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<StockItem>> GetAllAsync(CancellationToken ct = default);
    Task AddStockItemAsync(StockItem item, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovement>> GetMovementsByProductAsync(Guid productId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
