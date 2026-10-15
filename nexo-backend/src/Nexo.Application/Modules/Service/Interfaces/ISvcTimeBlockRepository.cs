using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

public interface ISvcTimeBlockRepository
{
    Task<IReadOnlyList<SvcTimeBlock>> GetRangeAsync(DateTime fromUtc, DateTime toUtc, Guid? professionalId, CancellationToken ct = default);
    Task<SvcTimeBlock?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(SvcTimeBlock block, CancellationToken ct = default);
    void Remove(SvcTimeBlock block);
    Task SaveChangesAsync(CancellationToken ct = default);
}
