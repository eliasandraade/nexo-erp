namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 3 — QUALITY AND POST-CONSTRUCTION (internal API). Instants are UTC (DateTimeOffset), days are DateOnly.
// ═══════════════════════════════════════════════════════════════════════

// ── Checklist templates ───────────────────────────────────────────────

public record BuildChecklistItemRequest(string Text, bool Required = true, bool RequiresPhoto = false);

public record SaveBuildChecklistTemplateRequest(
    string                                    Name,
    string?                                   Category      = null,
    string?                                   StageNameHint = null,
    IReadOnlyList<BuildChecklistItemRequest>? Items         = null);

public record BuildChecklistTemplateItemDto(Guid Id, int Order, string Text, bool Required, bool RequiresPhoto);

public record BuildChecklistTemplateDto(
    Guid                                        Id,
    string                                      Name,
    string?                                     Category,
    string?                                     StageNameHint,
    IReadOnlyList<BuildChecklistTemplateItemDto> Items,
    DateTimeOffset                              CreatedAt,
    DateTimeOffset                              UpdatedAt);

// ── Inspections ───────────────────────────────────────────────────────

/// <summary>Items are copied from the template (snapshot) and then the ad-hoc <c>Items</c> are appended.</summary>
public record CreateBuildInspectionRequest(
    string                                    Title,
    Guid?                                     StageId       = null,
    Guid?                                     TemplateId    = null,
    DateOnly?                                 ScheduledFor  = null,
    string?                                   InspectorName = null,
    string?                                   Notes         = null,
    IReadOnlyList<BuildChecklistItemRequest>? Items         = null);

public record UpdateBuildInspectionRequest(
    string    Title,
    Guid?     StageId       = null,
    DateOnly? ScheduledFor  = null,
    string?   InspectorName = null,
    string?   Notes         = null);

/// <summary>Result: Pending | Ok | NotOk | NotApplicable. Severity (NotOk only): Low | Medium | High | Critical.</summary>
public record RecordBuildInspectionItemRequest(
    string                 Result,
    string?                Comment   = null,
    IReadOnlyList<string>? PhotoKeys = null,
    string?                Severity  = null);

public record CompleteBuildInspectionRequest(string? Notes = null);

public record BuildInspectionItemDto(
    Guid                  Id,
    int                   Order,
    string                Text,
    bool                  Required,
    bool                  RequiresPhoto,
    string                Result,
    string?               Comment,
    string?               Severity,
    IReadOnlyList<string> PhotoKeys,
    IReadOnlyList<string> PhotoUrls,
    Guid?                 NonConformityId);

public record BuildInspectionDto(
    Guid                                  Id,
    Guid                                  ProjectId,
    string                                Kind,
    Guid?                                 StageId,
    string?                               StageName,
    Guid?                                 TemplateId,
    string                                Title,
    DateOnly?                             ScheduledFor,
    string                                Status,
    string?                               InspectorName,
    DateTimeOffset?                       StartedAt,
    DateTimeOffset?                       CompletedAt,
    string?                               CompletedByName,
    string?                               Notes,
    int                                   OkItems,
    int                                   NotOkItems,
    int                                   PendingItems,
    IReadOnlyList<BuildInspectionItemDto> Items,
    DateTimeOffset                        CreatedAt,
    DateTimeOffset                        UpdatedAt);

// ── Non-conformities ──────────────────────────────────────────────────

public record SaveBuildNonConformityRequest(
    string                 Title,
    string?                Description      = null,
    string?                Severity         = null,
    Guid?                  StageId          = null,
    string?                ResponsibleName  = null,
    DateOnly?              DueDate          = null,
    decimal?               CostImpact       = null,
    IReadOnlyList<string>? PhotoKeys        = null,
    string?                RootCause        = null,
    string?                CorrectiveAction = null);

public record ResolveBuildNonConformityRequest(string? CorrectiveAction = null, string? RootCause = null);

public record CancelBuildNonConformityRequest(string Reason);

public record BuildNonConformityDto(
    Guid                  Id,
    Guid                  ProjectId,
    int                   Number,
    Guid?                 StageId,
    string?               StageName,
    Guid?                 InspectionId,
    Guid?                 InspectionItemId,
    string                Title,
    string?               Description,
    string                Severity,
    string                Status,
    string?               ResponsibleName,
    DateOnly?             DueDate,
    bool                  IsOverdue,
    string?               RootCause,
    string?               CorrectiveAction,
    IReadOnlyList<string> PhotoKeys,
    IReadOnlyList<string> PhotoUrls,
    decimal?              CostImpact,
    DateTimeOffset?       ResolvedAt,
    string?               ResolvedByName,
    DateTimeOffset?       VerifiedAt,
    string?               VerifiedByName,
    bool                  VerifiedBySameUser,
    string?               CancelReason,
    string                CreatedByName,
    DateTimeOffset        CreatedAt,
    DateTimeOffset        UpdatedAt);

