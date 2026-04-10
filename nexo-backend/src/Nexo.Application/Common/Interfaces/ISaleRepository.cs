using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Sale?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<Sale?> GetByNumberAsync(int number, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
    Task AddPaymentAsync(SalePayment payment, CancellationToken ct = default);
    /// <summary>Explicitly tracks a new sale item as Added. Required because EF Core assigns
    /// Modified (not Added) to entities with non-sentinel Guids found in readonly backing fields.</summary>
    void TrackItem(SaleItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
