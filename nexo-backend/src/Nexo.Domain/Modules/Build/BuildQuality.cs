using System.Text.Json;
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 3 — QUALITY: checklist templates, inspections (quality and client walkthrough), non-conformities.
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Private photo keys kept as a JSON list (column json) — shared by inspection items, NCs and assistance.</summary>
public static class BuildPhotoKeys
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Read(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    public static string Write(IEnumerable<string>? keys)
        => JsonSerializer.Serialize((keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList(), Json);
}

/// <summary>A reusable checklist (e.g. "Alvenaria — conferência"): the items are copied into each inspection created from it.</summary>
public class BuildChecklistTemplate : TenantEntity
{
    private BuildChecklistTemplate() { }
    private BuildChecklistTemplate(Guid tenantId) : base(tenantId) { }

    public string  Name          { get; private set; } = string.Empty;
    /// <summary>Discipline or category (free text, e.g. "Estrutura", "Instalações").</summary>
    public string? Category      { get; private set; }
    /// <summary>Hint of the stage it is usually used in (free text — templates are not tied to a project).</summary>
    public string? StageNameHint { get; private set; }
    public Guid    CreatedBy     { get; private set; }

    public static BuildChecklistTemplate Create(Guid tenantId, string name, string? category, string? stageNameHint, Guid createdBy)
    {
        var t = new BuildChecklistTemplate(tenantId) { CreatedBy = createdBy };
        t.Apply(name, category, stageNameHint);
        return t;
    }

    public void Update(string name, string? category, string? stageNameHint)
    {
        Apply(name, category, stageNameHint);
        SetUpdatedAt();
    }

    private void Apply(string name, string? category, string? stageNameHint)
    {
        Name          = BuildMoney.Required(name, 200, "O nome do checklist");
        Category      = BuildMoney.Clean(category, 100, "A disciplina");
        StageNameHint = BuildMoney.Clean(stageNameHint, 200, "A etapa sugerida");
    }
}

public class BuildChecklistTemplateItem : TenantEntity
{
    private BuildChecklistTemplateItem() { }
    private BuildChecklistTemplateItem(Guid tenantId) : base(tenantId) { }

    public Guid   TemplateId    { get; private set; }
    public int    Order         { get; private set; }
    public string Text          { get; private set; } = string.Empty;
    public bool   Required      { get; private set; }
    public bool   RequiresPhoto { get; private set; }

    public static BuildChecklistTemplateItem Create(Guid tenantId, Guid templateId, int order, string text, bool required,
        bool requiresPhoto)
    {
        if (templateId == Guid.Empty) throw new DomainException("Checklist inválido.");
        return new BuildChecklistTemplateItem(tenantId)
        {
            TemplateId    = templateId,
            Order         = Math.Max(0, order),
            Text          = BuildMoney.Required(text, 500, "O texto do item"),
            Required      = required,
            RequiresPhoto = requiresPhoto,
        };
    }
}

public enum BuildInspectionKind
{
    /// <summary>Internal quality inspection (vistoria de qualidade).</summary>
    Quality           = 1,
    /// <summary>Walkthrough with the client before the handover (vistoria de entrega).</summary>
    ClientWalkthrough = 2,
}

public enum BuildInspectionStatus
{
    Planned           = 1,
    InProgress        = 2,
    Passed            = 3,
    PassedWithRemarks = 4,
    Failed            = 5,
    Cancelled         = 6,
}

public enum BuildInspectionItemResult
{
    Pending       = 0,
    Ok            = 1,
    NotOk         = 2,
    NotApplicable = 3,
}

/// <summary>
/// An inspection of a project (optionally of one stage). Items are a snapshot: copied from the template at creation,
/// so editing the template never changes past inspections. Planned → InProgress → Passed | PassedWithRemarks | Failed
/// (computed by <see cref="ComputeOutcome"/>) or Cancelled. A completed inspection is final.
/// </summary>
public class BuildInspection : TenantEntity
{
    private BuildInspection() { }
    private BuildInspection(Guid tenantId) : base(tenantId) { }

    public Guid                  ProjectId       { get; private set; }
    public Guid?                 StageId         { get; private set; }
    public Guid?                 TemplateId      { get; private set; }
    public BuildInspectionKind   Kind            { get; private set; }
    public string                Title           { get; private set; } = string.Empty;
    public DateOnly?             ScheduledFor    { get; private set; }
    public BuildInspectionStatus Status          { get; private set; }
    public string?               InspectorName   { get; private set; }
    public DateTime?             StartedAt       { get; private set; }
    public DateTime?             CompletedAt     { get; private set; }
    public string?               CompletedByName { get; private set; }
    /// <summary>Internal notes — never shown in the client portal.</summary>
    public string?               Notes           { get; private set; }
    public Guid                  CreatedBy       { get; private set; }

    public bool IsCompleted => Status is BuildInspectionStatus.Passed or BuildInspectionStatus.PassedWithRemarks
        or BuildInspectionStatus.Failed;
    public bool IsOpen => Status is BuildInspectionStatus.Planned or BuildInspectionStatus.InProgress;

    public static BuildInspection Create(Guid tenantId, Guid projectId, BuildInspectionKind kind, string title, Guid? stageId,
        Guid? templateId, DateOnly? scheduledFor, string? inspectorName, string? notes, Guid createdBy)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (!Enum.IsDefined(kind)) throw new DomainException("Tipo de vistoria inválido.");
        var i = new BuildInspection(tenantId)
        {
            ProjectId  = projectId,
            Kind       = kind,
            TemplateId = templateId,
            Status     = BuildInspectionStatus.Planned,
            CreatedBy  = createdBy,
        };
        i.Apply(title, stageId, scheduledFor, inspectorName, notes);
        return i;
    }

    public void Update(string title, Guid? stageId, DateOnly? scheduledFor, string? inspectorName, string? notes)
    {
        EnsureOpen();
        Apply(title, stageId, scheduledFor, inspectorName, notes);
        SetUpdatedAt();
    }

    private void Apply(string title, Guid? stageId, DateOnly? scheduledFor, string? inspectorName, string? notes)
    {
        Title         = BuildMoney.Required(title, 200, "O título da vistoria");
        StageId       = stageId;
        ScheduledFor  = scheduledFor;
        InspectorName = BuildMoney.Clean(inspectorName, 200, "O nome do vistoriador");
        Notes         = BuildMoney.Clean(notes, 4000, "As observações");
    }

    public void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException("Esta vistoria já foi concluída ou cancelada.", "inspection_closed");
    }

    public void Start(DateTime nowUtc)
    {
        EnsureOpen();
        if (Status == BuildInspectionStatus.InProgress) return;
        Status    = BuildInspectionStatus.InProgress;
        StartedAt = nowUtc;
        SetUpdatedAt();
    }

    /// <summary>
    /// Completes with the outcome computed from the items (see <see cref="ComputeOutcome"/>). Completing again → 409.
    /// </summary>
    public BuildInspectionStatus Complete(IReadOnlyCollection<BuildInspectionItem> items, string? notes, string completedBy,
        DateTime nowUtc)
    {
        if (IsCompleted) throw new ConflictException("Esta vistoria já foi concluída.");
        EnsureOpen();
        var outcome = ComputeOutcome(items);
        Status          = outcome;
        StartedAt     ??= nowUtc;
        CompletedAt     = nowUtc;
        CompletedByName = BuildMoney.Required(completedBy, 200, "Quem concluiu");
        if (!string.IsNullOrWhiteSpace(notes)) Notes = BuildMoney.Clean(notes, 4000, "As observações");
        SetUpdatedAt();
        return outcome;
    }

    public void Cancel()
    {
        if (Status == BuildInspectionStatus.Cancelled) return;
        if (IsCompleted) throw new ConflictException("Esta vistoria já foi concluída.");
        Status = BuildInspectionStatus.Cancelled;
        SetUpdatedAt();
    }

    /// <summary>
    /// The outcome rules: a required item still Pending blocks completion (422); an item that requires a photo and was
    /// judged Ok/NotOk needs at least one photo (422); then any required NotOk → Failed, any NotOk → PassedWithRemarks,
    /// otherwise (all Ok / not applicable / optional pending) → Passed. An inspection without items cannot be completed.
    /// </summary>
    public static BuildInspectionStatus ComputeOutcome(IReadOnlyCollection<BuildInspectionItem> items)
    {
        if (items.Count == 0)
            throw new DomainException("Inclua ao menos um item na vistoria.", "inspection_empty");
        var pending = items.Where(i => i.Required && i.Result == BuildInspectionItemResult.Pending).ToList();
        if (pending.Count > 0)
            throw new DomainException("Há itens obrigatórios sem resultado.", "inspection_required_items_pending",
                pending.Select(i => i.Text).ToList());
        var missingPhoto = items.Where(i => i.RequiresPhoto
                                            && (i.Result is BuildInspectionItemResult.Ok or BuildInspectionItemResult.NotOk)
                                            && i.PhotoKeys.Count == 0).ToList();
        if (missingPhoto.Count > 0)
            throw new DomainException("Há itens que exigem foto sem nenhuma foto.", "inspection_photo_required",
                missingPhoto.Select(i => i.Text).ToList());
        if (items.Any(i => i.Required && i.Result == BuildInspectionItemResult.NotOk)) return BuildInspectionStatus.Failed;
        if (items.Any(i => i.Result == BuildInspectionItemResult.NotOk)) return BuildInspectionStatus.PassedWithRemarks;
        return BuildInspectionStatus.Passed;
    }
}

