using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>Inventário (contagem física) and transferências between stores.</summary>
public interface IStockOperationsRepository
{
    /// <summary>Transaction-scoped advisory lock that serialises a sequence (count / transfer numbers).</summary>
    Task LockSequenceAsync(Guid scope, string sequence, CancellationToken ct = default);
    Task<int> GetNextCountNumberAsync(CancellationToken ct = default);
    Task AddCountAsync(InventoryCount count, CancellationToken ct = default);
    Task<InventoryCount?> GetCountAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<InventoryCount>> ListCountsAsync(CancellationToken ct = default);
    /// <summary>Row lock on the count (apply/cancel/count serialise).</summary>
    Task LockCountAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    Task<int> GetNextTransferNumberAsync(CancellationToken ct = default);
    Task AddTransferAsync(StockTransfer transfer, CancellationToken ct = default);
    /// <summary>Transfers where the store is origin or destination, newest first.</summary>
    Task<IReadOnlyList<StockTransfer>> ListTransfersAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Active products that track stock, for "count everything".</summary>
    Task<IReadOnlyList<Guid>> GetTrackedProductIdsAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Product>> GetProductsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    /// <summary>The store is an ACTIVE store of the current tenant.</summary>
    Task<bool> IsActiveStoreOfTenantAsync(Guid storeId, CancellationToken ct = default);
    Task<bool> HasOpenCountAsync(CancellationToken ct = default);
    /// <summary>
    /// Products of a SPECIFIC store of the current tenant by code — each store has its own catalogue,
    /// so a transfer maps the origin product to the destination's product with the same code.
    /// </summary>
    Task<IReadOnlyDictionary<string, Product>> GetProductsInStoreByCodeAsync(
        Guid tenantId, Guid storeId, IEnumerable<string> codes, CancellationToken ct = default);

    /// <summary>
    /// Locks (FOR UPDATE) the stock rows of these products in a store of the current tenant, in a
    /// deterministic order so two operations touching the same rows never deadlock.
    /// </summary>
    Task LockStockAsync(Guid tenantId, Guid storeId, IEnumerable<Guid> productIds, CancellationToken ct = default);

    /// <summary>
    /// Stock rows of a SPECIFIC store of the current tenant — the explicit, audited exception to the
    /// current-store query filter, used only by transfers (destination) and their checks.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, StockItem>> GetStockInStoreAsync(
        Guid tenantId, Guid storeId, IEnumerable<Guid> productIds, CancellationToken ct = default);

    Task AddStockItemAsync(StockItem item, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