/// <param name="PassRate">(Passed + PassedWithRemarks) / completed quality inspections, in % — null without any.</param>
public record BuildQualitySummaryDto(
    Guid                              ProjectId,
    IReadOnlyDictionary<string, int>  InspectionsByStatus,
    int                               CompletedInspections,
    decimal?                          PassRate,
    int                               OpenNonConformities,
    IReadOnlyDictionary<string, int>  OpenNonConformitiesBySeverity,
    int                               OverdueNonConformities,
    int                               AwaitingVerification,
    int                               CriticalOpen);

// ── Handover ──────────────────────────────────────────────────────────

public record BuildHandoverDocumentRequest(string Name, string? FileKey = null);

public record SaveBuildHandoverRequest(
    DateOnly?                                    ScheduledFor  = null,
    bool?                                        KeysDelivered = null,
    IReadOnlyList<BuildHandoverDocumentRequest>? Documents     = null,
    string?                                      Notes         = null);

public record CreateBuildWalkthroughRequest(
    string?                                   Title         = null,
    Guid?                                     TemplateId    = null,
    DateOnly?                                 ScheduledFor  = null,
    string?                                   InspectorName = null,
    IReadOnlyList<BuildChecklistItemRequest>? Items         = null);

public record DeliverBuildHandoverRequest(bool? KeysDelivered = null);

public record AcceptBuildHandoverRequest(string SignerName, string? Comment = null);

public record BuildHandoverDocumentDto(string Name, string? FileKey, string? FileUrl);

/// <param name="ProjectStatusNote">Set when delivering did not complete the project (e.g. it was cancelled).</param>
public record BuildHandoverDto(
    Guid                                    Id,
    Guid                                    ProjectId,
    string                                  Status,
    DateOnly?                               ScheduledFor,
    Guid?                                   WalkthroughInspectionId,
    string?                                 WalkthroughStatus,
    DateTimeOffset?                         DeliveredAt,
    string?                                 DeliveredByName,
    bool                                    KeysDelivered,
    IReadOnlyList<BuildHandoverDocumentDto> Documents,
    string?                                 Notes,
    DateTimeOffset?                         AcceptedAt,
    string?                                 AcceptedByName,
    string?                                 AcceptanceChannel,
    string?                                 AcceptanceComment,
    string                                  ProjectStatus,
    string?                                 ProjectStatusNote,
    int                                     OpenCriticalNonConformities,
    DateTimeOffset                          CreatedAt,
    DateTimeOffset                          UpdatedAt);

// ── Warranty ──────────────────────────────────────────────────────────

public record SaveBuildWarrantyTermRequest(string System, int Months, DateOnly? StartsAt = null, string? Description = null);

public record BuildWarrantyTermDto(
    Guid      Id,
    Guid      ProjectId,
    string    System,
    int       Months,
    DateOnly? StartsAt,
    DateOnly? ExpiresAt,
    bool      IsActive,
    string?   Description,
    bool      IsDefault);

// ── Assistance ────────────────────────────────────────────────────────

public record OpenBuildAssistanceRequest(
    string  Category,
    string  Title,
    string  Description,
    string? Location     = null,
    string? OpenedByName = null,
    string? ContactPhone = null);

/// <summary>WarrantyCovered null = evaluate from the warranty term of the category on the opening date.</summary>
public record TriageBuildAssistanceRequest(bool? WarrantyCovered = null, string? Notes = null);

public record ScheduleBuildAssistanceRequest(DateTimeOffset ScheduledFor);

public record ResolveBuildAssistanceRequest(string ResolutionNotes);

public record RejectBuildAssistanceRequest(string Reason);

public record AddBuildAssistancePhotoRequest(string Key);

public record BuildAssistanceDto(
    Guid                  Id,
    Guid                  PublicId,
    Guid                  ProjectId,
    string                ProjectName,
    int                   Number,
    string                Category,
    string                Title,
    string                Description,
    string?               Location,
    string                OpenedVia,
    string                OpenedByName,
    string?               ContactPhone,
    DateOnly              OpenedOn,
    string                Status,
    string?               TriageNotes,
    bool?                 WarrantyCovered,
    Guid?                 WarrantyTermId,
    string?               WarrantySystem,
    DateTimeOffset?       TriagedAt,
    string?               TriagedByName,
    DateTimeOffset?       ScheduledFor,
    DateTimeOffset?       StartedAt,
    DateTimeOffset?       ResolvedAt,
    string?               ResolutionNotes,
    DateTimeOffset?       ClosedAt,
    DateTimeOffset?       RejectedAt,
    string?               RejectionReason,
    int?                  ClientRating,
    string?               ClientFeedback,
    IReadOnlyList<string> PhotoKeys,
    IReadOnlyList<string> PhotoUrls,
    DateTimeOffset        CreatedAt,
    DateTimeOffset        UpdatedAt);
