using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>Shared checks of the Wave 3 services (tenant-scoped, explicit tenant predicate on every query).</summary>
internal static class BuildScope
{
    public static async Task EnsureProjectAsync(NexoDbContext db, Guid tenantId, Guid projectId, CancellationToken ct)
    {
        if (!await db.BldProjects.AnyAsync(p => p.TenantId == tenantId && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    public static async Task EnsureStageAsync(NexoDbContext db, Guid tenantId, Guid projectId, Guid? stageId, CancellationToken ct)
    {
        if (stageId is { } sid
            && !await db.BldStages.AnyAsync(s => s.TenantId == tenantId && s.Id == sid && s.ProjectId == projectId, ct))
            throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
    }

    /// <summary>Keys attached to a record must be private files of this tenant in <paramref name="folder"/>.</summary>
    public static void EnsureKeys(IEnumerable<string>? keys, Guid tenantId, string folder, string context)
    {
        foreach (var key in keys ?? [])
            if (!StorageKeys.IsPrivateOf(key, tenantId, folder))
                throw new DomainException($"Arquivo inválido: envie pelo upload com o contexto {context}.", "invalid_file_key");
    }

    public static TEnum ParseEnum<TEnum>(string? value, TEnum fallback, string what) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return Enum.TryParse<TEnum>(value, true, out var v) && Enum.IsDefined(v)
            ? v
            : throw new DomainException($"{what} inválido(a): '{value}'.", "invalid_value");
    }

    public static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc), TimeSpan.Zero);

    public static DateTimeOffset? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    public static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>
/// Quality (Wave 3) — company side (tenant-scoped, Management): checklist templates, inspections (quality and the
/// client walkthrough of the handover), non-conformities and the quality summary. Every change is audited.
/// Completing an inspection opens one non-conformity per NotOk item, in the same save.
/// </summary>
public sealed class BuildQualityService
{
    /// <summary>Private storage folder of inspection/NC photos (upload context build-quality).</summary>
    public const string PhotoFolder = "build/quality";
    public const string PhotoContext = "build-quality";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IPrivateFileUrls _urls;

