using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IReprocessLogRepository
{
    Task<ReprocessLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReprocessLog>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task AddAsync(ReprocessLog log, CancellationToken ct = default);
    void Update(ReprocessLog log);
    Task SaveChangesAsync(CancellationToken ct = default);
}
