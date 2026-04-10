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
    Task SaveChangesAsync(CancellationToken ct = default);
}
