using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Bypasses query filters. Used by the public portal where no auth context is set.
    /// Returns only active + menu-visible products belonging to the given store.
    /// </summary>
    Task<Product?> GetActiveMenuItemAsync(Guid id, Guid storeId, CancellationToken ct = default);
    Task<Product?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
