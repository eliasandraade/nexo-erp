using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>
/// Repository for SvcCatalogItem. Tenant + store isolation is enforced by the EF global
/// query filter — implementations never filter by tenant/store manually nor call IgnoreQueryFilters().
/// </summary>
public interface ISvcCatalogItemRepository
{
    Task<SvcCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcCatalogItem>> GetAllAsync(bool onlyActive = false, CancellationToken ct = default);

    /// <summary>Public-path read: active catalog items for a slug-resolved store (bypasses query filter).</summary>
    Task<IReadOnlyList<SvcCatalogItem>> GetActivePublicAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);

    /// <summary>Public-path read: a single catalog item scoped to the resolved store, or null.</summary>
    Task<SvcCatalogItem?> GetByIdPublicAsync(
        Guid id, Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task AddAsync(SvcCatalogItem entity, CancellationToken ct = default);
    void Update(SvcCatalogItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
