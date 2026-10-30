using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Cost per stage without a parallel ledger: a construction cost is a <see cref="FinancialMovement"/>
/// (ContextType Obra, ContextId = project) optionally tagged with a stage. Budgeted per stage comes from the
/// items of the project's approved (or converted) budgets; realized from its confirmed outgoing movements.
/// </summary>
public sealed class BuildCostService
{
    public const string NoStageName = "Sem etapa";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;

    public BuildCostService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<BuildMovementStageDto> AssignStageAsync(Guid projectId, Guid movementId, Guid? stageId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
        var movement = await _db.IntMovements.FirstOrDefaultAsync(m => m.TenantId == _tenant.Id && m.Id == movementId, ct)
            ?? throw new NotFoundException("Lançamento não encontrado.");
        if (movement.ContextType != FinancialContextType.Obra || movement.ContextId != projectId)
            throw new DomainException("Este lançamento não é desta obra.", "movement_not_of_project");

        string? stageName = null;
        if (stageId is { } sid && sid != Guid.Empty)
        {
            stageName = await _db.BldStages
                .Where(s => s.TenantId == _tenant.Id && s.Id == sid && s.ProjectId == projectId)
                .Select(s => s.Name).FirstOrDefaultAsync(ct)
                ?? throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
        }

        var before = movement.StageId;
        movement.AssignStage(stageId);
        _audit.Record(AuditActions.BuildMovementStageAssigned, "Movement", movement.Id,
            stageName is null ? "Lançamento da obra desligado de etapa." : $"Lançamento da obra ligado à etapa {stageName}.",
            new { projectId, before, after = movement.StageId });
        await _db.SaveChangesAsync(ct);
        return new BuildMovementStageDto(movement.Id, projectId, movement.StageId, stageName);
    }

    /// <summary>
    /// Budgeted amount per stage id (null key = items without stage) from ONE reference budget of the project —
    /// never the sum of several: the converted one wins, then the latest approved. Also used by measurements
    /// to suggest the measured value (delta % × stage budget).
    /// </summary>
    public async Task<IReadOnlyList<(Guid? StageId, decimal Total)>> BudgetedByStageAsync(Guid projectId, CancellationToken ct)
    {
        var referenceBudgetId = await _db.BldBudgets.AsNoTracking()
            .Where(b => b.TenantId == _tenant.Id && b.ProjectId == projectId
                     && (b.Status == BuildBudgetStatus.Approved || b.Status == BuildBudgetStatus.Converted))
            .OrderByDescending(b => b.Status == BuildBudgetStatus.Converted)
            .ThenByDescending(b => b.UpdatedAt).ThenByDescending(b => b.CreatedAt)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (referenceBudgetId is null) return [];

        var rows = await _db.BldBudgetItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.BudgetId == referenceBudgetId)
            .GroupBy(i => i.StageId)
            .Select(g => new { StageId = g.Key, Total = g.Sum(x => x.TotalCost) })
            .ToListAsync(ct);
        return rows.Select(r => (r.StageId, r.Total)).ToList();
    }

    public async Task<BuildCostByStageDto> GetCostByStageAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);

        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Order })
            .ToListAsync(ct);
        var stageIds = stages.Select(s => s.Id).ToHashSet();

        var budgeted = await BudgetedByStageAsync(projectId, ct);

        var realized = await _db.IntMovements.AsNoTracking()
            .Where(m => m.TenantId == _tenant.Id
                     && m.ContextType == FinancialContextType.Obra
                     && m.ContextId == projectId
                     && m.Status == MovementStatus.Confirmed
                     && m.Direction == MovementDirection.Out)
            .GroupBy(m => m.StageId)
            .Select(g => new { StageId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);

        // a stage of another project (or one deleted since) counts as "no stage"
        Guid? Bucket(Guid? id) => id is { } v && stageIds.Contains(v) ? v : null;
        var budgetBy = budgeted.GroupBy(x => Bucket(x.StageId)).ToDictionary(g => g.Key ?? Guid.Empty, g => g.Sum(x => x.Total));
        var realBy = realized.GroupBy(x => Bucket(x.StageId))
            .ToDictionary(g => g.Key ?? Guid.Empty, g => (Total: g.Sum(x => x.Total), Count: g.Sum(x => x.Count)));

        var rows = stages.Select(s =>
        {
            var b = budgetBy.GetValueOrDefault(s.Id);
            var r = realBy.GetValueOrDefault(s.Id);
            return new BuildStageCostDto(s.Id, s.Name, s.Order, b, r.Total, b - r.Total, r.Count);
        }).ToList();

        var nb = budgetBy.GetValueOrDefault(Guid.Empty);
        var nr = realBy.GetValueOrDefault(Guid.Empty);
        if (nb != 0m || nr.Total != 0m || nr.Count > 0)
            rows.Add(new BuildStageCostDto(null, NoStageName, null, nb, nr.Total, nb - nr.Total, nr.Count));

        var tb = rows.Sum(x => x.Budgeted);
        var tr = rows.Sum(x => x.Realized);
        return new BuildCostByStageDto(projectId, tb, tr, tb - tr, rows);
    }
}
