using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildBudgetRepository
{
    Task<BuildBudget?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads budget with its Items collection.</summary>
    Task<BuildBudget?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BuildBudget>> GetAllAsync(
        Guid?               projectId = null,
        BuildBudgetStatus?  status    = null,
        int                 page      = 1,
        int                 pageSize  = 20,
        CancellationToken   ct        = default);

    Task AddAsync(BuildBudget budget, CancellationToken ct = default);
    void Update(BuildBudget budget);
    Task SaveChangesAsync(CancellationToken ct = default);
}
