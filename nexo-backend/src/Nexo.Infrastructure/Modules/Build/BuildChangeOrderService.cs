using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// What an approved change order does, the same whoever approves it (company or client through the portal).
/// Every query names the change order's tenant explicitly, so it runs in a public request too.
///   1. Contract: project.BudgetApproved += CostImpact when the project has an approved budget (else it only counts
///      in the contract total of the financial summary).
///   2. Schedule: the affected stage's planned end moves by ScheduleImpactDays (only that stage — later stages are
///      not shifted).
///   3. When a baseline exists, the new plan is frozen as a revision "Aditivo #N aprovado".
/// The caller saves; then <see cref="RefreshSnapshotAsync"/> updates today's progress snapshot.
/// </summary>
public sealed class BuildChangeOrderEffects
{
    private readonly NexoDbContext _db;
    private readonly BuildProgressEngine _engine;

    public BuildChangeOrderEffects(NexoDbContext db, BuildProgressEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <returns>Whether the schedule changed (stage end moved).</returns>
    public async Task<bool> ApplyApprovalAsync(BuildChangeOrder co, Guid actorId, string actorName, CancellationToken ct)
    {
        var tid = co.TenantId;
        var project = await _db.BldProjects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tid && p.Id == co.ProjectId, ct)
            ?? throw new NotFoundException("BuildProject", co.ProjectId);
        if (project.ApplyChangeOrderCost(co.CostImpact)) co.MarkAppliedToBudget();

        if (co.ScheduleImpactDays == 0 || co.StageId is not { } stageId) return false;
        var stage = await _db.BldStages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tid && s.Id == stageId && s.ProjectId == co.ProjectId, ct);
        if (stage is null || !stage.ShiftPlannedEnd(co.ScheduleImpactDays)) return false;

        var last = await _db.BldScheduleRevisions.IgnoreQueryFilters()
            .Where(r => r.TenantId == tid && r.ProjectId == co.ProjectId)
            .MaxAsync(r => (int?)r.Number, ct) ?? 0;
        if (last > 0)
        {
            // tracked query: the stage moved above comes back with its new (unsaved) planned end
            var stages = await _db.BldStages.IgnoreQueryFilters()
                .Where(s => s.TenantId == tid && s.ProjectId == co.ProjectId)
                .OrderBy(s => s.Order).ThenBy(s => s.Name)
                .ToListAsync(ct);
            var items = stages.Select(s => new BuildScheduleItemDto(s.Id, s.Name, s.PlannedStartDate, s.PlannedEndDate, s.Weight)).ToList();
            _db.BldScheduleRevisions.Add(BuildScheduleRevision.Freeze(tid, co.ProjectId, last + 1,
                $"Aditivo #{co.Number} aprovado", JsonSerializer.Serialize(items, BuildProgressEngine.Json), actorId, actorName));
        }
        return true;
    }

    public async Task RefreshSnapshotAsync(Guid tenantId, Guid projectId, CancellationToken ct)
    {
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(tenantId, projectId, today, bypassFilters: true, ct);
        if (c is not null) await _engine.UpsertSnapshotAsync(c, today, bypassFilters: true, ct);
    }
}

