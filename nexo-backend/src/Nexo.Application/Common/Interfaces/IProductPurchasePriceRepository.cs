using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IProductPurchasePriceRepository
{
    Task<IReadOnlyList<ProductPurchasePrice>> GetLastFiveAsync(Guid productId, CancellationToken ct = default);
    Task AddAsync(ProductPurchasePrice price, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
