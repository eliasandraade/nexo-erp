using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcRecordEntry. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcRecordEntryRepository
{
    Task<SvcRecordEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcRecordEntry>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default);
    Task AddAsync(SvcRecordEntry entity, CancellationToken ct = default);
    void Remove(SvcRecordEntry entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
