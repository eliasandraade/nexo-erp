using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildBudgetRepository : IBuildBudgetRepository
{
    private readonly NexoDbContext _context;

    public BuildBudgetRepository(NexoDbContext context) => _context = context;

    public async Task<BuildBudget?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldBudgets
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>Loads budget with its Items collection.</summary>
    public async Task<BuildBudget?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.BldBudgets
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildBudget>> GetAllAsync(
        Guid?              projectId = null,
        BuildBudgetStatus? status    = null,
        int                page      = 1,
        int                pageSize  = 20,
        CancellationToken  ct        = default)
    {
        var query = _context.BldBudgets.AsQueryable();

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BuildBudget budget, CancellationToken ct = default)
        => await _context.BldBudgets.AddAsync(budget, ct);

    public void Update(BuildBudget budget)
        => _context.BldBudgets.Update(budget);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
