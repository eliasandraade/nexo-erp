using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>What an AI suggestion is about (Wave 5).</summary>
public enum BuildAiCapability
{
    DailyLogCopilot      = 1,
    ProgressInterpreter  = 2,
    ModelMapper          = 3,
    ClientUpdate         = 4,
    DelayRisk            = 5,
    CostRisk             = 6,
    DocumentIntelligence = 7,
    WeeklyDigest         = 8,
}

public enum BuildAiSuggestionStatus
{
    Pending           = 1,
    Accepted          = 2,
    PartiallyAccepted = 3,
    Rejected          = 4,
    Expired           = 5,
    Failed            = 6,
}

/// <summary>Who produced the suggestion: a language model, or the deterministic rules (no generated prose).</summary>
public enum BuildAiSource
{
    Llm   = 1,
    Rules = 2,
}

/// <summary>
/// A suggestion of the Build AI (or of its deterministic fallback). Never changes anything by itself: every state change
/// it proposes needs a person to accept it (whole or a selection of items). Keeps the provenance — the ids of what was
/// read (<see cref="InputRefsJson"/>), the validated output, provider, model, prompt version and hash, tokens and cost —
/// and what the acceptance created or changed (<see cref="AppliedRefsJson"/>). Pending suggestions expire lazily.
/// </summary>
public class BuildAiSuggestion : TenantEntity
{
    public const int MaxJson = 200_000;

    private BuildAiSuggestion() { }
    private BuildAiSuggestion(Guid tenantId) : base(tenantId) { }

    public Guid                    ProjectId       { get; private set; }
    public BuildAiCapability       Capability      { get; private set; }
    public BuildAiSuggestionStatus Status          { get; private set; }
    public BuildAiSource           Source          { get; private set; }
    /// <summary>The record the suggestion is about (a document, a model version, a daily log date…), when there is one.</summary>
    public Guid?                   SubjectId       { get; private set; }
    public string                  InputRefsJson   { get; private set; } = "{}";
    public string                  OutputJson      { get; private set; } = "{}";
    public decimal                 Confidence      { get; private set; }
    public string?                 Provider        { get; private set; }
    public string?                 Model           { get; private set; }
    public string?                 PromptType      { get; private set; }
    public string?                 PromptVersion   { get; private set; }
    public Guid?                   PromptVersionId { get; private set; }
    public string?                 PromptHash      { get; private set; }
    public int                     InputTokens     { get; private set; }
    public int                     OutputTokens    { get; private set; }
    public long                    CostMicros      { get; private set; }
    public int                     LatencyMs       { get; private set; }
    public Guid                    CreatedBy       { get; private set; }
    public string                  CreatedByName   { get; private set; } = string.Empty;
    public Guid?                   DecidedBy       { get; private set; }
    public string?                 DecidedByName   { get; private set; }
    public DateTime?               DecidedAt       { get; private set; }
    public string?                 DecisionNote    { get; private set; }
    public string?                 AppliedRefsJson { get; private set; }
    public DateTime?               ExpiresAt       { get; private set; }

    public bool IsPending => Status == BuildAiSuggestionStatus.Pending;

    public sealed record Provenance(string? Provider, string? Model, string? PromptType, string? PromptVersion, Guid? PromptVersionId,
        string? PromptHash, int InputTokens, int OutputTokens, long CostMicros, int LatencyMs)
    {
        public static Provenance Rules(string? promptType) => new("Rules", null, promptType, null, null, null, 0, 0, 0, 0);
    }

