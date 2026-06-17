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
    Task AddAsync(SvcCatalogItem entity, CancellationToken ct = default);
    void Update(SvcCatalogItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
