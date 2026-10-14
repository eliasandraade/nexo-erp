using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo.Interfaces;

public interface IPurchaseRepository
{
    Task<RetPurchase?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RetPurchase?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RetPurchase>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    /// <summary>Row lock on the purchase (order / receive / cancel serialise).</summary>
    Task LockAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    /// <summary>Serialises numbering of the current store's purchases (transaction-scoped).</summary>
    Task LockNumberingAsync(Guid storeId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, (string Name, string Code)>> GetProductLabelsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetSupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task AddAsync(RetPurchase purchase, CancellationToken ct = default);
    /// <summary>Tracks a new item as Added — EF marks children with preset Guids found in a
    /// readonly backing field as Modified, which turns the INSERT into a no-op UPDATE.</summary>
    void TrackItem(RetPurchaseItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
