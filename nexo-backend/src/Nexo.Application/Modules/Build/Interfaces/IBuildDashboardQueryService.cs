namespace Nexo.Application.Modules.Build.Interfaces;

/// <summary>
/// Read-only port that assembles the Build dashboard read-model for the current
/// tenant. Implementation lives in Nexo.Infrastructure and joins BuildProject /
/// BuildStage data with FinancialMovement (ContextType=Obra) aggregates.
/// The Build Application layer never takes a hard dependency on FinancialMovement.
/// </summary>
public interface IBuildDashboardQueryService
{
    Task<BuildDashboardDto> GetAsync(CancellationToken ct = default);
}
