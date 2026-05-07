using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildBudgetItemRepository : IBuildBudgetItemRepository
{
    private readonly NexoDbContext _context;

    public BuildBudgetItemRepository(NexoDbContext context) => _context = context;

    public async Task<BuildBudgetItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldBudgetItems
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildBudgetItem>> GetByBudgetAsync(Guid budgetId, CancellationToken ct = default)
        => await _context.BldBudgetItems
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(BuildBudgetItem item, CancellationToken ct = default)
        => await _context.BldBudgetItems.AddAsync(item, ct);

    public void Update(BuildBudgetItem item)
        => _context.BldBudgetItems.Update(item);

    public void Remove(BuildBudgetItem item)
        => _context.BldBudgetItems.Remove(item);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
