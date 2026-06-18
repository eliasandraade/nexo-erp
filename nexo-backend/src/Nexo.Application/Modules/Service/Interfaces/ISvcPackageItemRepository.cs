using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcPackageItem (template lines). Tenant + store isolation via the EF global query filter.</summary>
public interface ISvcPackageItemRepository
{
    Task<SvcPackageItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPackageItem>> GetByPackageAsync(Guid packageId, CancellationToken ct = default);
    Task<bool> ExistsForCatalogAsync(Guid packageId, Guid catalogItemId, CancellationToken ct = default);
    Task AddAsync(SvcPackageItem entity, CancellationToken ct = default);
    void Update(SvcPackageItem entity);
    void Remove(SvcPackageItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
