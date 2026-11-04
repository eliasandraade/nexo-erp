using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Common;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Loads what <see cref="BuildTwinStateEngine"/> needs (elements, zones, stages with their progress history, bindings, explicit
/// events) for one project/version and runs it. Every query names the tenant; <c>bypassFilters</c> only for callers without a
/// current tenant (the public portal, whose tenant comes from the slug).
/// </summary>
public sealed class BuildTwinStateReader
{
    private readonly NexoDbContext _db;

    public BuildTwinStateReader(NexoDbContext db) => _db = db;

    private IQueryable<T> Q<T>(Guid tenantId, bool bypass) where T : TenantEntity
        => (bypass ? _db.Set<T>().IgnoreQueryFilters() : _db.Set<T>()).Where(x => x.TenantId == tenantId);

    /// <summary>
    /// The version shown by default: the current version of a model, preferring 3D (NativeBim/Converted3D) and with a viewer,
    /// the architecture discipline first, then the oldest model. Null when the project has no ready version.
    /// </summary>
    public async Task<BuildModelVersion?> PrimaryVersionAsync(Guid tenantId, Guid projectId, bool bypass, CancellationToken ct)
    {
        var current = await (
                from m in Q<BuildModel>(tenantId, bypass)
                join v in Q<BuildModelVersion>(tenantId, bypass) on m.CurrentVersionId equals v.Id
                where m.ProjectId == projectId && v.Status == BuildModelVersionStatus.Ready
                select new { Model = m, Version = v })
            .AsNoTracking().ToListAsync(ct);
        return current
            .OrderByDescending(x => x.Version.GeometrySource is BuildGeometrySource.NativeBim or BuildGeometrySource.Converted3D)
            .ThenByDescending(x => x.Version.ViewerFileKey is not null)
            .ThenBy(x => x.Model.Discipline == BuildModelDiscipline.Arquitetura ? 0 : 1)
            .ThenBy(x => x.Model.CreatedAt)
            .Select(x => x.Version)
            .FirstOrDefault();
    }

    public async Task<BuildTwinStateEngine.Result> ComputeAsync(Guid tenantId, Guid projectId, Guid? versionId, DateOnly date,
        bool bypass, CancellationToken ct)
    {
        var elements = versionId is { } vid
            ? await Q<BuildModelElement>(tenantId, bypass).AsNoTracking()
                .Where(e => e.ModelVersionId == vid)
                .Select(e => new BuildTwinStateEngine.ElementInput(e.ExternalId, e.Category, e.ZoneId, e.ModelVersionId))
                .ToListAsync(ct)
            : [];

        var zones = await Q<BuildModelZone>(tenantId, bypass).AsNoTracking()
            .Where(z => z.ProjectId == projectId && (z.ModelVersionId == null || z.ModelVersionId == versionId))
            .Select(z => new { z.Id, z.ParentZoneId })
            .ToListAsync(ct);
        var zoneParents = zones.ToDictionary(z => z.Id, z => z.ParentZoneId);

        var stages = await Q<BuildStage>(tenantId, bypass).AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .Select(s => new { s.Id, s.PlannedEndDate, s.Status, s.ProgressPercent })
            .ToListAsync(ct);
        var history = (await Q<BuildStageProgressEvent>(tenantId, bypass).AsNoTracking()
                .Where(e => e.ProjectId == projectId && e.EffectiveDate <= date)
                .Select(e => new { e.StageId, e.EffectiveDate, e.CreatedAt, e.Percent })
                .ToListAsync(ct))
            .GroupBy(e => e.StageId).ToDictionary(g => g.Key, g => g.Select(e => (e.EffectiveDate, e.CreatedAt, e.Percent)).ToList());
        var anyHistory = (await Q<BuildStageProgressEvent>(tenantId, bypass).AsNoTracking()
                .Where(e => e.ProjectId == projectId).Select(e => e.StageId).Distinct().ToListAsync(ct))
            .ToHashSet();

        var today = BusinessClock.Today();
        var stageInputs = stages.Select(s => new BuildTwinStateEngine.StageInput(
            s.Id, s.PlannedEndDate,
            // only the current status is known: "blocked" applies from today on, never to the past
            Blocked: s.Status == BuildStageStatus.Blocked && date >= today,
            // a stage with history but no event up to the date was at 0% on that date
            history.TryGetValue(s.Id, out var h) ? h : anyHistory.Contains(s.Id) ? [(DateOnly.MinValue, DateTime.MinValue, 0)] : [],
            s.ProgressPercent)).ToList();

        var bindings = await Q<BuildModelStageBinding>(tenantId, bypass).AsNoTracking()
            .Where(b => b.ProjectId == projectId
                     && (b.Scope != BuildBindingScope.Category || b.ModelVersionId == null || b.ModelVersionId == versionId))
            .Select(b => new BuildTwinStateEngine.BindingInput(b.StageId, b.Scope, b.ElementExternalId, b.ZoneId, b.Category, b.ModelVersionId))
            .ToListAsync(ct);

        var events = await Q<BuildElementProgressEvent>(tenantId, bypass).AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.EffectiveDate <= date)
            .Select(e => new BuildTwinStateEngine.EventInput(e.ElementExternalId, e.State, e.EffectiveDate, e.CreatedAt))
            .ToListAsync(ct);

        return BuildTwinStateEngine.Compute(date, today, elements, zoneParents, stageInputs, bindings, events);
    }
}
