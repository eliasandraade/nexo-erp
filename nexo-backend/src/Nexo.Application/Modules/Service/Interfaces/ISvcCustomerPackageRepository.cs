using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for the SvcCustomerPackage aggregate (assigned packages + balances).</summary>
public interface ISvcCustomerPackageRepository
{
    Task<SvcCustomerPackage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SvcCustomerPackage?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcCustomerPackage>> GetAllAsync(
        Guid? customerId, Guid? subjectId, SvcCustomerPackageStatus? status, Guid? packageId, CancellationToken ct = default);
    Task AddAsync(SvcCustomerPackage entity, CancellationToken ct = default);
    void Update(SvcCustomerPackage entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