    public static BuildAiSuggestion Create(Guid tenantId, Guid projectId, BuildAiCapability capability, BuildAiSource source,
        Guid? subjectId, string inputRefsJson, string outputJson, decimal confidence, Provenance provenance, Guid createdBy,
        string? createdByName, DateTime? expiresAt)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (!Enum.IsDefined(capability) || !Enum.IsDefined(source)) throw new DomainException("Sugestão inválida.");
        if (string.IsNullOrWhiteSpace(outputJson) || outputJson.Length > MaxJson) throw new DomainException("Resposta da IA grande demais.");
        if (inputRefsJson.Length > MaxJson) throw new DomainException("Referências grandes demais.");
        return new BuildAiSuggestion(tenantId)
        {
            ProjectId       = projectId,
            Capability      = capability,
            Status          = BuildAiSuggestionStatus.Pending,
            Source          = source,
            SubjectId       = subjectId,
            InputRefsJson   = string.IsNullOrWhiteSpace(inputRefsJson) ? "{}" : inputRefsJson,
            OutputJson      = outputJson,
            Confidence      = Math.Round(Math.Clamp(confidence, 0m, 1m), 3),
            Provider        = Short(provenance.Provider, 50),
            Model           = Short(provenance.Model, 100),
            PromptType      = Short(provenance.PromptType, 100),
            PromptVersion   = Short(provenance.PromptVersion, 20),
            PromptVersionId = provenance.PromptVersionId,
            PromptHash      = Short(provenance.PromptHash, 64),
            InputTokens     = Math.Max(0, provenance.InputTokens),
            OutputTokens    = Math.Max(0, provenance.OutputTokens),
            CostMicros      = Math.Max(0, provenance.CostMicros),
            LatencyMs       = Math.Max(0, provenance.LatencyMs),
            CreatedBy       = createdBy,
            CreatedByName   = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : Short(createdByName, 200)!,
            ExpiresAt       = expiresAt,
        };
    }

    /// <summary>Lazy expiry: a pending suggestion past <see cref="ExpiresAt"/> becomes Expired (on read). Returns whether it changed.</summary>
    public bool ExpireIfDue(DateTime nowUtc)
    {
        if (!IsPending || ExpiresAt is not { } at || at > nowUtc) return false;
        Status = BuildAiSuggestionStatus.Expired;
        SetUpdatedAt();
        return true;
    }

    /// <summary>Before applying anything: only a pending, unexpired suggestion can be accepted or rejected (409 otherwise).</summary>
    public void EnsureDecidable(DateTime nowUtc)
    {
        ExpireIfDue(nowUtc);
        if (Status == BuildAiSuggestionStatus.Expired)
            throw new ConflictException("Esta sugestão expirou. Gere uma nova.");
        if (!IsPending)
            throw new ConflictException("Esta sugestão já foi decidida.");
    }

    public void Accept(bool partial, Guid by, string? byName, string? note, string appliedRefsJson, DateTime nowUtc)
    {
        EnsureDecidable(nowUtc);
        Status = partial ? BuildAiSuggestionStatus.PartiallyAccepted : BuildAiSuggestionStatus.Accepted;
        Decide(by, byName, note, nowUtc);
        AppliedRefsJson = appliedRefsJson;
    }

    public void Reject(Guid by, string? byName, string? note, DateTime nowUtc)
    {
        EnsureDecidable(nowUtc);
        Status = BuildAiSuggestionStatus.Rejected;
        Decide(by, byName, note, nowUtc);
    }

    private void Decide(Guid by, string? byName, string? note, DateTime nowUtc)
    {
        var clean = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (clean is { Length: > 1000 }) throw new DomainException("A observação pode ter até 1000 caracteres.");
        DecidedBy     = by;
        DecidedByName = string.IsNullOrWhiteSpace(byName) ? "usuário" : Short(byName, 200);
        DecidedAt     = nowUtc;
        DecisionNote  = clean;
        SetUpdatedAt();
    }

    private static string? Short(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }
}

public enum BuildDocumentCategory
{
    Contrato   = 1,
    Projeto    = 2,
    ArtRrt     = 3,
    Alvara     = 4,
    NotaFiscal = 5,
    Laudo      = 6,
    Manual     = 7,
    Garantia   = 8,
    Foto       = 9,
    Outro      = 10,
}

