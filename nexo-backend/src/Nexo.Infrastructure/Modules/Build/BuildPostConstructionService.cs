using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>Rules shared by the internal and the portal side of post-construction (explicit tenant on every query).</summary>
internal static class BuildPostConstructionRules
{
    /// <summary>Open (Open/InTreatment) Critical non-conformities of the project — they block the delivery.</summary>
    public static Task<int> OpenCriticalCountAsync(NexoDbContext db, Guid tenantId, Guid projectId, CancellationToken ct)
        => db.BldNonConformities.IgnoreQueryFilters().CountAsync(n => n.TenantId == tenantId && n.ProjectId == projectId
            && n.Severity == BuildNonConformitySeverity.Critical
            && (n.Status == BuildNonConformityStatus.Open || n.Status == BuildNonConformityStatus.InTreatment), ct);

    /// <summary>Accepted categories: the project's warranty systems plus "Outros".</summary>
    public static IReadOnlyList<string> Categories(IEnumerable<BuildWarrantyTerm> terms)
        => terms.Select(t => t.System).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)
            .Append(BuildWarrantyDefaults.Other).ToList();

    /// <summary>The category as stored: the matching warranty system's spelling, "Outros", or null when unknown.</summary>
    public static string? NormalizeCategory(IEnumerable<BuildWarrantyTerm> terms, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        return Categories(terms).FirstOrDefault(c => string.Equals(c, category.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> NextAssistanceNumberAsync(NexoDbContext db, Guid tenantId, Guid projectId, CancellationToken ct)
        => (await db.BldAssistanceRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ProjectId == projectId)
            .MaxAsync(a => (int?)a.Number, ct) ?? 0) + 1;
}

/// <summary>
/// Post-construction (Wave 3) — company side (tenant-scoped, Management): handover (walkthrough with the client,
/// delivery, acceptance), warranty terms and technical assistance (triage with warranty coverage, schedule, resolve).
/// Every change is audited.
/// </summary>
public sealed class BuildPostConstructionService
{
    public const string HandoverFolder = "build/handover";
    public const string HandoverContext = "build-handover";
    public const string AssistanceFolder = "build/assistance";
    public const string AssistanceContext = "build-assistance";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IPrivateFileUrls _urls;
    private readonly BuildQualityService _quality;

    public BuildPostConstructionService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IPrivateFileUrls urls,
        BuildQualityService quality)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _urls = urls;
        _quality = quality;
    }

    private Guid Tid => _tenant.Id;

    // ── Handover ──────────────────────────────────────────────────────────────

    public async Task<BuildHandoverDto> GetHandoverAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var h = await _db.BldHandovers.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == Tid && x.ProjectId == projectId, ct)
            ?? throw new NotFoundException("A entrega desta obra ainda não foi agendada.");
        return await MapHandoverAsync(h, ct);
    }

    /// <summary>Creates (first call) or updates the handover of the project. Document files: context build-handover.</summary>
    public async Task<BuildHandoverDto> SaveHandoverAsync(Guid projectId, SaveBuildHandoverRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        BuildScope.EnsureKeys(r.Documents?.Where(d => !string.IsNullOrWhiteSpace(d.FileKey)).Select(d => d.FileKey!), Tid,
            HandoverFolder, HandoverContext);
        var h = await LoadHandoverAsync(projectId, ct, required: false);
        if (h is null)
        {
            h = BuildHandover.Create(Tid, projectId, r.ScheduledFor);
            _db.BldHandovers.Add(h);
        }
        h.Update(r.ScheduledFor, r.KeysDelivered, r.Documents?.Select(d => new BuildHandoverDocument(d.Name, d.FileKey)).ToList(), r.Notes);
        _audit.Record(AuditActions.BuildHandoverSaved, "Handover", h.Id, "Entrega da obra agendada/alterada.",
            new { projectId, r.ScheduledFor, documents = r.Documents?.Count });
        await SaveUniqueAsync(ct, "A entrega desta obra já foi criada por outra pessoa. Recarregue.");
        return await GetHandoverAsync(projectId, ct);
    }

    /// <summary>Creates the client walkthrough (an inspection of kind ClientWalkthrough) linked to the handover.</summary>
    public async Task<BuildInspectionDto> CreateWalkthroughAsync(Guid projectId, CreateBuildWalkthroughRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var h = await LoadHandoverAsync(projectId, ct)
            ?? throw new NotFoundException("Agende a entrega antes de criar a vistoria com o cliente.");
        if (h.WalkthroughInspectionId is { } existing
            && await _db.BldInspections.AnyAsync(i => i.TenantId == Tid && i.Id == existing && i.Status != BuildInspectionStatus.Cancelled, ct))
            throw new ConflictException("Esta entrega já tem uma vistoria com o cliente.");
        var inspection = await _quality.StageInspectionAsync(projectId, BuildInspectionKind.ClientWalkthrough,
            string.IsNullOrWhiteSpace(r.Title) ? "Vistoria de entrega com o cliente" : r.Title, null, r.TemplateId,
            r.ScheduledFor ?? h.ScheduledFor, r.InspectorName, null, r.Items, ct);
        h.LinkWalkthrough(inspection.Id);
        await _db.SaveBuildAsync(ct);
        return await _quality.GetInspectionAsync(inspection.Id, ct);
    }

    /// <summary>
    /// Delivers the property: refused (422 handover_critical_nonconformity) while a Critical NC is open. Completes the
    /// project when its status allows (otherwise the status is kept and reported), starts the warranty terms on the
    /// delivery date and creates the default reference terms when the project has none.
    /// </summary>
    public async Task<BuildHandoverDto> DeliverAsync(Guid projectId, DeliverBuildHandoverRequest? r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var h = await LoadHandoverAsync(projectId, ct) ?? throw new NotFoundException("Agende a entrega antes de entregar o imóvel.");
        if (h.IsDelivered) throw new ConflictException("Este imóvel já foi entregue.");
        var critical = await BuildPostConstructionRules.OpenCriticalCountAsync(_db, Tid, projectId, ct);
        if (critical > 0)
            throw new DomainException($"A obra tem {critical} não conformidade(s) crítica(s) aberta(s); resolva antes da entrega.",
                "handover_critical_nonconformity");

        var now = DateTime.UtcNow;
        h.Deliver(r?.KeysDelivered, _audit.UserName, now);
        var project = await _db.BldProjects.FirstAsync(p => p.TenantId == Tid && p.Id == projectId, ct);
        var completed = false;
        if (project.Status is not (BuildProjectStatus.Completed or BuildProjectStatus.Cancelled))
        {
            project.Complete();
            completed = true;
        }

        var deliveredOn = BusinessClock.Today(now);
        var terms = await _db.BldWarrantyTerms.Where(t => t.TenantId == Tid && t.ProjectId == projectId).ToListAsync(ct);
        foreach (var t in terms) t.StartOn(deliveredOn);
        if (terms.Count == 0)
            _db.BldWarrantyTerms.AddRange(BuildWarrantyDefaults.Terms.Select(d =>
                BuildWarrantyTerm.Create(Tid, projectId, d.System, d.Months, deliveredOn, d.Description, isDefault: true)));

        _audit.Record(AuditActions.BuildHandoverDelivered, "Handover", h.Id, "Imóvel entregue ao cliente.",
            new { projectId, h.KeysDelivered, projectCompleted = completed, projectStatus = project.Status.ToString(),
                  defaultWarrantyTerms = terms.Count == 0 });
        await _db.SaveBuildAsync(ct);
        return await GetHandoverAsync(projectId, ct);
    }

    /// <summary>Records the client's acceptance internally (e.g. signed on paper): channel Internal.</summary>
    public async Task<BuildHandoverDto> AcceptAsync(Guid projectId, AcceptBuildHandoverRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var h = await LoadHandoverAsync(projectId, ct) ?? throw new NotFoundException("A entrega desta obra ainda não foi agendada.");
        h.Accept(r.SignerName, r.Comment, BuildDecisionChannel.Internal, DateTime.UtcNow);
        _audit.Record(AuditActions.BuildHandoverAccepted, "Handover", h.Id, $"Aceite da entrega registrado: {h.AcceptedByName}.",
            new { channel = "Internal" });
        await _db.SaveBuildAsync(ct);
        return await GetHandoverAsync(projectId, ct);
    }

    private async Task<BuildHandover?> LoadHandoverAsync(Guid projectId, CancellationToken ct, bool required = false)
    {
        var h = await _db.BldHandovers.FirstOrDefaultAsync(x => x.TenantId == Tid && x.ProjectId == projectId, ct);
        return h is null && required ? throw new NotFoundException("A entrega desta obra ainda não foi agendada.") : h;
    }

    private async Task<BuildHandoverDto> MapHandoverAsync(BuildHandover h, CancellationToken ct)
    {
        var project = await _db.BldProjects.AsNoTracking().FirstAsync(p => p.TenantId == Tid && p.Id == h.ProjectId, ct);
        var walkthroughStatus = h.WalkthroughInspectionId is { } wid
            ? await _db.BldInspections.AsNoTracking().Where(i => i.TenantId == Tid && i.Id == wid).Select(i => (BuildInspectionStatus?)i.Status)
                .FirstOrDefaultAsync(ct)
            : null;
        var critical = await BuildPostConstructionRules.OpenCriticalCountAsync(_db, Tid, h.ProjectId, ct);
        string? note = h.IsDelivered && project.Status != BuildProjectStatus.Completed
            ? $"A obra está com status {project.Status}; o status não foi alterado na entrega."
            : null;
        return new BuildHandoverDto(h.Id, h.ProjectId, h.Status.ToString(), h.ScheduledFor, h.WalkthroughInspectionId,
            walkthroughStatus?.ToString(), BuildScope.Utc(h.DeliveredAt), h.DeliveredByName, h.KeysDelivered,
            h.Documents.Select(d => new BuildHandoverDocumentDto(d.Name, d.FileKey, _urls.For(d.FileKey))).ToList(),
            h.Notes, BuildScope.Utc(h.AcceptedAt), h.AcceptedByName, h.AcceptanceChannel?.ToString(), h.AcceptanceComment,
            project.Status.ToString(), note, critical, h.CreatedAt, h.UpdatedAt);
    }

    // ── Warranty ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildWarrantyTermDto>> ListWarrantyAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var list = await _db.BldWarrantyTerms.AsNoTracking().Where(t => t.TenantId == Tid && t.ProjectId == projectId)
            .OrderByDescending(t => t.Months).ThenBy(t => t.System).ToListAsync(ct);
        return list.Select(MapTerm).ToList();
    }

    /// <summary>Creates a term; without a start it begins on the delivery date (or when the property is delivered).</summary>
    public async Task<BuildWarrantyTermDto> CreateWarrantyAsync(Guid projectId, SaveBuildWarrantyTermRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var term = BuildWarrantyTerm.Create(Tid, projectId, r.System, r.Months, r.StartsAt ?? await DeliveredOnAsync(projectId, ct),
            r.Description);
        _db.BldWarrantyTerms.Add(term);
        _audit.Record(AuditActions.BuildWarrantyTermSaved, "WarrantyTerm", term.Id, $"Garantia criada: {term.System} ({term.Months} meses).");
        await _db.SaveBuildAsync(ct);
        return MapTerm(term);
    }

    /// <summary>Creates the default reference terms when the project has none (starting on the delivery date, if any).</summary>
    public async Task<IReadOnlyList<BuildWarrantyTermDto>> CreateDefaultWarrantyAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        if (await _db.BldWarrantyTerms.AnyAsync(t => t.TenantId == Tid && t.ProjectId == projectId, ct))
            throw new ConflictException("Esta obra já tem garantias cadastradas.");
        var start = await DeliveredOnAsync(projectId, ct);
        _db.BldWarrantyTerms.AddRange(BuildWarrantyDefaults.Terms.Select(d =>
            BuildWarrantyTerm.Create(Tid, projectId, d.System, d.Months, start, d.Description, isDefault: true)));
        _audit.Record(AuditActions.BuildWarrantyTermSaved, "WarrantyTerm", projectId, "Garantias de referência criadas.");
        await _db.SaveBuildAsync(ct);
        return await ListWarrantyAsync(projectId, ct);
    }

    public async Task<BuildWarrantyTermDto> UpdateWarrantyAsync(Guid id, SaveBuildWarrantyTermRequest r, CancellationToken ct)
    {
        var term = await LoadTermAsync(id, ct);
        term.Update(r.System, r.Months, r.StartsAt ?? term.StartsAt, r.Description);
        _audit.Record(AuditActions.BuildWarrantyTermSaved, "WarrantyTerm", term.Id, $"Garantia alterada: {term.System} ({term.Months} meses).");
        await _db.SaveBuildAsync(ct);
        return MapTerm(term);
    }

    public async Task DeleteWarrantyAsync(Guid id, CancellationToken ct)
    {
        var term = await LoadTermAsync(id, ct);
        _db.BldWarrantyTerms.Remove(term);
        _audit.Record(AuditActions.BuildWarrantyTermDeleted, "WarrantyTerm", term.Id, $"Garantia excluída: {term.System}.");
        await _db.SaveBuildAsync(ct);
    }

    private async Task<DateOnly?> DeliveredOnAsync(Guid projectId, CancellationToken ct)
    {
        var delivered = await _db.BldHandovers.AsNoTracking()
            .Where(h => h.TenantId == Tid && h.ProjectId == projectId && h.Status == BuildHandoverStatus.Delivered)
            .Select(h => h.DeliveredAt).FirstOrDefaultAsync(ct);
        return delivered is { } d ? BusinessClock.Today(d) : null;
    }

    private async Task<BuildWarrantyTerm> LoadTermAsync(Guid id, CancellationToken ct)
        => await _db.BldWarrantyTerms.FirstOrDefaultAsync(t => t.TenantId == Tid && t.Id == id, ct)
           ?? throw new NotFoundException("Garantia não encontrada.");

    private static BuildWarrantyTermDto MapTerm(BuildWarrantyTerm t)
        => new(t.Id, t.ProjectId, t.System, t.Months, t.StartsAt, t.ExpiresAt, t.IsActive(BusinessClock.Today()), t.Description, t.IsDefault);

    // ── Assistance ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildAssistanceDto>> ListAssistanceAsync(Guid? projectId, string? status, bool? covered,
        CancellationToken ct)
    {
        if (projectId is { } pid) await BuildScope.EnsureProjectAsync(_db, Tid, pid, ct);
        var q = _db.BldAssistanceRequests.AsNoTracking().Where(a => a.TenantId == Tid);
        if (projectId is { } p) q = q.Where(a => a.ProjectId == p);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = BuildScope.ParseEnum(status, BuildAssistanceStatus.Open, "Status");
            q = q.Where(a => a.Status == s);
        }
        if (covered is { } c) q = q.Where(a => a.WarrantyCovered == c);
        var list = await q.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        return await MapAssistanceAsync(list, ct);
    }

    public async Task<BuildAssistanceDto> GetAssistanceAsync(Guid id, CancellationToken ct)
        => (await MapAssistanceAsync([await LoadAssistanceAsync(id, ct, tracking: false)], ct))[0];

    /// <summary>The company opens a request for the client (channel Internal). Category: a warranty system or "Outros".</summary>
    public async Task<BuildAssistanceDto> OpenAssistanceAsync(Guid projectId, OpenBuildAssistanceRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var terms = await _db.BldWarrantyTerms.AsNoTracking().Where(t => t.TenantId == Tid && t.ProjectId == projectId).ToListAsync(ct);
        var category = BuildPostConstructionRules.NormalizeCategory(terms, r.Category)
            ?? throw new DomainException("Categoria inválida: use um sistema da garantia desta obra ou \"Outros\".", "invalid_category");
        var request = BuildAssistanceRequest.Open(Tid, projectId,
            await BuildPostConstructionRules.NextAssistanceNumberAsync(_db, Tid, projectId, ct), category, r.Title, r.Description,
            r.Location, BuildDecisionChannel.Internal, string.IsNullOrWhiteSpace(r.OpenedByName) ? _audit.UserName : r.OpenedByName,
            r.ContactPhone, BusinessClock.Today());
        _db.BldAssistanceRequests.Add(request);
        _audit.Record(AuditActions.BuildAssistanceOpened, "AssistanceRequest", request.Id,
            $"Chamado de assistência #{request.Number} aberto: {request.Title}.", new { projectId, category, channel = "Internal" });
        await SaveUniqueAsync(ct, "Outro chamado foi aberto ao mesmo tempo nesta obra. Tente de novo.");
        return await GetAssistanceAsync(request.Id, ct);
    }

    /// <summary>
    /// Triage: the warranty coverage is evaluated from the project's term of the request's category on the opening date
    /// (covered = within the term's period), unless the company decides it explicitly. Notes are internal.
    /// </summary>
    public async Task<BuildAssistanceDto> TriageAsync(Guid id, TriageBuildAssistanceRequest? r, CancellationToken ct)
    {
        var a = await LoadAssistanceAsync(id, ct);
        var terms = await _db.BldWarrantyTerms.AsNoTracking().Where(t => t.TenantId == Tid && t.ProjectId == a.ProjectId).ToListAsync(ct);
        var coverage = BuildWarrantyCoverage.Evaluate(terms, a.Category, a.OpenedOn);
        var covered = r?.WarrantyCovered ?? coverage.Covered;
        a.Triage(covered, coverage.TermId, r?.Notes, _audit.UserName, DateTime.UtcNow);
        _audit.Record(AuditActions.BuildAssistanceStatusChanged, "AssistanceRequest", a.Id, $"Chamado #{a.Number} triado.",
            new { evaluated = coverage.Covered, covered, termId = coverage.TermId, manual = r?.WarrantyCovered is not null });
        await _db.SaveBuildAsync(ct);
        return await GetAssistanceAsync(id, ct);
    }

    public Task<BuildAssistanceDto> ScheduleAsync(Guid id, DateTimeOffset when, CancellationToken ct)
        => AssistanceTransitionAsync(id, a => a.Schedule(when.UtcDateTime), "agendado", ct);

    public Task<BuildAssistanceDto> StartAssistanceAsync(Guid id, CancellationToken ct)
        => AssistanceTransitionAsync(id, a => a.Start(DateTime.UtcNow), "em andamento", ct);

    public Task<BuildAssistanceDto> ResolveAssistanceAsync(Guid id, string notes, CancellationToken ct)
        => AssistanceTransitionAsync(id, a => a.Resolve(notes, DateTime.UtcNow), "resolvido", ct);

    public Task<BuildAssistanceDto> CloseAssistanceAsync(Guid id, CancellationToken ct)
        => AssistanceTransitionAsync(id, a => a.Close(DateTime.UtcNow), "encerrado", ct);

    public Task<BuildAssistanceDto> RejectAssistanceAsync(Guid id, string reason, CancellationToken ct)
        => AssistanceTransitionAsync(id, a => a.Reject(reason, DateTime.UtcNow), "recusado", ct);

    /// <summary>Attaches a photo uploaded with the context build-assistance (max 6 per request).</summary>
    public async Task<BuildAssistanceDto> AddAssistancePhotoAsync(Guid id, string key, CancellationToken ct)
    {
        BuildScope.EnsureKeys([key], Tid, AssistanceFolder, AssistanceContext);
        var a = await LoadAssistanceAsync(id, ct);
        a.AddPhoto(key);
        _audit.Record(AuditActions.BuildAssistancePhotoAdded, "AssistanceRequest", a.Id, $"Foto anexada ao chamado #{a.Number}.");
        await _db.SaveBuildAsync(ct);
        return await GetAssistanceAsync(id, ct);
    }

    private async Task<BuildAssistanceDto> AssistanceTransitionAsync(Guid id, Action<BuildAssistanceRequest> change, string label,
        CancellationToken ct)
    {
        var a = await LoadAssistanceAsync(id, ct);
        var from = a.Status;
        change(a);
        _audit.Record(AuditActions.BuildAssistanceStatusChanged, "AssistanceRequest", a.Id, $"Chamado #{a.Number} {label}.",
            new { from = from.ToString(), to = a.Status.ToString() });
        await _db.SaveBuildAsync(ct);
        return await GetAssistanceAsync(id, ct);
    }

    private async Task<BuildAssistanceRequest> LoadAssistanceAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldAssistanceRequests.Where(a => a.TenantId == Tid && a.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Chamado de assistência não encontrado.");
    }

    private async Task<IReadOnlyList<BuildAssistanceDto>> MapAssistanceAsync(IReadOnlyList<BuildAssistanceRequest> list, CancellationToken ct)
    {
        var projectIds = list.Select(a => a.ProjectId).Distinct().ToList();
        var termIds = list.Where(a => a.WarrantyTermId.HasValue).Select(a => a.WarrantyTermId!.Value).Distinct().ToList();
        var projects = await _db.BldProjects.AsNoTracking().Where(p => p.TenantId == Tid && projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var terms = await _db.BldWarrantyTerms.AsNoTracking().Where(t => t.TenantId == Tid && termIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.System, ct);
        return list.Select(a => new BuildAssistanceDto(a.Id, a.PublicId, a.ProjectId, projects.GetValueOrDefault(a.ProjectId) ?? "",
            a.Number, a.Category, a.Title, a.Description, a.Location, a.OpenedVia.ToString(), a.OpenedByName, a.ContactPhone, a.OpenedOn,
            a.Status.ToString(), a.TriageNotes, a.WarrantyCovered, a.WarrantyTermId,
            a.WarrantyTermId is { } tid ? terms.GetValueOrDefault(tid) : null, BuildScope.Utc(a.TriagedAt), a.TriagedByName,
            BuildScope.Utc(a.ScheduledFor), BuildScope.Utc(a.StartedAt), BuildScope.Utc(a.ResolvedAt), a.ResolutionNotes,
            BuildScope.Utc(a.ClosedAt), BuildScope.Utc(a.RejectedAt), a.RejectionReason, a.ClientRating, a.ClientFeedback, a.PhotoKeys,
            a.PhotoKeys.Select(k => _urls.For(k)).Where(u => u is not null).Select(u => u!).ToList(), a.CreatedAt, a.UpdatedAt)).ToList();
    }

    private async Task SaveUniqueAsync(CancellationToken ct, string conflictMessage)
    {
        try
        {
            await _db.SaveBuildAsync(ct);
        }
        catch (DbUpdateException ex) when (BuildScope.IsUniqueViolation(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }
}
