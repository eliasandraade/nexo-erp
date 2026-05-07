using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Queries FinancialMovement (Core module) filtered by:
///   ContextType = Obra  AND  ContextId = projectId
///   Status      = Confirmed
///   Direction   = Out   (realized expenses only)
///
/// Build NEVER creates parallel financial records — it only reads from Core.
/// Tenant isolation is guaranteed by the global EF query filter on TenantEntity.
/// </summary>
public class BuildFinancialQueryService : IBuildFinancialQueryService
{
    private readonly NexoDbContext _context;

    public BuildFinancialQueryService(NexoDbContext context) => _context = context;

    public async Task<BuildFinancialSnapshot> GetSnapshotAsync(Guid projectId, CancellationToken ct = default)
    {
        // Load project budget figures (global query filter ensures tenant isolation)
        var project = await _context.BldProjects
            .Select(p => new { p.Id, p.BudgetEstimated, p.BudgetApproved })
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        var estimatedBudget = project?.BudgetEstimated;
        var approvedBudget  = project?.BudgetApproved;

        // Aggregate realized expenses from confirmed outbound movements
        // Global query filter handles tenant isolation — no manual TenantId check needed
        var stats = await _context.IntMovements
            .Where(m => m.ContextType == FinancialContextType.Obra
                     && m.ContextId   == projectId
                     && m.Status      == MovementStatus.Confirmed
                     && m.Direction   == MovementDirection.Out)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalExpenses = g.Sum(m => m.Amount),
                Count         = g.Count(),
                LastDate      = g.Max(m => (DateOnly?)m.Date)
            })
            .FirstOrDefaultAsync(ct);

        var totalExpenses    = stats?.TotalExpenses    ?? 0m;
        var movementCount    = stats?.Count            ?? 0;
        var lastMovementDate = stats?.LastDate;

        // Variance: how much budget remains after realized expenses
        // Positive = under budget, Negative = over budget
        var varianceAmount  = (approvedBudget ?? 0m) - totalExpenses;
        var variancePercent = approvedBudget is > 0m
            ? varianceAmount / approvedBudget.Value * 100m
            : 0m;

        return new BuildFinancialSnapshot(
            TotalRealizedExpenses: totalExpenses,
            MovementCount:         movementCount,
            LastMovementDate:      lastMovementDate,
            EstimatedBudget:       estimatedBudget,
            ApprovedBudget:        approvedBudget,
            VarianceAmount:        varianceAmount,
            VariancePercent:       variancePercent);
    }
}
