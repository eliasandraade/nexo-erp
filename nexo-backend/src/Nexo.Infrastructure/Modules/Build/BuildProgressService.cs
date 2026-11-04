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
/// Progress of a project for the company (tenant-scoped): weighted overall %, curves, "how was it on day X",
/// schedule baseline/revisions, and the progress history + today's snapshot behind every stage change.
/// </summary>
public sealed class BuildProgressService : IBuildProgressRecorder
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildProgressEngine _engine;
    private readonly BuildAuditTrail _audit;

    public BuildProgressService(NexoDbContext db, ICurrentTenant tenant, BuildProgressEngine engine, BuildAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _engine = engine;
        _audit = audit;
    }

    // ── IBuildProgressRecorder ────────────────────────────────────────────────

    public void Stage(BuildStageProgressEvent progressEvent) => _db.BldStageProgressEvents.Add(progressEvent);

    public Task<bool> HasOpenCriticalNonConformityAsync(Guid tenantId, Guid stageId, CancellationToken ct = default)
        => _db.BldNonConformities.IgnoreQueryFilters().AnyAsync(n => n.TenantId == tenantId && n.StageId == stageId
            && n.Severity == BuildNonConformitySeverity.Critical
            && (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment), ct);

    public async Task RefreshTodaySnapshotAsync(Guid projectId, CancellationToken ct = default)
    {
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(_tenant.Id, projectId, today, bypassFilters: false, ct);
        if (c is null) return;
        await _engine.UpsertSnapshotAsync(c, today, bypassFilters: false, ct);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<BuildProgressDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(_tenant.Id, projectId, today, bypassFilters: false, ct)
            ?? throw new NotFoundException("BuildProject", projectId);
        var curves = await _engine.CurvesAsync(c, today, bypassFilters: false, ct);
        var r = c.Result;
        return new BuildProgressDto(
            projectId, today, r.PlannedPercent, r.ActualPercent, r.ForecastPercent, r.PlannedDelivery, r.ForecastDelivery,
            r.Methodology, BuildProgressEngine.PlanSource(c.Plan), c.Plan?.Number,
            r.Stages.Select(s => new BuildProgressStageDto(
                s.StageId, s.Name, s.Order, s.Weight, s.Status, s.PlannedPercent, s.ActualPercent, s.PlannedStart,
                s.PlannedEnd, s.ActualStart, s.ActualEnd, s.ForecastEnd, s.DelayDays, s.Flags)).ToList(),
            curves, r.Warnings);
    }

    /// <summary>"Como estava a obra em X": the latest snapshot on or before the date.</summary>
    public async Task<BuildProgressSnapshotDto> GetAtAsync(Guid projectId, DateOnly date, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var s = await _db.BldProgressSnapshots.AsNoTracking()
            .Where(x => x.TenantId == _tenant.Id && x.ProjectId == projectId && x.Date <= date)
            .OrderByDescending(x => x.Date)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Não há registro do avanço desta obra até {date:dd/MM/yyyy}.");
        using var doc = JsonDocument.Parse(s.StagesJson);
        return new BuildProgressSnapshotDto(projectId, date, s.Date, s.PlannedPercent, s.ActualPercent,
            s.ForecastPercent, s.ForecastDeliveryDate, s.Methodology, doc.RootElement.Clone(), s.UpdatedAt);
    }

    public async Task<IReadOnlyList<BuildScheduleRevisionDto>> GetRevisionsAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var list = await _db.BldScheduleRevisions.AsNoTracking()
            .Where(r => r.TenantId == _tenant.Id && r.ProjectId == projectId)
            .OrderBy(r => r.Number)
            .ToListAsync(ct);
        return list.Select(Map).ToList();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Freezes the current plan (per stage: planned dates and weight). The first one is the baseline; each
    /// later one is a revision and needs a reason. The planned curve follows the latest frozen plan.
    /// </summary>
    public async Task<BuildScheduleRevisionDto> FreezeScheduleAsync(Guid projectId, string? reason, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .ToListAsync(ct);
        if (stages.Count == 0)
            throw new DomainException("Cadastre as etapas da obra antes de definir a linha de base do cronograma.",
                "schedule_without_stages");

        var last = await _db.BldScheduleRevisions.AsNoTracking()
            .Where(r => r.TenantId == _tenant.Id && r.ProjectId == projectId)
            .MaxAsync(r => (int?)r.Number, ct) ?? 0;
        var items = stages.Select(s => new BuildScheduleItemDto(s.Id, s.Name, s.PlannedStartDate, s.PlannedEndDate, s.Weight)).ToList();
        var revision = BuildScheduleRevision.Freeze(_tenant.Id, projectId, last + 1, reason,
            JsonSerializer.Serialize(items, BuildProgressEngine.Json), _audit.UserId, _audit.UserName);
        _db.BldScheduleRevisions.Add(revision);
        _audit.Record(AuditActions.BuildScheduleFrozen, "ScheduleRevision", revision.Id,
            revision.Kind == BuildScheduleRevisionKind.Baseline
                ? "Linha de base do cronograma definida."
                : $"Cronograma revisado (revisão {revision.Number}): {revision.Reason}",
            new { projectId, revision.Number, kind = revision.Kind.ToString(), stages = items.Count });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("O cronograma foi congelado ao mesmo tempo por outra pessoa. Recarregue e tente de novo.");
        }

        await RefreshTodaySnapshotAsync(projectId, ct);
        return Map(revision);
    }

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private static BuildScheduleRevisionDto Map(BuildScheduleRevision r) => new(
        r.Id, r.Number, r.Kind.ToString(), r.Reason, BuildProgressEngine.ParseItems(r.ItemsJson), r.CreatedByName, r.CreatedAt);
}
