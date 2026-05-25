using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Products;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly NexoDbContext _context;

    public ProductRepository(NexoDbContext context) => _context = context;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Products.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Product?> GetActiveMenuItemAsync(Guid id, Guid storeId, CancellationToken ct = default)
        => await _context.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id
                                   && x.StoreId == storeId
                                   && x.IsActive
                                   && x.IsMenuVisible, ct);

    public async Task<IReadOnlyList<Product>> GetAllMenuItemsAsync(Guid storeId, Guid tenantId, CancellationToken ct = default)
        => await _context.Products
            .IgnoreQueryFilters()
            .Include(p => p.Category)
            .Where(p => p.StoreId == storeId && p.TenantId == tenantId && p.IsActive && p.IsMenuVisible)
            .OrderBy(p => p.Category != null ? p.Category.SortOrder : int.MaxValue)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<Product?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _context.Products.FirstOrDefaultAsync(x => x.Code == code.ToUpperInvariant(), ct);

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
        => await _context.Products.FirstOrDefaultAsync(x => x.Barcode == barcode, ct);

    public async Task<IReadOnlyList<Product>> GetAllAsync(
        bool includeInactive = false,
        bool? isIngredient = null,
        CancellationToken ct = default)
        => await _context.Products
            .AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .Where(x => isIngredient == null || x.IsIngredient == isIngredient)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<PagedResult<ProductDto>> GetPagedAsync(
        int page, int pageSize,
        string? search, bool includeInactive, bool? isIngredient,
        Guid? categoryId, string? unit,
        CancellationToken ct = default)
    {
        var q = _context.Products.AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .Where(x => isIngredient == null || x.IsIngredient == isIngredient);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x =>
                x.Name.ToLower().Contains(s) ||
                x.Code.ToLower().Contains(s) ||
                (x.Barcode != null && x.Barcode.ToLower().Contains(s)));
        }

        if (categoryId.HasValue)
            q = q.Where(x => x.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(unit) &&
            Enum.TryParse<ProductUnit>(unit, ignoreCase: true, out var unitEnum))
            q = q.Where(x => x.Unit == unitEnum);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(
                p.Id, p.Code, p.Barcode, p.Name, p.Description, p.CategoryId,
                p.Unit.ToString(), p.CostPrice, p.SalePrice, p.TrackStock,
                p.MinStockQuantity, p.MaxStockQuantity, p.IsActive,
                p.IsMenuVisible, p.IsIngredient, p.ImageUrl,
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<ProductDto>(items, total, page, pageSize);
    }

    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.Products.AnyAsync(
            x => x.Code == code.ToUpperInvariant() && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Product product, CancellationToken ct = default)
        => await _context.Products.AddAsync(product, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
