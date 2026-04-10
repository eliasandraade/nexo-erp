using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo.Interfaces;

public interface IPurchaseRepository
{
    Task<RetPurchase?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RetPurchase?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RetPurchase>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    Task AddAsync(RetPurchase purchase, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
