using System.Text.Json;

namespace Nexo.Application.Modules.Build;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <param name="Text">The day's free report (what happened on site), ≤ 8000 characters.</param>
/// <param name="PhotoIds">Optional daily-log photo ids of this project (vision: needs BuildVision, ≤ 5).</param>
public record BuildDailyLogCopilotRequest(DateOnly? Date, string? Text, IReadOnlyList<Guid>? PhotoIds = null);

/// <param name="LookbackDays">Diary entries of the last N days (default 14, 1–60).</param>
public record BuildProgressInterpreterRequest(int? LookbackDays = null, IReadOnlyList<Guid>? PhotoIds = null);

public record BuildModelMapperRequest(Guid? ModelVersionId = null);

/// <param name="From">Period start (default 7 days ago).</param>
/// <param name="To">Period end (default today).</param>
public record BuildClientUpdateAiRequest(DateOnly? From = null, DateOnly? To = null);

/// <param name="Kind">delay | cost</param>
public record BuildRiskExplainRequest(string? Kind);

public record AcceptBuildAiSuggestionRequest(IReadOnlyList<string>? ItemIds = null, string? Note = null);

public record RejectBuildAiSuggestionRequest(string? Note = null);

// ── Suggestion ───────────────────────────────────────────────────────────────

/// <summary>
/// A stored AI suggestion with its provenance. <c>Output</c> is the validated answer (never the provider's raw body);
/// <c>InputRefs</c> lists the ids of what was read; <c>Applied</c> what the acceptance created or changed.
/// </summary>
public record BuildAiSuggestionDto(
    Guid Id,
    Guid ProjectId,
    string Capability,
    string Status,
    string Source,
    Guid? SubjectId,
    decimal Confidence,
    JsonElement Output,
    JsonElement InputRefs,
    JsonElement? Applied,
    string? Provider,
    string? Model,
    string? PromptType,
    string? PromptVersion,
    Guid? PromptVersionId,
    string? PromptHash,
    int InputTokens,
    int OutputTokens,
    long CostMicros,
    int LatencyMs,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? ExpiresAt);

// ── Risk (deterministic) ─────────────────────────────────────────────────────

/// <summary>One deterministic risk item. <c>Key</c> is stable (e.g. <c>stage_delayed:{stageId}</c>) — the LLM may only refer to it.</summary>
public record BuildRiskItemDto(
    string Key,
    string Kind,
    string Severity,
    string Title,
    string Evidence,
    IReadOnlyDictionary<string, decimal?> Metrics,
    IReadOnlyList<string> Refs);

public record BuildRiskAnalysisDto(
    Guid ProjectId,
    string Kind,
    DateOnly AsOf,
    string Level,
    IReadOnlyDictionary<string, decimal?> Metrics,
    IReadOnlyList<BuildRiskItemDto> Items,
    string Methodology);

// ── Documents ────────────────────────────────────────────────────────────────

public record UpdateBuildDocumentRequest(string? Category, string? Title, DateOnly? ValidUntil);

public record SetBuildDocumentVisibilityRequest(bool ClientVisible);

/// <summary>A project document (internal). <c>Extracted</c> = what an accepted AI analysis confirmed (internal only).</summary>
public record BuildDocumentDto(
    Guid Id,
    Guid ProjectId,
    string Category,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    bool ClientVisible,
    DateOnly? ValidUntil,
    JsonElement? Extracted,
    Guid? SuggestionId,
    string UploadedByName,
    string? Url,
    DateTimeOffset? UrlExpiresAt,
    DateTimeOffset CreatedAt);

// ── Public portal (contract v1.4) ────────────────────────────────────────────

/// <summary>A client-visible document: title, category, signed URL (~15 min). Never internal fields nor what the AI extracted.</summary>
public record PublicBuildDocumentDto(
    string Title,
    string Category,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateOnly? ValidUntil,
    string? Url,
    DateTimeOffset? UrlExpiresAt,
    DateTimeOffset UploadedAt);

public record PublicBuildDocumentsDto(IReadOnlyList<PublicBuildDocumentDto> Items);
