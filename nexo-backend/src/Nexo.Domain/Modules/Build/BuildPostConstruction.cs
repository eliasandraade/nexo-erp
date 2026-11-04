using System.Text.Json;
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 3 — POST-CONSTRUCTION: handover (entrega), warranty terms, technical assistance.
// ═══════════════════════════════════════════════════════════════════════

public enum BuildHandoverStatus
{
    Scheduled      = 1,
    InspectionDone = 2,
    Delivered      = 3,
}

/// <summary>A document handed to the client (manual, as-built, warranty certificate…); the file is private, optional.</summary>
public sealed record BuildHandoverDocument(string Name, string? FileKey);

/// <summary>
/// The handover of a project (one per project): scheduled → walkthrough with the client done → delivered. Delivering
/// requires no open Critical NC (checked by the service). The client accepts (signer name typed in the portal, or recorded
/// internally) once the walkthrough is done or the property delivered; accepting is final (again → 409). No IP is stored.
/// </summary>
public class BuildHandover : TenantEntity
{
    public const int MaxDocuments = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private BuildHandover() { }
    private BuildHandover(Guid tenantId) : base(tenantId) { }

    public Guid                  ProjectId               { get; private set; }
    public BuildHandoverStatus   Status                  { get; private set; }
    public DateOnly?             ScheduledFor            { get; private set; }
    public Guid?                 WalkthroughInspectionId { get; private set; }
    public DateTime?             DeliveredAt             { get; private set; }
    public string?               DeliveredByName         { get; private set; }
    public bool                  KeysDelivered           { get; private set; }
    public string                DocumentsJson           { get; private set; } = "[]";
    /// <summary>Internal notes — never shown in the client portal.</summary>
    public string?               Notes                   { get; private set; }
    public DateTime?             AcceptedAt              { get; private set; }
    public string?               AcceptedByName          { get; private set; }
    public BuildDecisionChannel? AcceptanceChannel       { get; private set; }
    public string?               AcceptanceComment       { get; private set; }

    public IReadOnlyList<BuildHandoverDocument> Documents
        => string.IsNullOrWhiteSpace(DocumentsJson) ? [] : JsonSerializer.Deserialize<List<BuildHandoverDocument>>(DocumentsJson, Json) ?? [];
    public bool IsDelivered => Status == BuildHandoverStatus.Delivered;
    public bool IsAccepted => AcceptedAt is not null;
    public bool CanBeAccepted => !IsAccepted && Status is BuildHandoverStatus.InspectionDone or BuildHandoverStatus.Delivered;

