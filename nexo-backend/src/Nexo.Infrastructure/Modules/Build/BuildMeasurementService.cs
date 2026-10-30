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
/// Measurements (medições) — tenant-scoped, Management. Approval is the only way a measurement moves stage
/// progress, and it goes through <see cref="BuildStageProgressApplier"/> (the same path as the manual update):
/// one history event per stage with source Measurement and the measurement id, then today's snapshot.
/// </summary>
public sealed class BuildMeasurementService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IBuildProgressRecorder _progress;
    private readonly BuildCostService _cost;

    public BuildMeasurementService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit,
        IBuildProgressRecorder progress, BuildCostService cost)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _progress = progress;
        _cost = cost;
    }

    public async Task<IReadOnlyList<BuildMeasurementDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var list = await _db.BldMeasurements.AsNoTracking()
            .Where(m => m.TenantId == _tenant.Id && m.ProjectId == projectId).OrderBy(m => m.Number).ToListAsync(ct);
        return await MapManyAsync(projectId, list, ct);
    }

    public async Task<BuildMeasurementDto> GetAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct, tracking: false);
        return (await MapManyAsync(m.ProjectId, [m], ct))[0];
    }

    public async Task<BuildMeasurementDto> CreateAsync(Guid projectId, SaveBuildMeasurementRequest r, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        for (var attempt = 0; ; attempt++)
        {
            var number = (await _db.BldMeasurements.AsNoTracking()
                .Where(m => m.TenantId == _tenant.Id && m.ProjectId == projectId)
                .MaxAsync(m => (int?)m.Number, ct) ?? 0) + 1;
            var m = BuildMeasurement.Create(_tenant.Id, projectId, number, r.PeriodStart, r.PeriodEnd, r.Notes, _audit.UserId,
                _audit.UserName);
            _db.BldMeasurements.Add(m);
            _db.BldMeasurementItems.AddRange(await ItemsAsync(m, r.Items ?? [], ct));
            _audit.Record(AuditActions.BuildMeasurementSaved, "Measurement", m.Id, $"Medição #{m.Number} criada.",
                new { projectId, m.PeriodStart, m.PeriodEnd, items = r.Items?.Count ?? 0 });
            try
            {
                await _db.SaveBuildAsync(ct);
                return await GetAsync(m.Id, ct);
            }
            catch (DbUpdateException ex) when (attempt == 0
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>Draft only; items replace the previous ones (previous % re-read from the stages).</summary>
    public async Task<BuildMeasurementDto> UpdateAsync(Guid id, SaveBuildMeasurementRequest r, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        m.Update(r.PeriodStart, r.PeriodEnd, r.Notes);
        var old = await _db.BldMeasurementItems.Where(i => i.TenantId == _tenant.Id && i.MeasurementId == id).ToListAsync(ct);
        _db.BldMeasurementItems.RemoveRange(old);
        _db.BldMeasurementItems.AddRange(await ItemsAsync(m, r.Items ?? [], ct));
        _audit.Record(AuditActions.BuildMeasurementSaved, "Measurement", m.Id, $"Medição #{m.Number} alterada.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildMeasurementDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        if (!await _db.BldMeasurementItems.AnyAsync(i => i.TenantId == _tenant.Id && i.MeasurementId == id, ct))
            throw new DomainException("Inclua ao menos uma etapa na medição.", "measurement_empty");
        m.Submit(DateTime.UtcNow);
        _audit.Record(AuditActions.BuildMeasurementSubmitted, "Measurement", m.Id, $"Medição #{m.Number} enviada para aprovação.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Approves and applies: each stage gets the measured cumulative % through the shared progress path (event
    /// source Measurement, reference = this measurement), effective on the period end (never after today).
    /// Approving twice → 409. A stage that moved past the measured % since the draft → 422.
    /// </summary>
    public async Task<BuildMeasurementDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        var items = await _db.BldMeasurementItems.Where(i => i.TenantId == _tenant.Id && i.MeasurementId == id).ToListAsync(ct);
        if (items.Count == 0 && m.Status != BuildMeasurementStatus.Approved)
            throw new DomainException("Inclua ao menos uma etapa na medição.", "measurement_empty");
        m.Approve(_audit.UserId, _audit.UserName, DateTime.UtcNow);

        var stageIds = items.Select(i => i.StageId).ToList();
        var stages = await _db.BldStages
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == m.ProjectId && stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var today = BusinessClock.Today();
        var effective = m.PeriodEnd < today ? m.PeriodEnd : today;
        foreach (var item in items)
        {
            if (!stages.TryGetValue(item.StageId, out var stage))
                throw new DomainException("Uma etapa desta medição não existe mais; ajuste a medição.", "measurement_stage_missing");
            item.RefreshPrevious(stage.ProgressPercent);
            BuildStageProgressApplier.Apply(_progress, stage, item.MeasuredPercent, effective, BuildProgressSource.Measurement,
                _audit.UserId, _audit.UserName, $"Medição #{m.Number}", m.Id);
        }

        _audit.Record(AuditActions.BuildMeasurementApproved, "Measurement", m.Id, $"Medição #{m.Number} aprovada.",
            new { items = items.Select(i => new { i.StageId, i.PreviousPercent, i.MeasuredPercent }) });
        await _db.SaveBuildAsync(ct);
        await _progress.RefreshTodaySnapshotAsync(m.ProjectId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<BuildMeasurementDto> RejectAsync(Guid id, string? reason, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        m.Reject(_audit.UserName, reason, DateTime.UtcNow);
        _audit.Record(AuditActions.BuildMeasurementRejected, "Measurement", m.Id, $"Medição #{m.Number} recusada.", new { reason });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<BuildMeasurementItem>> ItemsAsync(BuildMeasurement m, IReadOnlyList<BuildMeasurementItemRequest> items,
        CancellationToken ct)
    {
        var ids = items.Select(i => i.StageId).Distinct().ToList();
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == m.ProjectId && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.ProgressPercent, ct);
        return items.Select(i =>
        {
            if (!stages.TryGetValue(i.StageId, out var current))
                throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
            return BuildMeasurementItem.Create(_tenant.Id, m.Id, i.StageId, current, i.MeasuredPercent, i.Amount);
        }).ToList();
    }

    private async Task<BuildMeasurement> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldMeasurements.Where(m => m.TenantId == _tenant.Id && m.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Medição não encontrada.");
    }

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private async Task<IReadOnlyList<BuildMeasurementDto>> MapManyAsync(Guid projectId, IReadOnlyList<BuildMeasurement> list,
        CancellationToken ct)
    {
        var ids = list.Select(m => m.Id).ToList();
        var items = (await _db.BldMeasurementItems.AsNoTracking()
                .Where(i => i.TenantId == _tenant.Id && ids.Contains(i.MeasurementId)).ToListAsync(ct))
            .ToLookup(i => i.MeasurementId);
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == projectId)
            .ToDictionaryAsync(s => s.Id, s => new { s.Name, s.Order }, ct);
        var budgetByStage = (await _cost.BudgetedByStageAsync(projectId, ct))
            .Where(x => x.StageId.HasValue).ToDictionary(x => x.StageId!.Value, x => x.Total);

        return list.Select(m =>
        {
            var lines = items[m.Id]
                .OrderBy(i => stages.TryGetValue(i.StageId, out var st) ? st.Order : int.MaxValue)
                .Select(i =>
                {
                    decimal? suggested = budgetByStage.TryGetValue(i.StageId, out var stageBudget) && stageBudget > 0m
                        ? BuildMoney.Round(stageBudget * i.DeltaPercent / 100m)
                        : null;
                    return new BuildMeasurementItemDto(i.Id, i.StageId, stages.GetValueOrDefault(i.StageId)?.Name,
                        i.PreviousPercent, i.MeasuredPercent, i.DeltaPercent, i.Amount, suggested);
                }).ToList();
            return new BuildMeasurementDto(m.Id, m.ProjectId, m.Number, m.PeriodStart, m.PeriodEnd, m.Status.ToString(), m.Notes,
                BuildBudgetCalculator.Utc(m.SubmittedAt), m.ApprovedByName, BuildBudgetCalculator.Utc(m.ApprovedAt),
                BuildBudgetCalculator.Utc(m.RejectedAt), m.RejectedByName, m.RejectionReason,
                lines.Sum(l => l.Amount ?? 0m), m.CreatedByName, lines, m.CreatedAt, m.UpdatedAt);
        }).ToList();
    }
}
