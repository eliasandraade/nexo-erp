using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>
/// Repository for SvcProfessional. Tenant + store isolation is enforced by the EF global
/// query filter — implementations never filter by tenant/store manually nor call IgnoreQueryFilters().
/// </summary>
public interface ISvcProfessionalRepository
{
    Task<SvcProfessional?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcProfessional>> GetAllAsync(bool onlyActive = false, CancellationToken ct = default);
    Task AddAsync(SvcProfessional entity, CancellationToken ct = default);
    void Update(SvcProfessional entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
