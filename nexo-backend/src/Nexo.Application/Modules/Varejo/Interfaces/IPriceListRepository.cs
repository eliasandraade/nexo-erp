using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo.Interfaces;

public interface IPriceListRepository
{
    Task<RetPriceList?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RetPriceList?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<RetPriceList?> GetDefaultAsync(CancellationToken ct = default);
    /// <summary>Returns the price list linked to a specific customer (if any).</summary>
    Task<RetPriceList?> GetByCustomerIdAsync(Guid customerId, CancellationToken ct = default);
    Task<IReadOnlyList<RetPriceList>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task AddAsync(RetPriceList priceList, CancellationToken ct = default);
    /// <summary>New items of a loaded list must be marked Added (readonly backing field).</summary>
    void TrackItem(RetPriceListItem item);
    /// <summary>Every list flagged default, active or not (to keep a single default per tenant).</summary>
    Task<IReadOnlyList<RetPriceList>> GetAllDefaultsAsync(CancellationToken ct = default);
    Task<RetCustomerPriceList?> GetCustomerLinkAsync(Guid customerId, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid CustomerId, string Name)>> GetLinkedCustomersAsync(Guid priceListId, CancellationToken ct = default);
    Task AddCustomerLinkAsync(RetCustomerPriceList link, CancellationToken ct = default);
    void RemoveCustomerLink(RetCustomerPriceList link);
    Task<IReadOnlyDictionary<Guid, (string Name, string Code)>> GetProductLabelsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
