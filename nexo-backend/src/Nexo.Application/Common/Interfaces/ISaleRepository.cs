using Nexo.Application.Common;
using Nexo.Application.Features.Sales;
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Row lock (SELECT … FOR UPDATE) on the sale for the current transaction, so two confirmations
    /// of the same sale serialise instead of both seeing Draft.
    /// </summary>
    Task LockAsync(Guid id, CancellationToken ct = default);
    Task<Sale?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<Sale?> GetByNumberAsync(int number, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetAllAsync(CancellationToken ct = default);
    Task<PagedResult<SaleListItemDto>> GetPagedAsync(int page, int pageSize, string? search, string? status, string? paymentMethod, CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
    Task AddPaymentAsync(SalePayment payment, CancellationToken ct = default);
    /// <summary>Explicitly tracks a new sale item as Added. Required because EF Core assigns
    /// Modified (not Added) to entities with non-sentinel Guids found in readonly backing fields.</summary>
    void TrackItem(SaleItem item);
    Task AddReturnAsync(SaleReturn saleReturn, CancellationToken ct = default);
    Task<SaleReturn?> GetReturnByIdempotencyKeyAsync(Guid key, CancellationToken ct = default);
    /// <summary>Devoluções of a sale, oldest first, with their items.</summary>
    Task<IReadOnlyList<SaleReturn>> GetReturnsBySaleAsync(Guid saleId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
