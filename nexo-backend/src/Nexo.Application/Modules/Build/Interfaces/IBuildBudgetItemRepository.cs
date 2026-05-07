using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildBudgetItemRepository
{
    Task<BuildBudgetItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BuildBudgetItem>> GetByBudgetAsync(Guid budgetId, CancellationToken ct = default);

    Task AddAsync(BuildBudgetItem item, CancellationToken ct = default);
    void Update(BuildBudgetItem item);
    void Remove(BuildBudgetItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
