using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IFinancialMovementRepository
{
    Task<FinancialMovement?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FinancialMovement?> GetDraftByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FinancialMovement>> GetByContextAsync(
        FinancialContextType contextType,
        Guid                 contextId,
        DateOnly?            from     = null,
        DateOnly?            to       = null,
        MovementStatus?      status   = null,
        int                  page     = 1,
        int                  pageSize = 20,
        CancellationToken    ct       = default);

    Task<int> CountByContextAsync(
        FinancialContextType contextType,
        Guid                 contextId,
        DateOnly?            from   = null,
        DateOnly?            to     = null,
        MovementStatus?      status = null,
        CancellationToken    ct     = default);

    Task AddAsync(FinancialMovement movement, CancellationToken ct = default);
    void Update(FinancialMovement movement);
    Task SaveChangesAsync(CancellationToken ct = default);
}
