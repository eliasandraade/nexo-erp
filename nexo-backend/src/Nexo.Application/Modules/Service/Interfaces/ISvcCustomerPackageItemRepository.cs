using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcCustomerPackageItem (consumable balances).</summary>
public interface ISvcCustomerPackageItemRepository
{
    Task<IReadOnlyList<SvcCustomerPackageItem>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default);
    Task AddAsync(SvcCustomerPackageItem entity, CancellationToken ct = default);
    void Update(SvcCustomerPackageItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
