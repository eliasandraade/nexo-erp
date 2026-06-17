using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcSubject. Tenant isolation is enforced by the EF global query filter.</summary>
public interface ISvcSubjectRepository
{
    Task<SvcSubject?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcSubject>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default);
    Task AddAsync(SvcSubject entity, CancellationToken ct = default);
    void Update(SvcSubject entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
