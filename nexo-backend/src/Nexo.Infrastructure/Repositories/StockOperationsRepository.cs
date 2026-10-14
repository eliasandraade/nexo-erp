using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class StockOperationsRepository : IStockOperationsRepository
{
    private readonly NexoDbContext _context;

    public StockOperationsRepository(NexoDbContext context) => _context = context;

    public async Task LockSequenceAsync(Guid scope, string sequence, CancellationToken ct = default)
    {
        // Stable across processes (string.GetHashCode is randomised per process).
        long salt = sequence switch
        {
            "inventory-count" => 0x1C0_0000_0000_0001,
            "stock-transfer"  => 0x1C0_0000_0000_0002,
            _ => throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Unknown sequence."),
        };
        var key = BitConverter.ToInt64(scope.ToByteArray(), 0) ^ salt;
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    public async Task<int> GetNextCountNumberAsync(CancellationToken ct = default)
        => (await _context.InventoryCounts.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;

    public async Task AddCountAsync(InventoryCount count, CancellationToken ct = default)
    {
        await _context.InventoryCounts.AddAsync(count, ct);
        foreach (var item in count.Items) _context.Entry(item).State = EntityState.Added;
    }

    public async Task<InventoryCount?> GetCountAsync(Guid id, CancellationToken ct = default)
        => await _context.InventoryCounts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<InventoryCount>> ListCountsAsync(CancellationToken ct = default)
        => await _context.InventoryCounts.Include(c => c.Items)
            .OrderByDescending(c => c.Number).Take(100).ToListAsync(ct);

    public async Task LockCountAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.inventory_counts WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE", ct);
        var tracked = _context.ChangeTracker.Entries<InventoryCount>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(ct);
    }

    public async Task<int> GetNextTransferNumberAsync(CancellationToken ct = default)
        => (await _context.StockTransfers.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;

    public async Task AddTransferAsync(StockTransfer transfer, CancellationToken ct = default)
    {
        await _context.StockTransfers.AddAsync(transfer, ct);
        foreach (var item in transfer.Items) _context.Entry(item).State = EntityState.Added;
    }

    public async Task<IReadOnlyList<StockTransfer>> ListTransfersAsync(Guid storeId, CancellationToken ct = default)
        => await _context.StockTransfers.Include(t => t.Items)
            .Where(t => t.FromStoreId == storeId || t.ToStoreId == storeId)
            .OrderByDescending(t => t.Number).Take(100).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetTrackedProductIdsAsync(CancellationToken ct = default)
        => await _context.Products.Where(p => p.IsActive && p.TrackStock)
            .OrderBy(p => p.Name).Select(p => p.Id).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, Product>> GetProductsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return await _context.Products.Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<bool> IsActiveStoreOfTenantAsync(Guid storeId, CancellationToken ct = default)
        => await _context.Stores.AnyAsync(s => s.Id == storeId && s.Status == Nexo.Domain.Enums.StoreStatus.Active, ct);   // tenant filter applies

    public async Task<bool> HasOpenCountAsync(CancellationToken ct = default)
        => await _context.InventoryCounts.AnyAsync(c => c.Status == InventoryCountStatus.Open, ct);

    public async Task<IReadOnlyDictionary<string, Product>> GetProductsInStoreByCodeAsync(
        Guid tenantId, Guid storeId, IEnumerable<string> codes, CancellationToken ct = default)
    {
        var list = codes.Distinct().ToList();
        // Explicit cross-store read, pinned to tenant + store (authorised by the caller).
        var products = await _context.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && list.Contains(p.Code))
            .ToListAsync(ct);
        return products.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
    }

    public async Task LockStockAsync(Guid tenantId, Guid storeId, IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return;

        // Serialise stock operations of this store: FOR UPDATE can't lock a row that doesn't exist
        // yet, and a transfer/count may have to create the store's stock row for a product.
        // Rows are locked in primary-key order — the order EF issues a sale's UPDATEs in — so a
        // multi-item sale and a stock operation can't deadlock on each other.
        var storeKey = BitConverter.ToInt64(storeId.ToByteArray(), 8);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({storeKey})", ct);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.stock_items WHERE tenant_id = {tenantId} AND store_id = {storeId} AND product_id = ANY({ids}) ORDER BY id FOR UPDATE", ct);
        foreach (var tracked in _context.ChangeTracker.Entries<StockItem>()
                     .Where(e => e.Entity.StoreId == storeId && ids.Contains(e.Entity.ProductId)).ToList())
            await tracked.ReloadAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, StockItem>> GetStockInStoreAsync(
        Guid tenantId, Guid storeId, IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToList();
        // Explicit cross-store read: the global filter only sees the current store. Tenant and store
        // are both pinned here; callers authorise the store first.
        return await _context.StockItems.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.StoreId == storeId && ids.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, ct);
    }

    public async Task AddStockItemAsync(StockItem item, CancellationToken ct = default)
        => await _context.StockItems.AddAsync(item, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default)
        => await _context.StockMovements.AddAsync(movement, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