/// <summary>Change orders (aditivos) — company side (tenant-scoped, Management). Every change is audited.</summary>
public sealed class BuildChangeOrderService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly BuildChangeOrderEffects _effects;

    public BuildChangeOrderService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, BuildChangeOrderEffects effects)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _effects = effects;
    }

    public async Task<IReadOnlyList<BuildChangeOrderDto>> ListAsync(Guid projectId, string? status, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var q = _db.BldChangeOrders.AsNoTracking().Where(c => c.TenantId == _tenant.Id && c.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BuildChangeOrderStatus>(status, true, out var st) || !Enum.IsDefined(st))
                throw new DomainException($"Status inválido: '{status}'.", "invalid_status");
            q = q.Where(c => c.Status == st);
        }
        var list = await q.OrderBy(c => c.Number).ToListAsync(ct);
        return await MapManyAsync(list, ct);
    }

    public async Task<BuildChangeOrderDto> GetAsync(Guid id, CancellationToken ct)
        => (await MapManyAsync([await LoadAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildChangeOrderDto> CreateAsync(Guid projectId, SaveBuildChangeOrderRequest r, CancellationToken ct,
        Guid? sourceSelectionId = null)
    {
        var co = await CreateEntityAsync(projectId, r, sourceSelectionId, ct);
        return await GetAsync(co.Id, ct);
    }

    /// <summary>Creates and saves the change order (number = max + 1 of the project, retried once on a race).</summary>
    internal async Task<BuildChangeOrder> CreateEntityAsync(Guid projectId, SaveBuildChangeOrderRequest r, Guid? sourceSelectionId,
        CancellationToken ct, Action<BuildChangeOrder>? beforeSave = null)
    {
        await EnsureProjectAsync(projectId, ct);
        await EnsureStageAsync(projectId, r.StageId, ct);
        for (var attempt = 0; ; attempt++)
        {
            var number = (await _db.BldChangeOrders.AsNoTracking()
                .Where(c => c.TenantId == _tenant.Id && c.ProjectId == projectId)
                .MaxAsync(c => (int?)c.Number, ct) ?? 0) + 1;
            var items = r.Items ?? [];
            var cost = r.CostImpact ?? items.Sum(i => BuildMoney.Round(i.Quantity * i.UnitCost));
            var co = BuildChangeOrder.Create(_tenant.Id, projectId, number, r.Title, r.Description,
                ParseReason(r.Reason), cost, r.ScheduleImpactDays, r.StageId, _audit.UserId, _audit.UserName, sourceSelectionId);
            _db.BldChangeOrders.Add(co);
            var lines = Lines(co.Id, items);
            _db.BldChangeOrderItems.AddRange(lines);
            beforeSave?.Invoke(co);
            _audit.Record(AuditActions.BuildChangeOrderSaved, "ChangeOrder", co.Id, $"Aditivo #{co.Number} criado: {co.Title}.",
                new { projectId, co.CostImpact, co.ScheduleImpactDays, sourceSelectionId });
            try
            {
                await _db.SaveBuildAsync(ct);
                return co;
            }
            catch (DbUpdateException ex) when (attempt == 0
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    public async Task<BuildChangeOrderDto> UpdateAsync(Guid id, SaveBuildChangeOrderRequest r, CancellationToken ct)
    {
        var co = await LoadAsync(id, ct);
        await EnsureStageAsync(co.ProjectId, r.StageId, ct);
        var items = r.Items ?? [];
        var cost = r.CostImpact ?? items.Sum(i => BuildMoney.Round(i.Quantity * i.UnitCost));
        co.Update(r.Title, r.Description, ParseReason(r.Reason), cost, r.ScheduleImpactDays, r.StageId);
        var old = await _db.BldChangeOrderItems.Where(i => i.TenantId == _tenant.Id && i.ChangeOrderId == id).ToListAsync(ct);
        _db.BldChangeOrderItems.RemoveRange(old);
        _db.BldChangeOrderItems.AddRange(Lines(co.Id, items));
        _audit.Record(AuditActions.BuildChangeOrderSaved, "ChangeOrder", co.Id, $"Aditivo #{co.Number} alterado.",
            new { co.CostImpact, co.ScheduleImpactDays });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildChangeOrderDto> SendAsync(Guid id, CancellationToken ct)
    {
        var co = await LoadAsync(id, ct);
        co.Send(DateTime.UtcNow);
        _audit.Record(AuditActions.BuildChangeOrderSent, "ChangeOrder", co.Id, $"Aditivo #{co.Number} enviado ao cliente.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The company approves (channel Internal): contract, stage end and schedule revision updated.</summary>
    public async Task<BuildChangeOrderDto> ApproveAsync(Guid id, string? comment, CancellationToken ct)
    {
        var co = await LoadAsync(id, ct);
        co.Approve(BuildDecisionChannel.Internal, _audit.UserName, _audit.UserId, comment, null, DateTime.UtcNow);
        var scheduleChanged = await _effects.ApplyApprovalAsync(co, _audit.UserId, _audit.UserName, ct);
        _audit.Record(AuditActions.BuildChangeOrderApproved, "ChangeOrder", co.Id,
            $"Aditivo #{co.Number} aprovado pela empresa.", new { co.CostImpact, co.ScheduleImpactDays, co.AppliedToBudget });
        await SaveDecisionAsync(ct);
        if (scheduleChanged) await _effects.RefreshSnapshotAsync(co.TenantId, co.ProjectId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildChangeOrderDto> RejectAsync(Guid id, string? comment, CancellationToken ct)
    {
        var co = await LoadAsync(id, ct);
        co.Reject(BuildDecisionChannel.Internal, _audit.UserName, _audit.UserId, comment, null, DateTime.UtcNow);
        _audit.Record(AuditActions.BuildChangeOrderRejected, "ChangeOrder", co.Id, $"Aditivo #{co.Number} recusado pela empresa.");
        await SaveDecisionAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildChangeOrderDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var co = await LoadAsync(id, ct);
        co.Cancel();
        _audit.Record(AuditActions.BuildChangeOrderCancelled, "ChangeOrder", co.Id, $"Aditivo #{co.Number} cancelado.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task SaveDecisionAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveBuildAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("O cronograma foi alterado ao mesmo tempo por outra pessoa. Recarregue e tente de novo.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<BuildChangeOrderItem> Lines(Guid changeOrderId, IReadOnlyList<BuildChangeOrderItemRequest> items)
    {
        var order = 0;
        return items.Select(i => BuildChangeOrderItem.Create(_tenant.Id, changeOrderId, ++order, i.Name,
            BuildBudgetCalculator.ParseKind(i.Kind), i.Quantity, i.Unit, i.UnitCost)).ToList();
    }

    private static BuildChangeOrderReason ParseReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return BuildChangeOrderReason.Other;
        return Enum.TryParse<BuildChangeOrderReason>(reason, true, out var r) && Enum.IsDefined(r)
            ? r
            : throw new DomainException($"Motivo do aditivo inválido: '{reason}'.", "invalid_reason");
    }

    private async Task<BuildChangeOrder> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldChangeOrders.Where(c => c.TenantId == _tenant.Id && c.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Aditivo não encontrado.");
    }

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private async Task EnsureStageAsync(Guid projectId, Guid? stageId, CancellationToken ct)
    {
        if (stageId is { } sid
            && !await _db.BldStages.AnyAsync(s => s.TenantId == _tenant.Id && s.Id == sid && s.ProjectId == projectId, ct))
            throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
    }

    private async Task<IReadOnlyList<BuildChangeOrderDto>> MapManyAsync(IReadOnlyList<BuildChangeOrder> list, CancellationToken ct)
    {
        var ids = list.Select(c => c.Id).ToList();
        var stageIds = list.Where(c => c.StageId.HasValue).Select(c => c.StageId!.Value).Distinct().ToList();
        var items = (await _db.BldChangeOrderItems.AsNoTracking()
                .Where(i => i.TenantId == _tenant.Id && ids.Contains(i.ChangeOrderId)).ToListAsync(ct))
            .ToLookup(i => i.ChangeOrderId);
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return list.Select(c => new BuildChangeOrderDto(
            c.Id, c.PublicId, c.ProjectId, c.Number, c.Title, c.Description, c.Reason.ToString(), c.CostImpact,
            c.ScheduleImpactDays, c.StageId, c.StageId is { } sid ? stages.GetValueOrDefault(sid) : null, c.Status.ToString(),
            BuildBudgetCalculator.Utc(c.SentAt), BuildBudgetCalculator.Utc(c.DecidedAt), c.DecidedBy,
            c.DecisionChannel?.ToString(), c.ClientComment, c.SignerName, c.SourceSelectionId, c.AppliedToBudget,
            c.CreatedByName,
            items[c.Id].OrderBy(i => i.Order).Select(i => new BuildChangeOrderItemDto(i.Id, i.Order, i.Name, i.Kind.ToString(),
                i.Quantity, i.Unit, i.UnitCost, i.TotalCost)).ToList(),
            c.CreatedAt, c.UpdatedAt)).ToList();
    }
}
