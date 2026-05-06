using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class ProductPurchasePriceRepository : IProductPurchasePriceRepository
{
    private readonly NexoDbContext _context;
    public ProductPurchasePriceRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductPurchasePrice>> GetLastFiveAsync(
        Guid productId, CancellationToken ct = default)
        => await _context.ProductPurchasePrices
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.PurchasedAt)
            .Take(5)
            .ToListAsync(ct);

    public async Task AddAsync(ProductPurchasePrice price, CancellationToken ct = default)
        => await _context.ProductPurchasePrices.AddAsync(price, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
