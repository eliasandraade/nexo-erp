using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IExtractionResultRepository
{
    Task<ExtractionResult?> GetLatestByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task<IReadOnlyList<ExtractionResult>> GetAllByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task AddAsync(ExtractionResult result, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
