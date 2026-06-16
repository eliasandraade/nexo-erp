using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Stock;
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
            .AsNoTracking()
            .Include(x => x.Product)
            .OrderBy(x => x.Product!.Name)
            .ToListAsync(ct);

    public async Task<StockPagedResponse> GetPagedAsync(
        int page, int pageSize, string? search, string? status,
        CancellationToken ct = default)
    {
        const int staleDays = 14;
        var staleCutoff = DateTime.UtcNow.AddDays(-staleDays);

        // Filtered query — navigation properties are auto-joined by EF Core
        var q = _context.StockItems.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.Product!.Name.ToLower().Contains(s) ||
                x.Product.Code.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            // NOTE: AvailableQuantity is a computed C# property (CurrentQuantity -
            // ReservedQuantity) and is unmapped, so EF Core cannot translate it to
            // SQL. Use the raw column arithmetic so the predicate runs in the DB.
            q = status switch
            {
                "zero"   => q.Where(x => x.CurrentQuantity - x.ReservedQuantity <= 0),
                "low"    => q.Where(x => x.CurrentQuantity - x.ReservedQuantity > 0
                                      && x.Product!.MinStockQuantity != null
                                      && x.CurrentQuantity - x.ReservedQuantity < x.Product.MinStockQuantity),
                "normal" => q.Where(x => x.CurrentQuantity - x.ReservedQuantity > 0
                                      && (x.Product!.MinStockQuantity == null
                                          || x.CurrentQuantity - x.ReservedQuantity >= x.Product.MinStockQuantity)),
                _ => q,
            };
        }

        // Run filtered count + KPI counts sequentially (DbContext is not thread-safe)
        var total = await q.CountAsync(ct);

        var baseQ = _context.StockItems.AsNoTracking();
        var belowMinCount = await baseQ.CountAsync(x =>
            x.CurrentQuantity - x.ReservedQuantity <= 0 ||
            (x.Product!.MinStockQuantity != null &&
             x.CurrentQuantity - x.ReservedQuantity > 0 &&
             x.CurrentQuantity - x.ReservedQuantity < x.Product.MinStockQuantity), ct);
        var noTurnoverCount = await baseQ.CountAsync(x =>
            x.LastMovementAt == null || x.LastMovementAt < staleCutoff, ct);

        var items = await q
            .OrderBy(x => x.Product!.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StockPagedItemDto(
                s.Id,
                s.ProductId,
                s.Product!.Name,
                s.Product.Code,
                s.Product.Unit.ToString(),
                s.Product.CategoryId,
                s.Product.Category != null ? s.Product.Category.Name : null,
                s.Product.MinStockQuantity,
                s.CurrentQuantity,
                s.ReservedQuantity,
                s.CurrentQuantity - s.ReservedQuantity,
                s.LastMovementAt))
            .ToListAsync(ct);

        return new StockPagedResponse(items, total, page, pageSize, belowMinCount, noTurnoverCount);
    }

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