/// <summary>One line of an inspection (snapshot of the template item) with its result, comment and private photos.</summary>
public class BuildInspectionItem : TenantEntity
{
    public const int MaxPhotos = 10;

    private BuildInspectionItem() { }
    private BuildInspectionItem(Guid tenantId) : base(tenantId) { }

    public Guid                      InspectionId  { get; private set; }
    public int                       Order         { get; private set; }
    public string                    Text          { get; private set; } = string.Empty;
    public bool                      Required      { get; private set; }
    public bool                      RequiresPhoto { get; private set; }
    public BuildInspectionItemResult Result        { get; private set; }
    public string?                   Comment       { get; private set; }
    /// <summary>Severity of the non-conformity opened when this item is NotOk (null → Required: High, else Medium).</summary>
    public BuildNonConformitySeverity? Severity    { get; private set; }
    public string                    PhotoKeysJson { get; private set; } = "[]";

    public IReadOnlyList<string> PhotoKeys => BuildPhotoKeys.Read(PhotoKeysJson);

    public static BuildInspectionItem Create(Guid tenantId, Guid inspectionId, int order, string text, bool required,
        bool requiresPhoto)
    {
        if (inspectionId == Guid.Empty) throw new DomainException("Vistoria inválida.");
        return new BuildInspectionItem(tenantId)
        {
            InspectionId  = inspectionId,
            Order         = Math.Max(0, order),
            Text          = BuildMoney.Required(text, 500, "O texto do item"),
            Required      = required,
            RequiresPhoto = requiresPhoto,
            Result        = BuildInspectionItemResult.Pending,
        };
    }

