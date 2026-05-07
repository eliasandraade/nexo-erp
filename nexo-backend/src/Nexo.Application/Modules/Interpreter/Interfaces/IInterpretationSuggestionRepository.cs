using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IInterpretationSuggestionRepository
{
    Task<InterpretationSuggestion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InterpretationSuggestion?> GetLatestByMovementIdAsync(Guid movementId, CancellationToken ct = default);
    Task AddAsync(InterpretationSuggestion suggestion, CancellationToken ct = default);
    void Update(InterpretationSuggestion suggestion);
    Task SaveChangesAsync(CancellationToken ct = default);
}