    public static BuildHandover Create(Guid tenantId, Guid projectId, DateOnly? scheduledFor)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        return new BuildHandover(tenantId) { ProjectId = projectId, Status = BuildHandoverStatus.Scheduled, ScheduledFor = scheduledFor };
    }

    public void Update(DateOnly? scheduledFor, bool? keysDelivered, IReadOnlyList<BuildHandoverDocument>? documents, string? notes)
    {
        if (!IsDelivered) ScheduledFor = scheduledFor ?? ScheduledFor;
        if (keysDelivered is { } k) KeysDelivered = k;
        if (documents is not null) SetDocuments(documents);
        Notes = BuildMoney.Clean(notes, 4000, "As observações");
        SetUpdatedAt();
    }

    private void SetDocuments(IReadOnlyList<BuildHandoverDocument> documents)
    {
        if (documents.Count > MaxDocuments) throw new DomainException($"A entrega aceita até {MaxDocuments} documentos.");
        var clean = documents.Select(d => new BuildHandoverDocument(
            BuildMoney.Required(d.Name, 200, "O nome do documento"),
            BuildMoney.Clean(d.FileKey, 500, "O arquivo do documento"))).ToList();
        DocumentsJson = JsonSerializer.Serialize(clean, Json);
    }

    /// <summary>Links the client walkthrough (only before the delivery; a new one replaces a cancelled one).</summary>
    public void LinkWalkthrough(Guid inspectionId)
    {
        if (IsDelivered) throw new DomainException("O imóvel já foi entregue.", "handover_delivered");
        WalkthroughInspectionId = inspectionId;
        SetUpdatedAt();
    }

    /// <summary>The walkthrough was completed: Scheduled → InspectionDone (no-op once delivered).</summary>
    public void MarkInspectionDone()
    {
        if (Status != BuildHandoverStatus.Scheduled) return;
        Status = BuildHandoverStatus.InspectionDone;
        SetUpdatedAt();
    }

    /// <summary>Delivers the property (the caller already checked that no Critical NC is open). Twice → 409.</summary>
    public void Deliver(bool? keysDelivered, string deliveredBy, DateTime nowUtc)
    {
        if (IsDelivered) throw new ConflictException("Este imóvel já foi entregue.");
        Status          = BuildHandoverStatus.Delivered;
        DeliveredAt     = nowUtc;
        DeliveredByName = BuildMoney.Required(deliveredBy, 200, "Quem entregou");
        if (keysDelivered is { } k) KeysDelivered = k;
        SetUpdatedAt();
    }

    public void Accept(string signerName, string? comment, BuildDecisionChannel channel, DateTime nowUtc)
    {
        if (IsAccepted) throw new ConflictException("O termo de entrega já foi aceito.");
        if (!CanBeAccepted)
            throw new DomainException("O aceite só pode ser feito depois da vistoria de entrega.", "handover_not_ready");
        if (!Enum.IsDefined(channel)) throw new DomainException("Canal de aceite inválido.");
        AcceptedByName    = BuildMoney.Required(signerName, 200, "O nome de quem assina");
        AcceptanceComment = BuildMoney.Clean(comment, 2000, "O comentário");
        AcceptanceChannel = channel;
        AcceptedAt        = nowUtc;
        SetUpdatedAt();
    }
}

/// <summary>
/// A warranty term of a delivered project, by system (Estrutura, Impermeabilização…). Coverage runs from
/// <see cref="StartsAt"/> (default: the delivery date) for <see cref="Months"/> months; the last covered day is
/// <see cref="ExpiresAt"/> (inclusive). A term without a start (created before the delivery) is not active yet.
/// </summary>
public class BuildWarrantyTerm : TenantEntity
{
    private BuildWarrantyTerm() { }
    private BuildWarrantyTerm(Guid tenantId) : base(tenantId) { }

    public Guid      ProjectId   { get; private set; }
    public string    System      { get; private set; } = string.Empty;
    public int       Months      { get; private set; }
    public DateOnly? StartsAt    { get; private set; }
    public string?   Description { get; private set; }
    /// <summary>Created from the default reference set on delivery.</summary>
    public bool      IsDefault   { get; private set; }

    /// <summary>
    /// Last covered day (inclusive), or null while the term has no start. Código Civil art. 132 §3: a term in months
    /// ends on the day with the same number as the start, or on the following day when that month has no such day
    /// (31 Jan + 1 month → 1 Mar).
    /// </summary>
    public DateOnly? ExpiresAt => StartsAt is { } s ? EndOfTerm(s, Months) : null;

    private static DateOnly EndOfTerm(DateOnly start, int months)
    {
        var end = start.AddMonths(months);            // clamps to the month's last day when the day does not exist
        return end.Day == start.Day ? end : end.AddDays(1);
    }