    public BuildQualityService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IPrivateFileUrls urls)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _urls = urls;
    }

    private Guid Tid => _tenant.Id;

    // ── Checklist templates ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildChecklistTemplateDto>> GetTemplatesAsync(CancellationToken ct)
    {
        var list = await _db.BldChecklistTemplates.AsNoTracking().Where(t => t.TenantId == Tid).OrderBy(t => t.Name).ToListAsync(ct);
        return await MapTemplatesAsync(list, ct);
    }

    public async Task<BuildChecklistTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct)
        => (await MapTemplatesAsync([await LoadTemplateAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildChecklistTemplateDto> CreateTemplateAsync(SaveBuildChecklistTemplateRequest r, CancellationToken ct)
    {
        var t = BuildChecklistTemplate.Create(Tid, r.Name, r.Category, r.StageNameHint, _audit.UserId);
        _db.BldChecklistTemplates.Add(t);
        _db.BldChecklistTemplateItems.AddRange(TemplateItems(t.Id, r.Items));
        _audit.Record(AuditActions.BuildChecklistTemplateSaved, "ChecklistTemplate", t.Id, $"Checklist criado: {t.Name}.");
        await _db.SaveBuildAsync(ct);
        return await GetTemplateAsync(t.Id, ct);
    }

    /// <summary>Replaces name and items. Past inspections keep their own copy of the items.</summary>
    public async Task<BuildChecklistTemplateDto> UpdateTemplateAsync(Guid id, SaveBuildChecklistTemplateRequest r, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(id, ct);
        t.Update(r.Name, r.Category, r.StageNameHint);
        var old = await _db.BldChecklistTemplateItems.Where(i => i.TenantId == Tid && i.TemplateId == id).ToListAsync(ct);
        _db.BldChecklistTemplateItems.RemoveRange(old);
        _db.BldChecklistTemplateItems.AddRange(TemplateItems(id, r.Items));
        _audit.Record(AuditActions.BuildChecklistTemplateSaved, "ChecklistTemplate", t.Id, $"Checklist alterado: {t.Name}.");
        await _db.SaveBuildAsync(ct);
        return await GetTemplateAsync(id, ct);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(id, ct);
        _db.BldChecklistTemplates.Remove(t);
        _audit.Record(AuditActions.BuildChecklistTemplateDeleted, "ChecklistTemplate", t.Id, $"Checklist excluído: {t.Name}.");
        await _db.SaveBuildAsync(ct);
    }

    private List<BuildChecklistTemplateItem> TemplateItems(Guid templateId, IReadOnlyList<BuildChecklistItemRequest>? items)
    {
        var order = 0;
        return (items ?? []).Select(i => BuildChecklistTemplateItem.Create(Tid, templateId, ++order, i.Text, i.Required, i.RequiresPhoto))
            .ToList();
    }

    private async Task<BuildChecklistTemplate> LoadTemplateAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldChecklistTemplates.Where(t => t.TenantId == Tid && t.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Checklist não encontrado.");
    }

    private async Task<IReadOnlyList<BuildChecklistTemplateDto>> MapTemplatesAsync(IReadOnlyList<BuildChecklistTemplate> list,
        CancellationToken ct)
    {
        var ids = list.Select(t => t.Id).ToList();
        var items = (await _db.BldChecklistTemplateItems.AsNoTracking()
            .Where(i => i.TenantId == Tid && ids.Contains(i.TemplateId)).ToListAsync(ct)).ToLookup(i => i.TemplateId);
        return list.Select(t => new BuildChecklistTemplateDto(t.Id, t.Name, t.Category, t.StageNameHint,
            items[t.Id].OrderBy(i => i.Order).Select(i => new BuildChecklistTemplateItemDto(i.Id, i.Order, i.Text, i.Required, i.RequiresPhoto))
                .ToList(),
            t.CreatedAt, t.UpdatedAt)).ToList();
    }

    // ── Inspections ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildInspectionDto>> ListInspectionsAsync(Guid projectId, string? kind, string? status,
        CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var q = _db.BldInspections.AsNoTracking().Where(i => i.TenantId == Tid && i.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var k = BuildScope.ParseEnum(kind, BuildInspectionKind.Quality, "Tipo de vistoria");
            q = q.Where(i => i.Kind == k);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = BuildScope.ParseEnum(status, BuildInspectionStatus.Planned, "Status");
            q = q.Where(i => i.Status == s);
        }
        var list = await q.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return await MapInspectionsAsync(list, ct);
    }

    public async Task<BuildInspectionDto> GetInspectionAsync(Guid id, CancellationToken ct)
        => (await MapInspectionsAsync([await LoadInspectionAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildInspectionDto> CreateInspectionAsync(Guid projectId, CreateBuildInspectionRequest r, CancellationToken ct)
    {
        var i = await StageInspectionAsync(projectId, BuildInspectionKind.Quality, r.Title, r.StageId, r.TemplateId, r.ScheduledFor,
            r.InspectorName, r.Notes, r.Items, ct);
        await _db.SaveBuildAsync(ct);
        return await GetInspectionAsync(i.Id, ct);
    }

    /// <summary>Adds (unsaved) an inspection with the template's items copied (snapshot) plus the ad-hoc items.</summary>
    internal async Task<BuildInspection> StageInspectionAsync(Guid projectId, BuildInspectionKind kind, string title, Guid? stageId,
        Guid? templateId, DateOnly? scheduledFor, string? inspectorName, string? notes, IReadOnlyList<BuildChecklistItemRequest>? extra,
        CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await BuildScope.EnsureStageAsync(_db, Tid, projectId, stageId, ct);
        var copied = new List<BuildChecklistTemplateItem>();
        if (templateId is { } tid)
        {
            _ = await LoadTemplateAsync(tid, ct, tracking: false);
            copied = await _db.BldChecklistTemplateItems.AsNoTracking()
                .Where(i => i.TenantId == Tid && i.TemplateId == tid).OrderBy(i => i.Order).ToListAsync(ct);
        }
        var inspection = BuildInspection.Create(Tid, projectId, kind, title, stageId, templateId, scheduledFor, inspectorName, notes,
            _audit.UserId);
        _db.BldInspections.Add(inspection);
        var order = 0;
        foreach (var c in copied)
            _db.BldInspectionItems.Add(BuildInspectionItem.Create(Tid, inspection.Id, ++order, c.Text, c.Required, c.RequiresPhoto));
        foreach (var e in extra ?? [])
            _db.BldInspectionItems.Add(BuildInspectionItem.Create(Tid, inspection.Id, ++order, e.Text, e.Required, e.RequiresPhoto));
        _audit.Record(AuditActions.BuildInspectionSaved, "Inspection", inspection.Id,
            $"Vistoria {(kind == BuildInspectionKind.ClientWalkthrough ? "de entrega" : "de qualidade")} criada: {inspection.Title}.",
            new { projectId, kind = kind.ToString(), templateId, items = order });
        return inspection;
    }

    public async Task<BuildInspectionDto> UpdateInspectionAsync(Guid id, UpdateBuildInspectionRequest r, CancellationToken ct)
    {
        var i = await LoadInspectionAsync(id, ct);
        await BuildScope.EnsureStageAsync(_db, Tid, i.ProjectId, r.StageId, ct);
        i.Update(r.Title, r.StageId, r.ScheduledFor, r.InspectorName, r.Notes);
        _audit.Record(AuditActions.BuildInspectionSaved, "Inspection", i.Id, $"Vistoria alterada: {i.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetInspectionAsync(id, ct);
    }

    public async Task<BuildInspectionDto> StartInspectionAsync(Guid id, CancellationToken ct)
    {
        var i = await LoadInspectionAsync(id, ct);
        i.Start(DateTime.UtcNow);
        _audit.Record(AuditActions.BuildInspectionSaved, "Inspection", i.Id, $"Vistoria iniciada: {i.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetInspectionAsync(id, ct);
    }

    /// <summary>Records one item's result (the inspection moves to InProgress). Photo keys: context build-quality.</summary>
    public async Task<BuildInspectionDto> RecordItemAsync(Guid inspectionId, Guid itemId, RecordBuildInspectionItemRequest r,
        CancellationToken ct)
    {
        var i = await LoadInspectionAsync(inspectionId, ct);
        i.EnsureOpen();
        var item = await _db.BldInspectionItems.FirstOrDefaultAsync(x => x.TenantId == Tid && x.Id == itemId && x.InspectionId == inspectionId, ct)
            ?? throw new NotFoundException("Item da vistoria não encontrado.");
        BuildScope.EnsureKeys(r.PhotoKeys, Tid, PhotoFolder, PhotoContext);
        var result = BuildScope.ParseEnum(r.Result, BuildInspectionItemResult.Pending, "Resultado");
        BuildNonConformitySeverity? severity = string.IsNullOrWhiteSpace(r.Severity)
            ? null
            : BuildScope.ParseEnum(r.Severity, BuildNonConformitySeverity.Medium, "Gravidade");
        item.Record(result, r.Comment, r.PhotoKeys, severity);
        i.Start(DateTime.UtcNow);
        await _db.SaveBuildAsync(ct);
        return await GetInspectionAsync(inspectionId, ct);
    }

    /// <summary>
    /// Completes with the computed outcome; each NotOk item opens a non-conformity (severity of the item; stage of the
    /// inspection; photos copied). A completed client walkthrough moves its handover to InspectionDone.
    /// </summary>
    public async Task<BuildInspectionDto> CompleteInspectionAsync(Guid id, string? notes, CancellationToken ct)
    {
        var inspection = await LoadInspectionAsync(id, ct);
        var items = await _db.BldInspectionItems.Where(x => x.TenantId == Tid && x.InspectionId == id).OrderBy(x => x.Order).ToListAsync(ct);
        var outcome = inspection.Complete(items, notes, _audit.UserName, DateTime.UtcNow);

        var notOk = items.Where(x => x.Result == BuildInspectionItemResult.NotOk).ToList();
        if (notOk.Count > 0)
        {
            var next = await NextNcNumberAsync(inspection.ProjectId, ct);
            foreach (var item in notOk)
            {
                var title = $"{inspection.Title}: {item.Text}";
                var nc = BuildNonConformity.Create(Tid, inspection.ProjectId, next++, title.Length > 200 ? title[..200] : title,
                    item.Comment, item.NonConformitySeverity, inspection.StageId, null, null, null, item.PhotoKeys,
                    _audit.UserId, _audit.UserName, inspection.Id, item.Id);
                _db.BldNonConformities.Add(nc);
            }
        }

        if (inspection.Kind == BuildInspectionKind.ClientWalkthrough)
        {
            var handover = await _db.BldHandovers.FirstOrDefaultAsync(h => h.TenantId == Tid && h.WalkthroughInspectionId == id, ct);
            handover?.MarkInspectionDone();
        }

        _audit.Record(AuditActions.BuildInspectionCompleted, "Inspection", inspection.Id,
            $"Vistoria concluída: {inspection.Title} — {outcome}.",
            new { outcome = outcome.ToString(), notOk = notOk.Count, nonConformities = notOk.Count });
        await SaveNumberedAsync(ct);
        return await GetInspectionAsync(id, ct);
    }

    public async Task<BuildInspectionDto> CancelInspectionAsync(Guid id, CancellationToken ct)
    {
        var i = await LoadInspectionAsync(id, ct);
        i.Cancel();
        _audit.Record(AuditActions.BuildInspectionCancelled, "Inspection", i.Id, $"Vistoria cancelada: {i.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetInspectionAsync(id, ct);
    }

    private async Task<BuildInspection> LoadInspectionAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldInspections.Where(i => i.TenantId == Tid && i.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Vistoria não encontrada.");
    }

    internal async Task<IReadOnlyList<BuildInspectionDto>> MapInspectionsAsync(IReadOnlyList<BuildInspection> list, CancellationToken ct)
    {
        var ids = list.Select(i => i.Id).ToList();
        var stageIds = list.Where(i => i.StageId.HasValue).Select(i => i.StageId!.Value).Distinct().ToList();
        var items = (await _db.BldInspectionItems.AsNoTracking().Where(x => x.TenantId == Tid && ids.Contains(x.InspectionId))
            .ToListAsync(ct)).ToLookup(x => x.InspectionId);
        var ncByItem = await _db.BldNonConformities.AsNoTracking()
            .Where(n => n.TenantId == Tid && n.InspectionId != null && ids.Contains(n.InspectionId.Value) && n.InspectionItemId != null)
            .Select(n => new { n.InspectionItemId, n.Id })
            .ToListAsync(ct);
        var ncs = ncByItem.GroupBy(n => n.InspectionItemId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
        var stages = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return list.Select(i =>
        {
            var lines = items[i.Id].OrderBy(x => x.Order).ToList();
            return new BuildInspectionDto(i.Id, i.ProjectId, i.Kind.ToString(), i.StageId,
                i.StageId is { } sid ? stages.GetValueOrDefault(sid) : null, i.TemplateId, i.Title, i.ScheduledFor, i.Status.ToString(),
                i.InspectorName, BuildScope.Utc(i.StartedAt), BuildScope.Utc(i.CompletedAt), i.CompletedByName, i.Notes,
                lines.Count(x => x.Result == BuildInspectionItemResult.Ok),
                lines.Count(x => x.Result == BuildInspectionItemResult.NotOk),
                lines.Count(x => x.Result == BuildInspectionItemResult.Pending),
                lines.Select(x => new BuildInspectionItemDto(x.Id, x.Order, x.Text, x.Required, x.RequiresPhoto, x.Result.ToString(),
                    x.Comment, x.Severity?.ToString(), x.PhotoKeys, Urls(x.PhotoKeys),
                    ncs.TryGetValue(x.Id, out var ncId) ? ncId : null)).ToList(),
                i.CreatedAt, i.UpdatedAt);
        }).ToList();
    }

    // ── Non-conformities ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildNonConformityDto>> ListNonConformitiesAsync(Guid projectId, string? status, string? severity,
        bool? overdue, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var q = _db.BldNonConformities.AsNoTracking().Where(n => n.TenantId == Tid && n.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = BuildScope.ParseEnum(status, BuildNonConformityStatus.Open, "Status");
            q = q.Where(n => n.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var sv = BuildScope.ParseEnum(severity, BuildNonConformitySeverity.Medium, "Gravidade");
            q = q.Where(n => n.Severity == sv);
        }
        if (overdue == true)
        {
            var today = BusinessClock.Today();
            q = q.Where(n => (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment)
                             && n.DueDate != null && n.DueDate < today);
        }
        var list = await q.OrderByDescending(n => n.Severity).ThenBy(n => n.Number).ToListAsync(ct);
        return await MapNcsAsync(list, ct);
    }

    public async Task<BuildNonConformityDto> GetNonConformityAsync(Guid id, CancellationToken ct)
        => (await MapNcsAsync([await LoadNcAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildNonConformityDto> CreateNonConformityAsync(Guid projectId, SaveBuildNonConformityRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await BuildScope.EnsureStageAsync(_db, Tid, projectId, r.StageId, ct);
        BuildScope.EnsureKeys(r.PhotoKeys, Tid, PhotoFolder, PhotoContext);
        var severity = BuildScope.ParseEnum(r.Severity, BuildNonConformitySeverity.Medium, "Gravidade");
        var nc = BuildNonConformity.Create(Tid, projectId, await NextNcNumberAsync(projectId, ct), r.Title, r.Description, severity,
            r.StageId, r.ResponsibleName, r.DueDate, r.CostImpact, r.PhotoKeys, _audit.UserId, _audit.UserName);
        if (r.RootCause is not null || r.CorrectiveAction is not null)
            nc.Update(r.Title, r.Description, severity, r.StageId, r.ResponsibleName, r.DueDate, r.CostImpact, r.PhotoKeys,
                r.RootCause, r.CorrectiveAction);
        _db.BldNonConformities.Add(nc);
        _audit.Record(AuditActions.BuildNonConformitySaved, "NonConformity", nc.Id, $"Não conformidade #{nc.Number} aberta: {nc.Title}.",
            new { projectId, severity = severity.ToString(), r.StageId });
        await SaveNumberedAsync(ct);
        return await GetNonConformityAsync(nc.Id, ct);
    }

    public async Task<BuildNonConformityDto> UpdateNonConformityAsync(Guid id, SaveBuildNonConformityRequest r, CancellationToken ct)
    {
        var nc = await LoadNcAsync(id, ct);
        await BuildScope.EnsureStageAsync(_db, Tid, nc.ProjectId, r.StageId, ct);
        BuildScope.EnsureKeys(r.PhotoKeys, Tid, PhotoFolder, PhotoContext);
        var severity = BuildScope.ParseEnum(r.Severity, nc.Severity, "Gravidade");
        nc.Update(r.Title, r.Description, severity, r.StageId, r.ResponsibleName, r.DueDate, r.CostImpact, r.PhotoKeys,
            r.RootCause, r.CorrectiveAction);
        _audit.Record(AuditActions.BuildNonConformitySaved, "NonConformity", nc.Id, $"Não conformidade #{nc.Number} alterada.",
            new { severity = severity.ToString() });
        await _db.SaveBuildAsync(ct);
        return await GetNonConformityAsync(id, ct);
    }

    public Task<BuildNonConformityDto> StartTreatmentAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, nc => nc.StartTreatment(), "em tratamento", ct);

    public Task<BuildNonConformityDto> ResolveNonConformityAsync(Guid id, ResolveBuildNonConformityRequest? r, CancellationToken ct)
        => TransitionAsync(id, nc => nc.Resolve(r?.CorrectiveAction, r?.RootCause, _audit.UserId, _audit.UserName, DateTime.UtcNow),
            "resolvida", ct);

    public Task<BuildNonConformityDto> VerifyNonConformityAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, nc => nc.Verify(_audit.UserId, _audit.UserName, DateTime.UtcNow), "verificada", ct);

    public Task<BuildNonConformityDto> ReopenNonConformityAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, nc => nc.Reopen(), "reaberta", ct);

    public Task<BuildNonConformityDto> CancelNonConformityAsync(Guid id, string reason, CancellationToken ct)
        => TransitionAsync(id, nc => nc.Cancel(reason), "cancelada", ct);

    private async Task<BuildNonConformityDto> TransitionAsync(Guid id, Action<BuildNonConformity> change, string label, CancellationToken ct)
    {
        var nc = await LoadNcAsync(id, ct);
        var from = nc.Status;
        change(nc);
        _audit.Record(AuditActions.BuildNonConformityStatusChanged, "NonConformity", nc.Id, $"Não conformidade #{nc.Number} {label}.",
            new { from = from.ToString(), to = nc.Status.ToString(), verifiedBySameUser = nc.VerifiedBySameUser ? true : (bool?)null });
        await _db.SaveBuildAsync(ct);
        return await GetNonConformityAsync(id, ct);
    }

    private async Task<int> NextNcNumberAsync(Guid projectId, CancellationToken ct)
        => (await _db.BldNonConformities.AsNoTracking().Where(n => n.TenantId == Tid && n.ProjectId == projectId)
            .MaxAsync(n => (int?)n.Number, ct) ?? 0) + 1;

    /// <summary>Two NCs numbered at the same time in the same project: the second save is a conflict (409), never a 500.</summary>
    private async Task SaveNumberedAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveBuildAsync(ct);
        }
        catch (DbUpdateException ex) when (BuildScope.IsUniqueViolation(ex))
        {
            throw new ConflictException("Outra não conformidade foi registrada ao mesmo tempo nesta obra. Tente de novo.");
        }
    }

    private async Task<BuildNonConformity> LoadNcAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldNonConformities.Where(n => n.TenantId == Tid && n.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Não conformidade não encontrada.");
    }

    private async Task<IReadOnlyList<BuildNonConformityDto>> MapNcsAsync(IReadOnlyList<BuildNonConformity> list, CancellationToken ct)
    {
        var stageIds = list.Where(n => n.StageId.HasValue).Select(n => n.StageId!.Value).Distinct().ToList();
        var stages = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var today = BusinessClock.Today();
        return list.Select(n => new BuildNonConformityDto(n.Id, n.ProjectId, n.Number, n.StageId,
            n.StageId is { } sid ? stages.GetValueOrDefault(sid) : null, n.InspectionId, n.InspectionItemId, n.Title, n.Description,
            n.Severity.ToString(), n.Status.ToString(), n.ResponsibleName, n.DueDate, n.IsOverdue(today), n.RootCause, n.CorrectiveAction,
            n.PhotoKeys, Urls(n.PhotoKeys), n.CostImpact, BuildScope.Utc(n.ResolvedAt), n.ResolvedByName, BuildScope.Utc(n.VerifiedAt),
            n.VerifiedByName, n.VerifiedBySameUser, n.CancelReason, n.CreatedByName, n.CreatedAt, n.UpdatedAt)).ToList();
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    public async Task<BuildQualitySummaryDto> GetSummaryAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var inspections = await _db.BldInspections.AsNoTracking()
            .Where(i => i.TenantId == Tid && i.ProjectId == projectId && i.Kind == BuildInspectionKind.Quality)
            .GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var byStatus = Enum.GetValues<BuildInspectionStatus>()
            .ToDictionary(s => s.ToString(), s => inspections.FirstOrDefault(x => x.Status == s)?.Count ?? 0);
        var passed = byStatus[nameof(BuildInspectionStatus.Passed)] + byStatus[nameof(BuildInspectionStatus.PassedWithRemarks)];
        var completed = passed + byStatus[nameof(BuildInspectionStatus.Failed)];

        var today = BusinessClock.Today();
        var ncs = await _db.BldNonConformities.AsNoTracking()
            .Where(n => n.TenantId == Tid && n.ProjectId == projectId
                        && (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment
                            || n.Status == BuildNonConformityStatus.Resolved))
            .Select(n => new { n.Status, n.Severity, n.DueDate })
            .ToListAsync(ct);
        var open = ncs.Where(n => n.Status != BuildNonConformityStatus.Resolved).ToList();
        return new BuildQualitySummaryDto(
            projectId, byStatus, completed,
            completed == 0 ? null : Math.Round(100m * passed / completed, 1, MidpointRounding.AwayFromZero),
            open.Count,
            Enum.GetValues<BuildNonConformitySeverity>().ToDictionary(s => s.ToString(), s => open.Count(n => n.Severity == s)),
            open.Count(n => n.DueDate is { } d && d < today),
            ncs.Count(n => n.Status == BuildNonConformityStatus.Resolved),
            open.Count(n => n.Severity == BuildNonConformitySeverity.Critical));
    }

    private IReadOnlyList<string> Urls(IReadOnlyList<string> keys)
        => keys.Select(k => _urls.For(k)).Where(u => u is not null).Select(u => u!).ToList();
}
