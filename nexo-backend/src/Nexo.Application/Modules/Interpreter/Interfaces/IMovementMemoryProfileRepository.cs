using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IMovementMemoryProfileRepository
{
    Task<MovementMemoryProfile?> GetAsync(Guid tenantId, Guid? userId, CancellationToken ct = default);
    Task AddAsync(MovementMemoryProfile profile, CancellationToken ct = default);
    void Update(MovementMemoryProfile profile);
    Task SaveChangesAsync(CancellationToken ct = default);
}
