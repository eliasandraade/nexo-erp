using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IMovementAttachmentRepository
{
    Task<MovementAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MovementAttachment>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task AddAsync(MovementAttachment attachment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
