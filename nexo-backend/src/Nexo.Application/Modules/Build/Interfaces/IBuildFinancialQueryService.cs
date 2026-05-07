namespace Nexo.Application.Modules.Build.Interfaces;

/// <summary>
/// Read-only port for querying financial data scoped to a BuildProject.
/// Implementation lives in Nexo.Infrastructure and queries FinancialMovement
/// with ContextType = Obra and ContextId = projectId.
/// Build Application layer never takes a hard dependency on FinancialMovement entities.
/// </summary>
public interface IBuildFinancialQueryService
{
    Task<BuildFinancialSnapshot> GetSnapshotAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>
/// Value object returned by IBuildFinancialQueryService.
/// All monetary values are in BRL (two decimal places).
/// </summary>
public sealed record BuildFinancialSnapshot(
    decimal  TotalRealizedExpenses,
    int      MovementCount,
    DateOnly? LastMovementDate,
    decimal? EstimatedBudget,
    decimal? ApprovedBudget,
    decimal  VarianceAmount,      // ApprovedBudget - TotalRealizedExpenses (null-safe; 0 when budgets absent)
    decimal  VariancePercent);    // VarianceAmount / ApprovedBudget * 100 (0 when budget = 0)