    /// <summary>Records the result (the caller checks that the inspection is still open).</summary>
    public void Record(BuildInspectionItemResult result, string? comment, IEnumerable<string>? photoKeys,
        BuildNonConformitySeverity? severity)
    {
        if (!Enum.IsDefined(result)) throw new DomainException("Resultado do item inválido.");
        if (severity is { } s && !Enum.IsDefined(s)) throw new DomainException("Gravidade inválida.");
        var keys = BuildPhotoKeys.Write(photoKeys);
        if (BuildPhotoKeys.Read(keys).Count > MaxPhotos)
            throw new DomainException($"Cada item aceita até {MaxPhotos} fotos.", "too_many_photos");
        Result        = result;
        Comment       = BuildMoney.Clean(comment, 2000, "O comentário");
        PhotoKeysJson = keys;
        Severity      = severity;
        SetUpdatedAt();
    }

    /// <summary>Severity of the NC this item opens when NotOk.</summary>
    public BuildNonConformitySeverity NonConformitySeverity
        => Severity ?? (Required ? BuildNonConformitySeverity.High : BuildNonConformitySeverity.Medium);
}

public enum BuildNonConformitySeverity
{
    Low      = 1,
    Medium   = 2,
    High     = 3,
    Critical = 4,
}

public enum BuildNonConformityStatus
{
    Open        = 1,
    InTreatment = 2,
    Resolved    = 3,
    Verified    = 4,
    Cancelled   = 5,
}

/// <summary>
/// A non-conformity (NC) of a project: opened by hand or automatically by an inspection item judged NotOk.
/// Open → InTreatment → Resolved → Verified (or Cancelled while open; a failed verification reopens a resolved NC).
/// Verification by the same person who resolved is allowed but recorded. An open Critical NC blocks marking its stage
/// 100% (<see cref="EnsureStageCanComplete"/>) and blocks the handover. A cost impact is informative only: nothing
/// financial is created automatically.
/// </summary>
public class BuildNonConformity : TenantEntity
{
    public const int MaxPhotos = 10;