/// <summary>
/// A document of a project (contract, design, ART/RRT, permit, invoice, report, manual, warranty…), stored privately.
/// Hidden from the client portal until marked client-visible; what the AI extracted (<see cref="ExtractedJson"/>) is
/// internal and never reaches the portal.
/// </summary>
public class BuildDocument : TenantEntity
{
    public const long MaxBytes = 20L * 1024 * 1024;

    private BuildDocument() { }
    private BuildDocument(Guid tenantId) : base(tenantId) { }

    public Guid                  ProjectId     { get; private set; }
    public BuildDocumentCategory Category      { get; private set; }
    public string                Title         { get; private set; } = string.Empty;
    public string                FileKey       { get; private set; } = string.Empty;
    /// <summary>The client's file name, sanitized — display only, never part of the key.</summary>
    public string                FileName      { get; private set; } = string.Empty;
    public string                ContentType   { get; private set; } = string.Empty;
    public long                  SizeBytes     { get; private set; }
    public string                Sha256        { get; private set; } = string.Empty;
    public bool                  ClientVisible { get; private set; }
    public DateOnly?             ValidUntil    { get; private set; }
    public string?               ExtractedJson { get; private set; }
    public Guid?                 SuggestionId  { get; private set; }
    public Guid                  UploadedBy    { get; private set; }
    public string                UploadedByName { get; private set; } = string.Empty;

    public static BuildDocument Create(Guid tenantId, Guid projectId, Guid id, BuildDocumentCategory category, string title, string fileKey,
        string fileName, string contentType, long sizeBytes, string sha256, Guid uploadedBy, string? uploadedByName)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (string.IsNullOrWhiteSpace(fileKey)) throw new DomainException("Arquivo inválido.");
        if (sizeBytes <= 0 || sizeBytes > MaxBytes) throw new DomainException("Tamanho de arquivo inválido.");
        var d = new BuildDocument(tenantId)
        {
            ProjectId      = projectId,
            FileKey        = fileKey,
            FileName       = SanitizeFileName(fileName),
            ContentType    = contentType,
            SizeBytes      = sizeBytes,
            Sha256         = sha256,
            UploadedBy     = uploadedBy,
            UploadedByName = string.IsNullOrWhiteSpace(uploadedByName) ? "usuário" : uploadedByName.Trim()[..Math.Min(200, uploadedByName.Trim().Length)],
        };
        d.Id = id;
        d.Describe(category, title, null);
        return d;
    }

    public void Describe(BuildDocumentCategory category, string title, DateOnly? validUntil)
    {
        if (!Enum.IsDefined(category)) throw new DomainException("Categoria de documento inválida.", "invalid_value");
        var t = title?.Trim() ?? "";
        if (t.Length == 0) throw new DomainException("Informe o título do documento.");
        if (t.Length > 200) throw new DomainException("O título pode ter até 200 caracteres.");
        Category   = category;
        Title      = t;
        ValidUntil = validUntil;
        SetUpdatedAt();
    }

    public void SetClientVisible(bool visible)
    {
        ClientVisible = visible;
        SetUpdatedAt();
    }

    /// <summary>What an accepted AI extraction confirms: category, validity and the extracted data (internal).</summary>
    public void ApplyExtraction(BuildDocumentCategory? category, DateOnly? validUntil, string extractedJson, Guid suggestionId)
    {
        if (category is { } c && Enum.IsDefined(c)) Category = c;
        if (validUntil is { } v) ValidUntil = v;
        ExtractedJson = extractedJson.Length > BuildAiSuggestion.MaxJson ? null : extractedJson;
        SuggestionId  = suggestionId;
        SetUpdatedAt();
    }

    /// <summary>Display name only: no path, no control or reserved characters, at most 200 characters.</summary>
    public static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "documento" : name.Replace('\\', '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        n = new string(n.Where(ch => !char.IsControl(ch) && ch is not ('<' or '>' or ':' or '"' or '|' or '?' or '*')).ToArray()).Trim().Trim('.');
        if (n.Length == 0) n = "documento";
        return n.Length > 200 ? n[..200] : n;
    }
}
