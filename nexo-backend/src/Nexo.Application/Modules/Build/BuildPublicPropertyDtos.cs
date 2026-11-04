using System.Text.Json.Serialization;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// CLIENT PORTAL v1.2 — "Meu Imóvel" (handover, warranty, technical assistance). Public DTOs only: opaque ids, no
// internal notes (handover notes, inspection notes/comments, triage notes), no contact data of the request, no keys.
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Bootstrap section after the delivery (flag postConstruction).</summary>
public sealed record PublicBuildPropertyDto
{
    public DateOnly? HandoverDate { get; init; }
    public bool HandoverAccepted { get; init; }
    public IReadOnlyList<PublicBuildWarrantyTermDto> WarrantyTerms { get; init; } = [];
    public int OpenAssistanceCount { get; init; }
}

public sealed record PublicBuildWarrantyTermDto(
    string    System,
    int       Months,
    DateOnly  StartsAt,
    DateOnly  ExpiresAt,
    bool      IsActive,
    string?   Description);

public sealed record PublicBuildWarrantyDto(IReadOnlyList<PublicBuildWarrantyTermDto> Items, string Notice);

// ── Handover ──────────────────────────────────────────────────────────

public sealed record PublicBuildHandoverDocumentDto(string Name, string? Url);

public sealed record PublicBuildWalkthroughItemDto(string Text, string Result);

public sealed record PublicBuildWalkthroughDto
{
    public string Status { get; init; } = string.Empty;
    public DateOnly? ScheduledFor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CompletedAt { get; init; }
    public int TotalItems { get; init; }
    public int OkItems { get; init; }
    public int PendingItems { get; init; }
    public IReadOnlyList<PublicBuildWalkthroughItemDto> Items { get; init; } = [];
}

public sealed record PublicBuildHandoverDto
{
    public string Status { get; init; } = string.Empty;
    public DateOnly? ScheduledFor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DeliveredAt { get; init; }
    public bool KeysDelivered { get; init; }
    public IReadOnlyList<PublicBuildHandoverDocumentDto> Documents { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DocumentUrlsExpireAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildWalkthroughDto? Walkthrough { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? AcceptedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AcceptedByName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AcceptanceChannel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AcceptanceComment { get; init; }
    public bool CanAccept { get; init; }
}

public sealed record PublicBuildHandoverAcceptRequest(string? SignerName, string? Comment);

// ── Assistance ────────────────────────────────────────────────────────

public sealed record PublicBuildAssistanceEventDto(string Status, DateTimeOffset At);

public sealed record PublicBuildAssistanceDto
{
    public Guid Id { get; init; }
    public int Number { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Location { get; init; }
    public string OpenedVia { get; init; } = string.Empty;
    public DateTimeOffset OpenedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? WarrantyCovered { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? ScheduledFor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ResolutionNotes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RejectionReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Rating { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Feedback { get; init; }
    public IReadOnlyList<string> PhotoUrls { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? PhotoUrlsExpireAt { get; init; }
    public bool CanAddPhotos { get; init; }
    public bool CanGiveFeedback { get; init; }
    public IReadOnlyList<PublicBuildAssistanceEventDto> Timeline { get; init; } = [];
}

public sealed record PublicBuildAssistanceListDto(IReadOnlyList<PublicBuildAssistanceDto> Items, IReadOnlyList<string> Categories,
    bool CanOpen);

public sealed record PublicBuildAssistanceOpenRequest(string? Category, string? Title, string? Description, string? Location,
    string? ContactPhone, string? OpenedByName);

public sealed record PublicBuildAssistanceFeedbackRequest(int? Rating, string? Comment);

/// <summary>A portal request authorized to receive one photo (resolved before the file is read).</summary>
public sealed record PublicBuildAssistanceUploadTarget(Guid TenantId, Guid ProjectId, Guid RequestId, string CompanySlug,
    string ProjectSlug);