    public static BuildWarrantyTerm Create(Guid tenantId, Guid projectId, string system, int months, DateOnly? startsAt,
        string? description, bool isDefault = false)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        var t = new BuildWarrantyTerm(tenantId) { ProjectId = projectId, IsDefault = isDefault };
        t.Apply(system, months, startsAt, description);
        return t;
    }

    public void Update(string system, int months, DateOnly? startsAt, string? description)
    {
        Apply(system, months, startsAt, description);
        SetUpdatedAt();
    }

    /// <summary>Fills the start with the delivery date when none was set.</summary>
    public void StartOn(DateOnly deliveredOn)
    {
        if (StartsAt is not null) return;
        StartsAt = deliveredOn;
        SetUpdatedAt();
    }

    private void Apply(string system, int months, DateOnly? startsAt, string? description)
    {
        if (months is < 1 or > 600) throw new DomainException("O prazo da garantia precisa ficar entre 1 e 600 meses.", "invalid_warranty_months");
        System      = BuildMoney.Required(system, 100, "O sistema");
        Months      = months;
        StartsAt    = startsAt;
        Description = BuildMoney.Clean(description, 2000, "A descrição");
    }

    /// <summary>Covered on <paramref name="date"/>: StartsAt ≤ date ≤ ExpiresAt.</summary>
    public bool Covers(DateOnly date) => StartsAt is { } s && date >= s && date <= ExpiresAt!.Value;

    public bool IsActive(DateOnly today) => Covers(today);

    public bool IsFor(string category)
        => string.Equals(System.Trim(), category?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reference warranty terms created on delivery when the project has none. A reasonable starting point, NOT legal
/// advice: Estrutura follows the 5-year period of the Código Civil (art. 618 — solidez e segurança); the others are
/// shorter references in the spirit of ABNT NBR 15575. The company edits them to match its contract.
/// </summary>
public static class BuildWarrantyDefaults
{
    public const string Other = "Outros";

    public static IReadOnlyList<(string System, int Months, string Description)> Terms { get; } =
    [
        ("Estrutura", 60, "Solidez e segurança da estrutura (referência: Código Civil, art. 618)."),
        ("Impermeabilização", 60, "Estanqueidade de lajes, áreas molhadas e coberturas."),
        ("Instalações elétricas", 36, "Fiação, quadros e pontos (exceto lâmpadas e equipamentos)."),
        ("Instalações hidráulicas", 36, "Tubulações, registros e ralos (exceto metais sanitários com garantia própria)."),
        ("Revestimentos", 24, "Pisos, azulejos e rejuntes — destacamento e fissuras."),
        ("Esquadrias", 24, "Portas, janelas e ferragens — funcionamento e vedação."),
        ("Pintura", 24, "Descascamento e manchas não causados por uso ou umidade externa."),
    ];
}

public enum BuildAssistanceStatus
{
    Open       = 1,
    Triaged    = 2,
    Scheduled  = 3,
    InProgress = 4,
    Resolved   = 5,
    Closed     = 6,
    Rejected   = 7,
}

/// <summary>Result of the warranty coverage evaluation of a category on a date.</summary>
public sealed record BuildWarrantyCoverage(bool? Covered, Guid? TermId)
{
    /// <summary>
    /// The term of the category (same system name, case-insensitive) decides: covered when the date is within its
    /// period. No term for the category (e.g. "Outros") → null (to be decided by the company).
    /// </summary>
    public static BuildWarrantyCoverage Evaluate(IEnumerable<BuildWarrantyTerm> terms, string category, DateOnly date)
    {
        var matching = terms.Where(t => t.IsFor(category)).ToList();
        if (matching.Count == 0) return new(null, null);
        var covering = matching.FirstOrDefault(t => t.Covers(date));
        return covering is not null ? new(true, covering.Id) : new(false, matching[0].Id);
    }
}

/// <summary>
/// A technical assistance request (assistência técnica) after the delivery — opened by the company or by the client in
/// the portal. Open → Triaged → Scheduled → InProgress → Resolved → Closed, or Rejected (with a reason). Triage records the
/// warranty coverage (evaluated on the opening date). The client may add up to <see cref="MaxPhotos"/> photos while it is
/// in progress and rate it once resolved. Triage notes are internal and never reach the portal.
/// </summary>
public class BuildAssistanceRequest : TenantEntity
{
    public const int MaxPhotos = 6;

    private BuildAssistanceRequest() { }
    private BuildAssistanceRequest(Guid tenantId) : base(tenantId) { }

    public Guid                  ProjectId       { get; private set; }
    public Guid                  PublicId        { get; private set; }
    public int                   Number          { get; private set; }
    public string                Category        { get; private set; } = string.Empty;
    public string                Title           { get; private set; } = string.Empty;
    public string                Description     { get; private set; } = string.Empty;
    public string?               Location        { get; private set; }
    public BuildDecisionChannel  OpenedVia       { get; private set; }
    public string                OpenedByName    { get; private set; } = string.Empty;
    public string?               ContactPhone    { get; private set; }
    public DateOnly              OpenedOn        { get; private set; }
    public BuildAssistanceStatus Status          { get; private set; }
    /// <summary>Internal — never shown in the portal.</summary>
    public string?               TriageNotes     { get; private set; }
    public bool?                 WarrantyCovered { get; private set; }
    public Guid?                 WarrantyTermId  { get; private set; }
    public DateTime?             TriagedAt       { get; private set; }
    public string?               TriagedByName   { get; private set; }
    public DateTime?             ScheduledFor    { get; private set; }
    public DateTime?             StartedAt       { get; private set; }
    public DateTime?             ResolvedAt      { get; private set; }
    /// <summary>What was done — visible to the client.</summary>
    public string?               ResolutionNotes { get; private set; }
    public DateTime?             ClosedAt        { get; private set; }
    public DateTime?             RejectedAt      { get; private set; }
    /// <summary>Why it was rejected — visible to the client.</summary>
    public string?               RejectionReason { get; private set; }
    public int?                  ClientRating    { get; private set; }
    public string?               ClientFeedback  { get; private set; }
    public DateTime?             FeedbackAt      { get; private set; }
    public string                PhotoKeysJson   { get; private set; } = "[]";

    public IReadOnlyList<string> PhotoKeys => BuildPhotoKeys.Read(PhotoKeysJson);
    public bool IsOpen => Status is not (BuildAssistanceStatus.Resolved or BuildAssistanceStatus.Closed or BuildAssistanceStatus.Rejected);
    public bool CanAddPhotos => IsOpen && PhotoKeys.Count < MaxPhotos;
    public bool CanGiveFeedback => Status == BuildAssistanceStatus.Resolved && ClientRating is null;

    public static BuildAssistanceRequest Open(Guid tenantId, Guid projectId, int number, string category, string title,
        string description, string? location, BuildDecisionChannel openedVia, string openedByName, string? contactPhone,
        DateOnly openedOn)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (number < 1) throw new DomainException("Número do chamado inválido.");
        if (!Enum.IsDefined(openedVia)) throw new DomainException("Canal de abertura inválido.");
        return new BuildAssistanceRequest(tenantId)
        {
            ProjectId    = projectId,
            PublicId     = Guid.NewGuid(),
            Number       = number,
            Category     = BuildMoney.Required(category, 100, "A categoria"),
            Title        = BuildMoney.Required(title, 200, "O título"),
            Description  = BuildMoney.Required(description, 4000, "A descrição"),
            Location     = BuildMoney.Clean(location, 200, "O local no imóvel"),
            OpenedVia    = openedVia,
            OpenedByName = BuildMoney.Required(openedByName, 200, "Quem abriu"),
            ContactPhone = BuildMoney.Clean(contactPhone, 30, "O telefone"),
            OpenedOn     = openedOn,
            Status       = BuildAssistanceStatus.Open,
        };
    }

    /// <summary>Open/Triaged → Triaged: records the coverage (evaluated or decided by the company) and internal notes.</summary>
    public void Triage(bool? covered, Guid? termId, string? notes, string userName, DateTime nowUtc)
    {
        if (Status is not (BuildAssistanceStatus.Open or BuildAssistanceStatus.Triaged)) throw InvalidTransition("triar");
        WarrantyCovered = covered;
        WarrantyTermId  = termId;
        TriageNotes     = BuildMoney.Clean(notes, 2000, "As notas da triagem");
        TriagedAt       = nowUtc;
        TriagedByName   = BuildMoney.Required(userName, 200, "Quem triou");
        Status          = BuildAssistanceStatus.Triaged;
        SetUpdatedAt();
    }

    public void Schedule(DateTime visitUtc)
    {
        if (Status is not (BuildAssistanceStatus.Triaged or BuildAssistanceStatus.Scheduled)) throw InvalidTransition("agendar");
        ScheduledFor = visitUtc;
        Status       = BuildAssistanceStatus.Scheduled;
        SetUpdatedAt();
    }

    public void Start(DateTime nowUtc)
    {
        if (Status == BuildAssistanceStatus.InProgress) throw new ConflictException("Este chamado já está em andamento.");
        if (Status is not (BuildAssistanceStatus.Triaged or BuildAssistanceStatus.Scheduled)) throw InvalidTransition("iniciar");
        StartedAt = nowUtc;
        Status    = BuildAssistanceStatus.InProgress;
        SetUpdatedAt();
    }

    public void Resolve(string resolutionNotes, DateTime nowUtc)
    {
        if (Status == BuildAssistanceStatus.Resolved) throw new ConflictException("Este chamado já foi resolvido.");
        if (Status is not (BuildAssistanceStatus.Triaged or BuildAssistanceStatus.Scheduled or BuildAssistanceStatus.InProgress))
            throw InvalidTransition("resolver");
        ResolutionNotes = BuildMoney.Required(resolutionNotes, 2000, "O que foi feito");
        ResolvedAt      = nowUtc;
        StartedAt     ??= nowUtc;
        Status          = BuildAssistanceStatus.Resolved;
        SetUpdatedAt();
    }

    public void Close(DateTime nowUtc)
    {
        if (Status == BuildAssistanceStatus.Closed) throw new ConflictException("Este chamado já foi encerrado.");
        if (Status != BuildAssistanceStatus.Resolved) throw InvalidTransition("encerrar");
        ClosedAt = nowUtc;
        Status   = BuildAssistanceStatus.Closed;
        SetUpdatedAt();
    }

    public void Reject(string reason, DateTime nowUtc)
    {
        if (Status == BuildAssistanceStatus.Rejected) throw new ConflictException("Este chamado já foi recusado.");
        if (Status is not (BuildAssistanceStatus.Open or BuildAssistanceStatus.Triaged or BuildAssistanceStatus.Scheduled))
            throw InvalidTransition("recusar");
        RejectionReason = BuildMoney.Required(reason, 1000, "O motivo da recusa");
        RejectedAt      = nowUtc;
        Status          = BuildAssistanceStatus.Rejected;
        SetUpdatedAt();
    }

    public void AddPhoto(string key)
    {
        if (!IsOpen) throw new DomainException("Este chamado não aceita mais fotos.", "assistance_closed");
        var keys = PhotoKeys.ToList();
        if (keys.Count >= MaxPhotos)
            throw new DomainException($"Cada chamado aceita até {MaxPhotos} fotos.", "assistance_photo_limit");
        keys.Add(key);
        PhotoKeysJson = BuildPhotoKeys.Write(keys);
        SetUpdatedAt();
    }

    /// <summary>The client's rating (1–5) once resolved; only once (again → 409).</summary>
    public void GiveFeedback(int rating, string? comment, DateTime nowUtc)
    {
        if (ClientRating is not null) throw new ConflictException("A avaliação deste chamado já foi enviada.");
        if (Status != BuildAssistanceStatus.Resolved)
            throw new DomainException("A avaliação só pode ser enviada depois que o chamado for resolvido.", "assistance_not_resolved");
        if (rating is < 1 or > 5) throw new DomainException("A nota precisa ficar entre 1 e 5.", "invalid_rating");
        ClientRating   = rating;
        ClientFeedback = BuildMoney.Clean(comment, 2000, "O comentário");
        FeedbackAt     = nowUtc;
        SetUpdatedAt();
    }

    private DomainException InvalidTransition(string action)
        => new($"Não é possível {action} um chamado com status {Status}.", "assistance_invalid_transition");
}