    private BuildNonConformity() { }
    private BuildNonConformity(Guid tenantId) : base(tenantId) { }

    public Guid                       ProjectId        { get; private set; }
    public Guid?                      StageId          { get; private set; }
    public Guid?                      InspectionId     { get; private set; }
    public Guid?                      InspectionItemId { get; private set; }
    public int                        Number           { get; private set; }
    public string                     Title            { get; private set; } = string.Empty;
    public string?                    Description      { get; private set; }
    public BuildNonConformitySeverity Severity         { get; private set; }
    public BuildNonConformityStatus   Status           { get; private set; }
    public string?                    ResponsibleName  { get; private set; }
    public DateOnly?                  DueDate          { get; private set; }
    public string?                    RootCause        { get; private set; }
    public string?                    CorrectiveAction { get; private set; }
    public string                     PhotoKeysJson    { get; private set; } = "[]";
    public decimal?                   CostImpact       { get; private set; }
    public DateTime?                  ResolvedAt       { get; private set; }
    public Guid?                      ResolvedBy       { get; private set; }
    public string?                    ResolvedByName   { get; private set; }
    public DateTime?                  VerifiedAt       { get; private set; }
    public Guid?                      VerifiedBy       { get; private set; }
    public string?                    VerifiedByName   { get; private set; }
    public string?                    CancelReason     { get; private set; }
    public Guid                       CreatedBy        { get; private set; }
    public string                     CreatedByName    { get; private set; } = string.Empty;

    public IReadOnlyList<string> PhotoKeys => BuildPhotoKeys.Read(PhotoKeysJson);
    public bool IsOpen => Status is BuildNonConformityStatus.Open or BuildNonConformityStatus.InTreatment;
    public bool BlocksCompletion => IsOpen && Severity == BuildNonConformitySeverity.Critical;
    /// <summary>Verified by the same user who resolved it (allowed, recorded).</summary>
    public bool VerifiedBySameUser => VerifiedBy is not null && VerifiedBy == ResolvedBy;
    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is { } due && due < today;

    public static BuildNonConformity Create(Guid tenantId, Guid projectId, int number, string title, string? description,
        BuildNonConformitySeverity severity, Guid? stageId, string? responsibleName, DateOnly? dueDate, decimal? costImpact,
        IEnumerable<string>? photoKeys, Guid createdBy, string? createdByName, Guid? inspectionId = null, Guid? inspectionItemId = null)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (number < 1) throw new DomainException("Número da não conformidade inválido.");
        var nc = new BuildNonConformity(tenantId)
        {
            ProjectId        = projectId,
            Number           = number,
            Status           = BuildNonConformityStatus.Open,
            InspectionId     = inspectionId,
            InspectionItemId = inspectionItemId,
            CreatedBy        = createdBy,
            CreatedByName    = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
        nc.Apply(title, description, severity, stageId, responsibleName, dueDate, costImpact, photoKeys);
        return nc;
    }

    public void Update(string title, string? description, BuildNonConformitySeverity severity, Guid? stageId,
        string? responsibleName, DateOnly? dueDate, decimal? costImpact, IEnumerable<string>? photoKeys, string? rootCause,
        string? correctiveAction)
    {
        if (!IsOpen)
            throw new DomainException("Só não conformidades abertas ou em tratamento podem ser alteradas.", "nc_not_open");
        Apply(title, description, severity, stageId, responsibleName, dueDate, costImpact, photoKeys);
        RootCause        = BuildMoney.Clean(rootCause, 2000, "A causa raiz");
        CorrectiveAction = BuildMoney.Clean(correctiveAction, 2000, "A ação corretiva");
        SetUpdatedAt();
    }

