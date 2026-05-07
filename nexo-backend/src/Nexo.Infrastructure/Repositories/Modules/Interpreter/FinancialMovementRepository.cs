using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class FinancialMovementRepository : IFinancialMovementRepository
{
    private readonly NexoDbContext _context;

    public FinancialMovementRepository(NexoDbContext context) => _context = context;

    public async Task<FinancialMovement?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.IntMovements.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<FinancialMovement?> GetDraftByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.IntMovements
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == MovementStatus.Draft, ct);

    public async Task<IReadOnlyList<FinancialMovement>> GetByContextAsync(
        FinancialContextType contextType,
        Guid                 contextId,
        DateOnly?            from     = null,
        DateOnly?            to       = null,
        MovementStatus?      status   = null,
        int                  page     = 1,
        int                  pageSize = 20,
        CancellationToken    ct       = default)
    {
        var query = _context.IntMovements
            .Where(x => x.ContextType == contextType && x.ContextId == contextId);

        if (from.HasValue)   query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue)     query = query.Where(x => x.Date <= to.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        return await query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountByContextAsync(
        FinancialContextType contextType,
        Guid                 contextId,
        DateOnly?            from   = null,
        DateOnly?            to     = null,
        MovementStatus?      status = null,
        CancellationToken    ct     = default)
    {
        var query = _context.IntMovements
            .Where(x => x.ContextType == contextType && x.ContextId == contextId);

        if (from.HasValue)   query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue)     query = query.Where(x => x.Date <= to.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        return await query.CountAsync(ct);
    }

    public async Task AddAsync(FinancialMovement movement, CancellationToken ct = default)
        => await _context.IntMovements.AddAsync(movement, ct);

    public void Update(FinancialMovement movement)
        => _context.IntMovements.Update(movement);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
