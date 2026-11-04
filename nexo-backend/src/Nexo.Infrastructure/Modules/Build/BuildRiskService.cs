using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Deterministic delay and cost risk of a project (<see cref="BuildRiskAnalyzer"/>), always available — no AI, no flag: the
/// numbers come from the Wave 1 progress engine and cost per stage, Wave 2 materials and change orders, Wave 3
/// non-conformities. The Build AI may only explain these items (POST …/risk/explain).
/// </summary>
public sealed class BuildRiskService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildProgressEngine _engine;
    private readonly BuildCostService _cost;

    public BuildRiskService(NexoDbContext db, ICurrentTenant tenant, BuildProgressEngine engine, BuildCostService cost)
    {
        _db = db;
        _tenant = tenant;
        _engine = engine;
        _cost = cost;
    }

    private Guid Tid => _tenant.Id;

    public async Task<BuildRiskAnalysisDto> DelayAsync(Guid projectId, CancellationToken ct)
    {
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(Tid, projectId, today, bypassFilters: false, ct)
                ?? throw new Nexo.Domain.Exceptions.NotFoundException("BuildProject", projectId);

        var materials = await _db.BldMaterialRequests.AsNoTracking()
            .Where(m => m.TenantId == Tid && m.ProjectId == projectId && m.NeededBy != null && m.NeededBy < today
                        && m.Status != BuildMaterialRequestStatus.Delivered && m.Status != BuildMaterialRequestStatus.Cancelled)
            .Select(m => new BuildRiskLateMaterial(m.Id, m.MaterialName, m.NeededBy!.Value, m.StageId)).ToListAsync(ct);
        var changeOrders = await _db.BldChangeOrders.AsNoTracking()
            .Where(o => o.TenantId == Tid && o.ProjectId == projectId
                        && (o.Status == BuildChangeOrderStatus.Draft || o.Status == BuildChangeOrderStatus.SentToClient))
            .Select(o => new { o.Id, o.Number, o.Title, o.Status, o.ScheduleImpactDays, o.CostImpact }).ToListAsync(ct);
        var ncs = await _db.BldNonConformities.AsNoTracking()
            .Where(n => n.TenantId == Tid && n.ProjectId == projectId
                        && (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment)
                        && (n.Severity == BuildNonConformitySeverity.High || n.Severity == BuildNonConformitySeverity.Critical))
            .Select(n => new { n.Id, n.Number, n.Title, n.Severity, n.StageId, n.DueDate }).ToListAsync(ct);

        return BuildRiskAnalyzer.Delay(new BuildDelayRiskInput(projectId, today, c.Project.ExpectedEndDate, c.Result, materials,
            changeOrders.Select(o => new BuildRiskChangeOrder(o.Id, o.Number, o.Title, o.Status.ToString(), o.ScheduleImpactDays, o.CostImpact)).ToList(),
            ncs.Select(n => new BuildRiskNonConformity(n.Id, n.Number, n.Title, n.Severity.ToString(), n.StageId, n.DueDate)).ToList()));
    }

    public async Task<BuildRiskAnalysisDto> CostAsync(Guid projectId, CancellationToken ct)
    {
        var cost = await _cost.GetCostByStageAsync(projectId, ct);
        var percents = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId)
            .ToDictionaryAsync(s => s.Id, s => (decimal)s.ProgressPercent, ct);
        var stages = cost.Stages.Select(s => new BuildCostRiskStage(s.StageId, s.StageName, s.Budgeted, s.Realized,
            s.StageId is { } id ? percents.GetValueOrDefault(id) : 0m)).ToList();

        var approved = await _db.BldChangeOrders.AsNoTracking()
            .Where(o => o.TenantId == Tid && o.ProjectId == projectId && o.Status == BuildChangeOrderStatus.Approved)
            .Select(o => o.CostImpact).ToListAsync(ct);
        var unlinked = await _db.BldMaterialRequests.AsNoTracking()
            .Where(m => m.TenantId == Tid && m.ProjectId == projectId && m.MovementId == null
                        && (m.Status == BuildMaterialRequestStatus.Ordered || m.Status == BuildMaterialRequestStatus.PartiallyDelivered
                            || m.Status == BuildMaterialRequestStatus.Delivered))
            .Select(m => new BuildUnlinkedMaterial(m.Id, m.MaterialName, Math.Round(m.Quantity * (m.EstimatedUnitCost ?? 0m), 2)))
            .ToListAsync(ct);

        return BuildRiskAnalyzer.Cost(new BuildCostRiskInput(projectId, BusinessClock.Today(), stages, approved.Sum(), approved.Count, unlinked));
    }
}
