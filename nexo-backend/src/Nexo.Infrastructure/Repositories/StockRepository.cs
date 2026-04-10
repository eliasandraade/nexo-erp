using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class StockRepository : IStockRepository
{
    private readonly NexoDbContext _context;

    public StockRepository(NexoDbContext context) => _context = context;

    public async Task<StockItem?> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
        => await _context.StockItems.FirstOrDefaultAsync(x => x.ProductId == productId, ct);

    public async Task<IReadOnlyList<StockItem>> GetAllAsync(CancellationToken ct = default)
        => await _context.StockItems
            .Include(x => x.Product)
            .OrderBy(x => x.Product!.Name)
            .ToListAsync(ct);

    public async Task AddStockItemAsync(StockItem item, CancellationToken ct = default)
        => await _context.StockItems.AddAsync(item, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default)
        => await _context.StockMovements.AddAsync(movement, ct);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsByProductAsync(Guid productId, CancellationToken ct = default)
        => await _context.StockMovements
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
