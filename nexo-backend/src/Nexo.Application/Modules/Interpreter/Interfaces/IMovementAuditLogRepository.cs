using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IMovementAuditLogRepository
{
    Task<IReadOnlyList<MovementAuditLog>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task AddAsync(MovementAuditLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
