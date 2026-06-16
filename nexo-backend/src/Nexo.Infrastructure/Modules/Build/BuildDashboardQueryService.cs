using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Assembles the Build dashboard read-model for the current tenant.
/// Realized expenses come exclusively from confirmed outbound FinancialMovements
/// with ContextType=Obra. Tenant isolation is enforced by the global EF query filter.
/// </summary>
public class BuildDashboardQueryService : IBuildDashboardQueryService
{
    private readonly NexoDbContext _context;

    public BuildDashboardQueryService(NexoDbContext context) => _context = context;

    public async Task<BuildDashboardDto> GetAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Project status counts + budget totals + overdue — single pass.
        var projects = await _context.BldProjects
            .Select(p => new { p.Status, p.ExpectedEndDate, p.BudgetEstimated, p.BudgetApproved })
            .ToListAsync(ct);

        int planning   = projects.Count(p => p.Status == BuildProjectStatus.Planning);
        int inProgress = projects.Count(p => p.Status == BuildProjectStatus.InProgress);
        int paused     = projects.Count(p => p.Status == BuildProjectStatus.Paused);
        int completed  = projects.Count(p => p.Status == BuildProjectStatus.Completed);
        int cancelled  = projects.Count(p => p.Status == BuildProjectStatus.Cancelled);
        int overdue    = projects.Count(p =>
            p.ExpectedEndDate != null
            && p.ExpectedEndDate < today
            && p.Status != BuildProjectStatus.Completed
            && p.Status != BuildProjectStatus.Cancelled);

        decimal totalEstimated = projects.Sum(p => p.BudgetEstimated ?? 0m);
        decimal totalApproved  = projects.Sum(p => p.BudgetApproved ?? 0m);

        // Realized expenses: tenant-wide confirmed Obra outbound movements.
        decimal totalRealized = await _context.IntMovements
            .Where(m => m.ContextType == FinancialContextType.Obra
                     && m.Status      == MovementStatus.Confirmed
                     && m.Direction   == MovementDirection.Out)
            .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;

        // Average stage progress across all stages (null-safe on empty).
        double avgProgress = await _context.BldStages
            .Select(s => (double?)s.ProgressPercent)
            .AverageAsync(ct) ?? 0d;

        // Recent expenses: last 5 confirmed Obra outbound movements, with project name.
        var recent = await (
            from m in _context.IntMovements
            join p in _context.BldProjects on m.ContextId equals (Guid?)p.Id
            where m.ContextType == FinancialContextType.Obra
               && m.Status      == MovementStatus.Confirmed
               && m.Direction   == MovementDirection.Out
            orderby m.Date descending, m.CreatedAt descending
            select new BuildRecentExpenseDto(p.Id, p.Name, m.Amount, m.Date, m.Description))
            .Take(5)
            .ToListAsync(ct);

        return new BuildDashboardDto(
            TotalProjects:    projects.Count,
            PlanningCount:    planning,
            InProgressCount:  inProgress,
            PausedCount:      paused,
            CompletedCount:   completed,
            CancelledCount:   cancelled,
            OverdueCount:     overdue,
            TotalEstimated:   totalEstimated,
            TotalApproved:    totalApproved,
            TotalRealized:    totalRealized,
            Balance:          totalApproved - totalRealized,
            AvgStageProgress: Math.Round(avgProgress, 1),
            RecentExpenses:   recent);
    }
}