    private void Apply(string title, string? description, BuildNonConformitySeverity severity, Guid? stageId,
        string? responsibleName, DateOnly? dueDate, decimal? costImpact, IEnumerable<string>? photoKeys)
    {
        if (!Enum.IsDefined(severity)) throw new DomainException("Gravidade inválida.");
        if (costImpact is { } c && (c < 0m || c > 1_000_000_000m)) throw new DomainException("Impacto de custo fora do limite.");
        var keys = BuildPhotoKeys.Write(photoKeys);
        if (BuildPhotoKeys.Read(keys).Count > MaxPhotos)
            throw new DomainException($"Uma não conformidade aceita até {MaxPhotos} fotos.", "too_many_photos");
        Title           = BuildMoney.Required(title, 200, "O título da não conformidade");
        Description     = BuildMoney.Clean(description, 4000, "A descrição");
        Severity        = severity;
        StageId         = stageId;
        ResponsibleName = BuildMoney.Clean(responsibleName, 200, "O responsável");
        DueDate         = dueDate;
        CostImpact      = costImpact is { } v ? BuildMoney.Round(v) : null;
        PhotoKeysJson   = keys;
    }

    /// <summary>Open → InTreatment.</summary>
    public void StartTreatment()
    {
        if (Status == BuildNonConformityStatus.InTreatment) throw new ConflictException("Esta não conformidade já está em tratamento.");
        if (Status != BuildNonConformityStatus.Open) throw InvalidTransition("iniciar o tratamento");
        Status = BuildNonConformityStatus.InTreatment;
        SetUpdatedAt();
    }

    /// <summary>Open/InTreatment → Resolved. A corrective action is required (given now or before).</summary>
    public void Resolve(string? correctiveAction, string? rootCause, Guid userId, string userName, DateTime nowUtc)
    {
        if (Status == BuildNonConformityStatus.Resolved) throw new ConflictException("Esta não conformidade já foi resolvida.");
        if (!IsOpen) throw InvalidTransition("resolver");
        var action = BuildMoney.Clean(correctiveAction, 2000, "A ação corretiva") ?? CorrectiveAction;
        if (action is null)
            throw new DomainException("Informe a ação corretiva para resolver a não conformidade.", "nc_corrective_action_required");
        CorrectiveAction = action;
        RootCause        = BuildMoney.Clean(rootCause, 2000, "A causa raiz") ?? RootCause;
        Status           = BuildNonConformityStatus.Resolved;
        ResolvedAt       = nowUtc;
        ResolvedBy       = userId;
        ResolvedByName   = BuildMoney.Required(userName, 200, "Quem resolveu");
        SetUpdatedAt();
    }

    /// <summary>Resolved → Verified (only after resolved; the verifier may be the resolver — recorded).</summary>
    public void Verify(Guid userId, string userName, DateTime nowUtc)
    {
        if (Status == BuildNonConformityStatus.Verified) throw new ConflictException("Esta não conformidade já foi verificada.");
        if (Status != BuildNonConformityStatus.Resolved)
            throw new DomainException("Só uma não conformidade resolvida pode ser verificada.", "nc_not_resolved");
        Status         = BuildNonConformityStatus.Verified;
        VerifiedAt     = nowUtc;
        VerifiedBy     = userId;
        VerifiedByName = BuildMoney.Required(userName, 200, "Quem verificou");
        SetUpdatedAt();
    }

    /// <summary>Resolved → InTreatment: the verification found the problem still there.</summary>
    public void Reopen()
    {
        if (Status != BuildNonConformityStatus.Resolved) throw InvalidTransition("reabrir");
        Status         = BuildNonConformityStatus.InTreatment;
        ResolvedAt     = null;
        ResolvedBy     = null;
        ResolvedByName = null;
        SetUpdatedAt();
    }

    public void Cancel(string reason)
    {
        if (Status == BuildNonConformityStatus.Cancelled) throw new ConflictException("Esta não conformidade já foi cancelada.");
        if (!IsOpen) throw InvalidTransition("cancelar");
        CancelReason = BuildMoney.Required(reason, 1000, "O motivo do cancelamento");
        Status       = BuildNonConformityStatus.Cancelled;
        SetUpdatedAt();
    }

    private DomainException InvalidTransition(string action)
        => new($"Não é possível {action} uma não conformidade com status {Status}.", "nc_invalid_transition");

    /// <summary>
    /// The domain rule shared by every path that moves a stage to 100% (manual progress, measurement approval, status
    /// Completed): a stage with an open (Open/InTreatment) Critical NC cannot be completed.
    /// </summary>
    public static void EnsureStageCanComplete(bool hasOpenCriticalNonConformity)
    {
        if (hasOpenCriticalNonConformity)
            throw new DomainException("Etapa com não conformidade crítica aberta.", StageBlockedCode);
    }

    public const string StageBlockedCode = "stage_has_critical_nonconformity";
}
