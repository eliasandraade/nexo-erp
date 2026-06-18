using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for the SvcPackage template aggregate. Tenant + store isolation via the EF global query filter.</summary>
public interface ISvcPackageRepository
{
    Task<SvcPackage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SvcPackage?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPackage>> GetAllAsync(bool? active, CancellationToken ct = default);
    Task AddAsync(SvcPackage entity, CancellationToken ct = default);
    void Update(SvcPackage entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
