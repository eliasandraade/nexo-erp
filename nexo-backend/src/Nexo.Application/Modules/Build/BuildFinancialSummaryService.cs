using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Read-only service that assembles a financial summary for a BuildProject.
///
/// Delegates all FinancialMovement querying to IBuildFinancialQueryService
/// (implemented in Nexo.Infrastructure). This service never imports EF Core
/// or FinancialMovement entities — clean dependency rule.
/// </summary>
public class BuildFinancialSummaryService
{
    private readonly IBuildProjectRepository    _projects;
    private readonly IBuildFinancialQueryService _financial;

    public BuildFinancialSummaryService(
        IBuildProjectRepository    projects,
        IBuildFinancialQueryService financial)
    {
        _projects  = projects;
        _financial = financial;
    }

    /// <summary>
    /// Contract total: the approved budget (already including the change orders approved while it existed) plus
    /// the approved change orders that could not be added to it. Null when there is neither.
    /// </summary>
    public static decimal? ContractTotal(decimal? budgetApproved, BuildFinancialSnapshot s)
        => budgetApproved is null && s.ApprovedChangeOrdersTotal == 0m
            ? null
            : (budgetApproved ?? 0m) + s.UnappliedChangeOrdersTotal;

    public async Task<BuildProjectFinancialSummaryDto> GetAsync(
        Guid              projectId,
        CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        var snapshot = await _financial.GetSnapshotAsync(projectId, ct);

        return new BuildProjectFinancialSummaryDto(
            ProjectId:              projectId,
            EstimatedBudget:        project.BudgetEstimated,
            ApprovedBudget:         project.BudgetApproved,
            TotalRealizedExpenses:  snapshot.TotalRealizedExpenses,
            MovementCount:          snapshot.MovementCount,
            LastMovementDate:       snapshot.LastMovementDate,
            VarianceAmount:         snapshot.VarianceAmount,
            VariancePercent:        snapshot.VariancePercent,
            ApprovedChangeOrdersTotal: snapshot.ApprovedChangeOrdersTotal,
            ContractTotal:          ContractTotal(project.BudgetApproved, snapshot));
    }
}
