using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Digital twin — company side (tenant-scoped, Management): zones (from the model and manual), elements, 4D state on a date,
/// stage bindings, explicit element events, the project overview and the 5D preparation (cost spread by quantity).
/// Reads stay available when Build3D is off; only uploads/processing are gated (see <see cref="BuildModelService"/>).
/// </summary>
public sealed class BuildTwinService
{
    public const int MaxPageSize = 500;
    public const int MaxBulkIds = 5000;

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly BuildTwinStateReader _reader;
    private readonly BuildTenantSettingsService _settings;
    private readonly BuildCostService _costs;

    public BuildTwinService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, BuildTwinStateReader reader,
        BuildTenantSettingsService settings, BuildCostService costs)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _reader = reader;
        _settings = settings;
        _costs = costs;
    }

    private Guid Tid => _tenant.Id;

    private async Task<BuildModelVersion> VersionAsync(Guid versionId, CancellationToken ct)
        => await _db.BldModelVersions.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == Tid && v.Id == versionId, ct)
           ?? throw new NotFoundException("BuildModelVersion", versionId);

    private static DateOnly DateOrToday(DateOnly? date) => date ?? BusinessClock.Today();

    // ── Zones ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildModelZoneDto>> VersionZonesAsync(Guid versionId, CancellationToken ct)
    {
        var v = await VersionAsync(versionId, ct);
        var zones = await _db.BldModelZones.AsNoTracking().Where(z => z.TenantId == Tid && z.ModelVersionId == v.Id)
            .OrderBy(z => z.Order).ToListAsync(ct);
        return await MapZonesAsync(zones, v.Id, ct);
    }

    /// <summary>Manual zones of the project (assisted mapping / simplified representation).</summary>
    public async Task<IReadOnlyList<BuildModelZoneDto>> ProjectZonesAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var zones = await _db.BldModelZones.AsNoTracking()
            .Where(z => z.TenantId == Tid && z.ProjectId == projectId && z.ModelVersionId == null)
            .OrderBy(z => z.Order).ThenBy(z => z.Name).ToListAsync(ct);
        return await MapZonesAsync(zones, null, ct);
    }

    public async Task<BuildModelZoneDto> CreateZoneAsync(Guid projectId, SaveBuildZoneRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await ValidateZoneRefsAsync(projectId, null, r, ct);
        var z = BuildModelZone.Manual(Tid, projectId, BuildTwinParse.Enum<BuildZoneKind>(r.Kind, "Tipo de zona"), r.Name, r.Order,
            r.ParentZoneId, r.Outline, r.SheetVersionId, r.SimplifiedBox, _audit.UserId);
        _db.BldModelZones.Add(z);
        _audit.Record(AuditActions.BuildModelZoneSaved, "ModelZone", z.Id, $"Zona criada: {z.Name}.", new { projectId, kind = z.Kind.ToString() });
        await _db.SaveBuildAsync(ct);
        return BuildTwinMap.Zone(z, 0);
    }

    public async Task<BuildModelZoneDto> UpdateZoneAsync(Guid zoneId, SaveBuildZoneRequest r, CancellationToken ct)
    {
        var z = await _db.BldModelZones.FirstOrDefaultAsync(x => x.TenantId == Tid && x.Id == zoneId, ct)
            ?? throw new NotFoundException("BuildModelZone", zoneId);
        await ValidateZoneRefsAsync(z.ProjectId, z.Id, r, ct);
        z.Update(BuildTwinParse.Enum<BuildZoneKind>(r.Kind, "Tipo de zona"), r.Name, r.Order, r.ParentZoneId, r.Outline, r.SheetVersionId,
            r.SimplifiedBox);
        _audit.Record(AuditActions.BuildModelZoneSaved, "ModelZone", z.Id, $"Zona alterada: {z.Name}.");
        await _db.SaveBuildAsync(ct);
        return BuildTwinMap.Zone(z, 0);
    }

    public async Task DeleteZoneAsync(Guid zoneId, CancellationToken ct)
    {
        var z = await _db.BldModelZones.FirstOrDefaultAsync(x => x.TenantId == Tid && x.Id == zoneId, ct)
            ?? throw new NotFoundException("BuildModelZone", zoneId);
        if (!z.IsManual) throw new DomainException("Zonas lidas do modelo não podem ser excluídas.", "zone_from_model");
        _db.BldModelZones.Remove(z);
        _audit.Record(AuditActions.BuildModelZoneDeleted, "ModelZone", z.Id, $"Zona excluída: {z.Name}.");
        await _db.SaveBuildAsync(ct);
    }

    private async Task ValidateZoneRefsAsync(Guid projectId, Guid? self, SaveBuildZoneRequest r, CancellationToken ct)
    {
        if (r.ParentZoneId is { } parent)
        {
            if (parent == self) throw new DomainException("Uma zona não pode ser pai dela mesma.", "invalid_parent_zone");
            if (!await _db.BldModelZones.AnyAsync(z => z.TenantId == Tid && z.Id == parent && z.ProjectId == projectId, ct))
                throw new DomainException("A zona pai não é desta obra.", "invalid_parent_zone");
            if (self is { } id && await IsDescendantAsync(parent, id, ct))
                throw new DomainException("A zona pai não pode ser uma subzona desta.", "invalid_parent_zone");
        }
        if (r.SheetVersionId is { } sheet
            && !await _db.BldModelVersions.AnyAsync(v => v.TenantId == Tid && v.Id == sheet && v.ProjectId == projectId
                && (v.SourceFormat == BuildModelSourceFormat.Pdf || v.SourceFormat == BuildModelSourceFormat.Image
                    || v.SourceFormat == BuildModelSourceFormat.Dxf), ct))
            throw new DomainException("A folha informada não é um PDF/imagem/DXF desta obra.", "invalid_sheet");
    }

    private async Task<bool> IsDescendantAsync(Guid candidate, Guid ancestor, CancellationToken ct)
    {
        var current = (Guid?)candidate;
        for (var guard = 0; current is { } c && guard < 64; guard++)
        {
            if (c == ancestor) return true;
            current = await _db.BldModelZones.Where(z => z.TenantId == Tid && z.Id == c).Select(z => z.ParentZoneId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    private async Task<IReadOnlyList<BuildModelZoneDto>> MapZonesAsync(IReadOnlyList<BuildModelZone> zones, Guid? versionId, CancellationToken ct)
    {
        var counts = versionId is { } vid
            ? await _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == vid && e.ZoneId != null)
                .GroupBy(e => e.ZoneId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            : [];
        return zones.Select(z => BuildTwinMap.Zone(z, counts.GetValueOrDefault(z.Id))).ToList();
    }

    // ── Elements and state ────────────────────────────────────────────────────

    public async Task<BuildModelElementPageDto> ElementsAsync(Guid versionId, string? category, Guid? zoneId, string? state, DateOnly? date,
        int? skip, int? take, CancellationToken ct)
    {
        var v = await VersionAsync(versionId, ct);
        var s = Math.Max(0, skip ?? 0);
        var n = Math.Clamp(take ?? 100, 1, MaxPageSize);
        var unboundOnly = string.Equals(state?.Trim(), "Unbound", StringComparison.OrdinalIgnoreCase);
        BuildElementState? stateFilter = string.IsNullOrWhiteSpace(state) || unboundOnly ? null : BuildTwinParse.Enum<BuildElementState>(state, "Estado");

        var zones = await _db.BldModelZones.AsNoTracking().Where(z => z.TenantId == Tid && z.ModelVersionId == v.Id)
            .Select(z => new { z.Id, z.Name, z.ParentZoneId }).ToListAsync(ct);
        HashSet<Guid>? zoneSet = null;
        if (zoneId is { } zid)
        {
            if (zones.All(z => z.Id != zid)) throw new DomainException("A zona não é desta versão do modelo.", "invalid_zone");
            zoneSet = Descendants(zid, zones.Select(z => (z.Id, z.ParentZoneId)));
        }

        var q = _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == v.Id);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category.Trim());
        if (zoneSet is not null) q = q.Where(e => e.ZoneId != null && zoneSet.Contains(e.ZoneId.Value));

        var result = await _reader.ComputeAsync(Tid, v.ProjectId, v.Id, DateOrToday(date), bypass: false, ct);
        var states = result.Elements.ToDictionary(e => e.ExternalId, StringComparer.Ordinal);

        List<BuildModelElement> page;
        int total;
        if (stateFilter is not null || unboundOnly)
        {
            var wanted = result.Elements.Where(e => unboundOnly ? e.State is null : e.State == stateFilter)
                .Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal);
            var all = await q.Where(e => wanted.Contains(e.ExternalId)).OrderBy(e => e.Category).ThenBy(e => e.Name).ThenBy(e => e.ExternalId).ToListAsync(ct);
            total = all.Count;
            page = all.Skip(s).Take(n).ToList();
        }
        else
        {
            total = await q.CountAsync(ct);
            page = await q.OrderBy(e => e.Category).ThenBy(e => e.Name).ThenBy(e => e.ExternalId).Skip(s).Take(n).ToListAsync(ct);
        }

        var zoneNames = zones.ToDictionary(z => z.Id, z => z.Name);
        var categories = await _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == v.Id)
            .Select(e => e.Category).Distinct().OrderBy(c => c).ToListAsync(ct);
        return new BuildModelElementPageDto(page.Select(e =>
        {
            var st = states.GetValueOrDefault(e.ExternalId);
            return new BuildModelElementDto(e.ExternalId, e.Category, e.Name, e.TypeName, e.Material, e.ZoneId,
                e.ZoneId is { } z ? zoneNames.GetValueOrDefault(z) : null,
                e.PropertiesJson is null ? null : JsonDocument.Parse(e.PropertiesJson).RootElement.Clone(),
                st?.State?.ToString(), st?.StageId, st?.Explicit ?? false);
        }).ToList(), total, s, n, categories);
    }

    public async Task<BuildTwinStateDto> StateAsync(Guid versionId, DateOnly? date, CancellationToken ct)
    {
        var v = await VersionAsync(versionId, ct);
        return BuildTwinMap.State(await _reader.ComputeAsync(Tid, v.ProjectId, v.Id, DateOrToday(date), bypass: false, ct));
    }

    private static HashSet<Guid> Descendants(Guid root, IEnumerable<(Guid Id, Guid? Parent)> zones)
    {
        var children = zones.Where(z => z.Parent is not null).GroupBy(z => z.Parent!.Value).ToDictionary(g => g.Key, g => g.Select(z => z.Id).ToList());
        var set = new HashSet<Guid> { root };
        var queue = new Queue<Guid>([root]);
        while (queue.Count > 0)
            foreach (var c in children.GetValueOrDefault(queue.Dequeue()) ?? [])
                if (set.Add(c)) queue.Enqueue(c);
        return set;
    }

    // ── Bindings ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildModelStageBindingDto>> BindingsAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var rows = await (from b in _db.BldModelBindings.AsNoTracking()
                          join s in _db.BldStages.AsNoTracking() on b.StageId equals s.Id
                          where b.TenantId == Tid && s.TenantId == Tid && b.ProjectId == projectId
                          orderby s.Order, b.Scope, b.CreatedAt
                          select new { b, s.Name }).Take(20_000).ToListAsync(ct);
        return rows.Select(x => Map(x.b, x.Name)).ToList();
    }

    public async Task<BuildModelStageBindingDto> CreateBindingAsync(Guid projectId, CreateBuildBindingRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var stageName = await StageNameAsync(projectId, r.StageId, ct);
        var scope = BuildTwinParse.Enum<BuildBindingScope>(r.Scope, "Escopo do vínculo");
        Guid? versionId = r.ModelVersionId;
        if (versionId is { } vid) await EnsureVersionOfProjectAsync(projectId, vid, ct);
        switch (scope)
        {
            case BuildBindingScope.Element:
                if (versionId is null || string.IsNullOrWhiteSpace(r.ElementExternalId))
                    throw new DomainException("Informe a versão do modelo e o id do elemento.", "invalid_binding");
                if (!await _db.BldModelElements.AnyAsync(e => e.TenantId == Tid && e.ModelVersionId == versionId && e.ExternalId == r.ElementExternalId.Trim(), ct))
                    throw new DomainException("O elemento não existe nesta versão do modelo.", "element_not_found");
                break;
            case BuildBindingScope.Zone:
                if (r.ZoneId is not { } zid || !await _db.BldModelZones.AnyAsync(z => z.TenantId == Tid && z.Id == zid && z.ProjectId == projectId, ct))
                    throw new DomainException("A zona não é desta obra.", "invalid_zone");
                versionId = await _db.BldModelZones.Where(z => z.TenantId == Tid && z.Id == zid).Select(z => z.ModelVersionId).FirstAsync(ct);
                break;
            default:
                if (string.IsNullOrWhiteSpace(r.Category)) throw new DomainException("Informe a categoria (ex.: IfcWall).", "invalid_binding");
                break;
        }
        var b = BuildModelStageBinding.Create(Tid, projectId, r.StageId, scope, versionId, r.ElementExternalId, r.ZoneId, r.Category,
            BuildBindingSource.Manual, _audit.UserId);
        _db.BldModelBindings.Add(b);
        _audit.Record(AuditActions.BuildModelBindingSaved, "ModelBinding", b.Id, $"Vínculo ao modelo ({scope}) na etapa {stageName}.",
            new { projectId, stageId = r.StageId, scope = scope.ToString(), r.ElementExternalId, r.ZoneId, r.Category, versionId });
        await _db.SaveBuildAsync(ct);
        return Map(b, stageName);
    }

    /// <summary>Every element of the version matching the filter becomes an Element binding of the stage.</summary>
    public async Task<BulkBuildBindingResultDto> BulkBindAsync(Guid projectId, BulkBuildBindingRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var stageName = await StageNameAsync(projectId, r.StageId, ct);
        await EnsureVersionOfProjectAsync(projectId, r.ModelVersionId, ct);
        if (string.IsNullOrWhiteSpace(r.Category) && r.ZoneId is null && (r.ExternalIds is null || r.ExternalIds.Count == 0))
            throw new DomainException("Informe ao menos um filtro: categoria, zona ou ids dos elementos.", "invalid_binding");
        if (r.ExternalIds is { Count: > MaxBulkIds })
            throw new DomainException($"No máximo {MaxBulkIds} ids por vez.", "invalid_binding");

        var q = _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == r.ModelVersionId);
        if (!string.IsNullOrWhiteSpace(r.Category)) q = q.Where(e => e.Category == r.Category.Trim());
        if (r.ZoneId is { } zid)
        {
            var zones = await _db.BldModelZones.AsNoTracking().Where(z => z.TenantId == Tid && z.ModelVersionId == r.ModelVersionId)
                .Select(z => new { z.Id, z.ParentZoneId }).ToListAsync(ct);
            if (zones.All(z => z.Id != zid)) throw new DomainException("A zona não é desta versão do modelo.", "invalid_zone");
            var set = Descendants(zid, zones.Select(z => (z.Id, z.ParentZoneId)));
            q = q.Where(e => e.ZoneId != null && set.Contains(e.ZoneId.Value));
        }
        if (r.ExternalIds is { Count: > 0 })
        {
            var ids = r.ExternalIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
            var found = await q.Where(e => ids.Contains(e.ExternalId)).Select(e => e.ExternalId).ToListAsync(ct);
            if (found.Count != ids.Count)
                throw new DomainException($"{ids.Count - found.Count} id(s) não existem nesta versão do modelo.", "element_not_found");
            q = q.Where(e => ids.Contains(e.ExternalId));
        }
        var matched = await q.Select(e => e.ExternalId).ToListAsync(ct);

        var replaced = 0;
        if (r.ReplaceExisting && matched.Count > 0)
        {
            var set = matched.ToHashSet(StringComparer.Ordinal);
            var existing = await _db.BldModelBindings.Where(b => b.TenantId == Tid && b.ProjectId == projectId
                    && b.Scope == BuildBindingScope.Element && b.ElementExternalId != null && set.Contains(b.ElementExternalId))
                .ToListAsync(ct);
            replaced = existing.Count;
            _db.BldModelBindings.RemoveRange(existing);
        }
        _db.BldModelBindings.AddRange(matched.Select(id => BuildModelStageBinding.Create(Tid, projectId, r.StageId, BuildBindingScope.Element,
            r.ModelVersionId, id, null, null, BuildBindingSource.Manual, _audit.UserId)));
        _audit.Record(AuditActions.BuildModelBindingSaved, "ModelBinding", r.StageId,
            $"{matched.Count} elemento(s) do modelo vinculados à etapa {stageName}.",
            new { projectId, r.ModelVersionId, r.Category, r.ZoneId, explicitIds = r.ExternalIds?.Count, matched = matched.Count, replaced });
        await _db.SaveBuildAsync(ct);
        return new BulkBuildBindingResultDto(matched.Count, matched.Count, replaced);
    }

    public async Task DeleteBindingAsync(Guid bindingId, CancellationToken ct)
    {
        var b = await _db.BldModelBindings.FirstOrDefaultAsync(x => x.TenantId == Tid && x.Id == bindingId, ct)
            ?? throw new NotFoundException("BuildModelStageBinding", bindingId);
        _db.BldModelBindings.Remove(b);
        _audit.Record(AuditActions.BuildModelBindingDeleted, "ModelBinding", b.Id, $"Vínculo ao modelo removido ({b.Scope}).");
        await _db.SaveBuildAsync(ct);
    }

    private async Task<string> StageNameAsync(Guid projectId, Guid stageId, CancellationToken ct)
        => await _db.BldStages.Where(s => s.TenantId == Tid && s.Id == stageId && s.ProjectId == projectId).Select(s => s.Name).FirstOrDefaultAsync(ct)
           ?? throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");

    private async Task EnsureVersionOfProjectAsync(Guid projectId, Guid versionId, CancellationToken ct)
    {
        if (!await _db.BldModelVersions.AnyAsync(v => v.TenantId == Tid && v.Id == versionId && v.ProjectId == projectId, ct))
            throw new DomainException("A versão do modelo não é desta obra.", "invalid_model_version");
    }

    private static BuildModelStageBindingDto Map(BuildModelStageBinding b, string stageName)
        => new(b.Id, b.StageId, stageName, b.Scope.ToString(), b.ModelVersionId, b.ElementExternalId, b.ZoneId, b.Category, b.Source.ToString(),
            BuildScope.Utc(b.CreatedAt));

    // ── Explicit element events ───────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildElementEventDto>> RecordEventsAsync(Guid projectId, RecordBuildElementEventsRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await EnsureVersionOfProjectAsync(projectId, r.ModelVersionId, ct);
        var ids = (r.ExternalIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count is 0 or > MaxBulkIds) throw new DomainException($"Informe de 1 a {MaxBulkIds} elementos.", "invalid_elements");
        var found = await _db.BldModelElements.Where(e => e.TenantId == Tid && e.ModelVersionId == r.ModelVersionId && ids.Contains(e.ExternalId))
            .Select(e => e.ExternalId).ToListAsync(ct);
        if (found.Count != ids.Count)
            throw new DomainException($"{ids.Count - found.Count} id(s) não existem nesta versão do modelo.", "element_not_found");
        var state = BuildTwinParse.Enum<BuildElementState>(r.State, "Estado");
        var source = string.IsNullOrWhiteSpace(r.Source) ? BuildElementEventSource.Manual : BuildTwinParse.Enum<BuildElementEventSource>(r.Source, "Origem");
        var date = DateOrToday(r.EffectiveDate);
        if (date > BusinessClock.Today()) throw new DomainException("A data do estado não pode ser futura.", "invalid_date");

        var events = ids.Select(id => BuildElementProgressEvent.Record(Tid, projectId, r.ModelVersionId, id, state, source, date,
            _audit.UserId, _audit.UserName, r.Note, r.SourceRef)).ToList();
        _db.BldElementEvents.AddRange(events);
        _audit.Record(AuditActions.BuildElementStateRecorded, "ModelVersion", r.ModelVersionId,
            $"Estado '{state}' registrado em {ids.Count} elemento(s) do modelo em {date:dd/MM/yyyy}.",
            new { projectId, count = ids.Count, state = state.ToString(), source = source.ToString(), date });
        await _db.SaveBuildAsync(ct);
        return events.Select(BuildTwinMap.Event).ToList();
    }

    public async Task<IReadOnlyList<BuildElementEventDto>> ElementEventsAsync(Guid versionId, string externalId, CancellationToken ct)
    {
        var v = await VersionAsync(versionId, ct);
        var id = (externalId ?? "").Trim();
        if (!await _db.BldModelElements.AnyAsync(e => e.TenantId == Tid && e.ModelVersionId == v.Id && e.ExternalId == id, ct))
            throw new NotFoundException("BuildModelElement", id);
        var rows = await _db.BldElementEvents.AsNoTracking()
            .Where(e => e.TenantId == Tid && e.ProjectId == v.ProjectId && e.ElementExternalId == id)
            .OrderByDescending(e => e.EffectiveDate).ThenByDescending(e => e.CreatedAt).Take(500).ToListAsync(ct);
        return rows.Select(BuildTwinMap.Event).ToList();
    }

    // ── Overview and 5D prep ──────────────────────────────────────────────────

    public async Task<BuildTwinOverviewDto> OverviewAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var models = await _db.BldModels.AsNoTracking().Where(m => m.TenantId == Tid && m.ProjectId == projectId).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var versions = await _db.BldModelVersions.AsNoTracking().Where(v => v.TenantId == Tid && v.ProjectId == projectId).ToListAsync(ct);
        var currentIds = models.Where(m => m.CurrentVersionId != null).Select(m => m.CurrentVersionId!.Value).ToHashSet();
        var versionIds = versions.Select(v => v.Id).ToList();
        var pendingJobs = await _db.BldModelJobs.AsNoTracking().CountAsync(j => j.TenantId == Tid && versionIds.Contains(j.VersionId)
            && (j.Status == BuildModelJobStatus.Pending || j.Status == BuildModelJobStatus.Running), ct);
        var manualZones = await _db.BldModelZones.AsNoTracking()
            .Where(z => z.TenantId == Tid && z.ProjectId == projectId && z.ModelVersionId == null)
            .Select(z => new { z.SimplifiedBoxJson }).ToListAsync(ct);

        var today = BusinessClock.Today();
        var total = 0;
        var bound = 0;
        var byStage = new Dictionary<Guid, int>();
        foreach (var vid in currentIds)
        {
            var r = await _reader.ComputeAsync(Tid, projectId, vid, today, bypass: false, ct);
            total += r.Elements.Count;
            foreach (var e in r.Elements.Where(e => e.StageId is not null))
            {
                bound++;
                byStage[e.StageId!.Value] = byStage.GetValueOrDefault(e.StageId!.Value) + 1;
            }
        }

        var stages = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name).ToListAsync(ct);
        var primary = await _reader.PrimaryVersionAsync(Tid, projectId, bypass: false, ct);
        var geometry = GeometryOf(primary, manualZones.Any(z => z.SimplifiedBoxJson != null));
        return new BuildTwinOverviewDto(projectId, await _settings.Build3DEnabledAsync(Tid, ct), geometry?.ToString() ?? "None",
            models.Select(m => BuildTwinMap.Model(m, versions)).ToList(), manualZones.Count, total, bound,
            total == 0 ? 0m : Math.Round(bound * 100m / total, 1), pendingJobs,
            stages.Select(s => new BuildTwinStageDto(s.Id, s.Name, s.Order, s.ProgressPercent, s.Status.ToString(), byStage.GetValueOrDefault(s.Id)))
                .ToList());
    }

    /// <summary>The geometry shown for a project: the primary version's, else boxes typed by the user, else none.</summary>
    public static BuildGeometrySource? GeometryOf(BuildModelVersion? primary, bool hasBoxes)
        => primary?.GeometrySource ?? (hasBoxes ? BuildGeometrySource.SimplifiedRepresentation : null);

    public const string CostMethodology =
        "Rateio por quantidade: o custo de cada etapa (orçado do orçamento de referência; realizado = lançamentos confirmados da obra "
        + "até a data) dividido pelos elementos do modelo vinculados a ela e pelas quantidades do IFC (volume, área). Preparação "
        + "para o 5D — não é orçamento por elemento.";

    public async Task<BuildTwinCostDto> CostAsync(Guid projectId, DateOnly? date, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var d = DateOrToday(date);
        var stages = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name).Select(s => new { s.Id, s.Name, s.Order }).ToListAsync(ct);
        var budgeted = (await _costs.BudgetedByStageAsync(projectId, ct)).Where(x => x.StageId is not null)
            .GroupBy(x => x.StageId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
        var realized = await _db.IntMovements.AsNoTracking()
            .Where(m => m.TenantId == Tid && m.ContextType == FinancialContextType.Obra && m.ContextId == projectId
                     && m.Status == MovementStatus.Confirmed && m.Direction == MovementDirection.Out && m.StageId != null && m.Date <= d)
            .GroupBy(m => m.StageId!.Value).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        var primary = await _reader.PrimaryVersionAsync(Tid, projectId, bypass: false, ct);
        var boundByStage = new Dictionary<Guid, List<string>>();
        var quantities = new Dictionary<string, (decimal? V, decimal? A, decimal? L)>(StringComparer.Ordinal);
        if (primary is not null)
        {
            var r = await _reader.ComputeAsync(Tid, projectId, primary.Id, d, bypass: false, ct);
            foreach (var e in r.Elements.Where(e => e.StageId is not null))
                (boundByStage.TryGetValue(e.StageId!.Value, out var l) ? l : boundByStage[e.StageId!.Value] = []).Add(e.ExternalId);
            var props = await _db.BldModelElements.AsNoTracking()
                .Where(e => e.TenantId == Tid && e.ModelVersionId == primary.Id && e.PropertiesJson != null)
                .Select(e => new { e.ExternalId, e.PropertiesJson }).ToListAsync(ct);
            foreach (var p in props) quantities[p.ExternalId] = Quantities(p.PropertiesJson!);
        }

        var rows = stages.Select(s =>
        {
            var ids = boundByStage.GetValueOrDefault(s.Id) ?? [];
            decimal? Sum(Func<(decimal? V, decimal? A, decimal? L), decimal?> pick)
            {
                var values = ids.Select(i => quantities.TryGetValue(i, out var q) ? pick(q) : null).Where(x => x is not null).ToList();
                return values.Count == 0 ? null : Math.Round(values.Sum(x => x!.Value), 3);
            }
            var b = budgeted.GetValueOrDefault(s.Id);
            var re = realized.GetValueOrDefault(s.Id);
            var vol = Sum(q => q.V);
            var area = Sum(q => q.A);
            return new BuildTwinStageCostDto(s.Id, s.Name, s.Order, b, re, ids.Count, vol, area, Sum(q => q.L),
                ids.Count == 0 ? null : Math.Round(b / ids.Count, 2),
                ids.Count == 0 ? null : Math.Round(re / ids.Count, 2),
                vol is > 0 ? Math.Round(re / vol.Value, 2) : null,
                area is > 0 ? Math.Round(re / area.Value, 2) : null);
        }).ToList();
        return new BuildTwinCostDto(projectId, d, primary?.Id, CostMethodology, rows);
    }

    private static (decimal? V, decimal? A, decimal? L) Quantities(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            decimal? Get(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.TryGetDecimal(out var d) ? d : null;
            return (Get("netVolume"), Get("netArea"), Get("length"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }
}
