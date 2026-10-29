using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

// ── Module state and activation (platform) ───────────────────────────────────

public sealed record TaxModuleActivationDto(
    Guid Id, TaxModuleStatus Status, TaxApprovalMode Mode, string DecisionReference, string Reason, string ActorName, string ActorRole, DateTime At);

/// <summary>
/// Where the module stands: what the installation configuration allows (read-only here — it is changed in the
/// deployment, never from a screen), what the platform activated, and whether new work can run.
/// </summary>
public sealed record TaxModuleStateDto(
    bool ConfigEnabled, TaxApprovalMode ConfigMode, IReadOnlyList<string> ConfigurationProblems,
    TaxModuleStatus Status, TaxApprovalMode? ActivatedMode, TaxModuleActivationDto? Latest, bool NewWorkRuns, string Summary,
    IReadOnlyList<TaxModuleActivationDto> History);

/// <summary>
/// Activation of the configured mode. <c>Confirmation</c> must repeat the exact phrase the wizard shows, and the
/// controlled pilot must acknowledge that it runs in shadow mode — a single click never turns the module on.
/// </summary>
public sealed record ActivateTaxModuleRequest(
    TaxApprovalMode Mode, string DecisionReference, string Reason, string Confirmation, bool AcknowledgeShadowMode);

public sealed record SuspendTaxModuleRequest(string Reason, string Confirmation);
public sealed record DeactivateTaxModuleRequest(string Reason, string Confirmation);

public enum TaxReadinessLevel
{
    Ok = 1,
    Info = 2,
    Warning = 3,
    /// <summary>Blocks the activation of the mode it refers to.</summary>
    Blocking = 4,
}

public sealed record TaxReadinessItemDto(string Key, string Area, string Label, TaxReadinessLevel Level, string Detail);

/// <summary>Administrative diagnosis of Tax Intelligence (platform only — never a public health check).</summary>
public sealed record TaxReadinessDto(
    DateTime GeneratedAt, string EngineVersion, string CanonicalVersion, string CoveragePolicy, TaxModuleStateDto Module,
    IReadOnlyList<TaxReadinessItemDto> Items, IReadOnlyList<string> RulesEligible, IReadOnlyList<string> RulesBlocking,
    IReadOnlyList<string> PendingRegulation, TaxAuditEventDto? LastAuditEvent);

/// <summary>Preconditions of activating a mode today, item by item, with exactly what blocks it.</summary>
public sealed record TaxActivationChecklistDto(
    TaxApprovalMode Mode, bool CanActivate, string ConfirmationPhrase, IReadOnlyList<TaxReadinessItemDto> Items,
    IReadOnlyList<string> RulesMissingApproval, IReadOnlyList<string> Caveats);

public sealed record TaxAuditEventDto(
    Guid Id, string Action, string Severity, string? EntityType, string? EntityId, string Description, string ActorName, DateTime At, Guid? TenantId);

// ── Professional reviews ─────────────────────────────────────────────────────

/// <summary>One professional review as recorded: the professional as informed, and apart who recorded it and when.</summary>
public sealed record TaxProfessionalReviewRecordDto(
    Guid Id, Guid RuleVersionId, ProfessionalReviewStatus Status, ProfessionalReviewStatus PreviousStatus, string Reviewer,
    string? ProfessionalReference, DateOnly? ReviewedOn, string? Scope, IReadOnlyList<string> CoveredRegimes, DateOnly? ValidFrom,
    DateOnly? ValidTo, string? Assumptions, string? Caveats, string? Notes, TaxStoredFileDto? SupportingDocument,
    string RecordedByName, string RecordedByRole, DateTime RecordedAt);

// ── Files ────────────────────────────────────────────────────────────────────

/// <summary>A stored private file: what was measured on upload, never a URL. Download goes through an authorised endpoint.</summary>
public sealed record TaxStoredFileDto(
    Guid Id, string OriginalFileName, string ContentType, long SizeBytes, string Sha256, TaxFileScanStatus ScanStatus,
    TaxFileStatus Status, int Version, Guid? ReplacesFileId, string? Description, string UploadedByName, DateTime UploadedAt,
    string? RevokedByName, DateTime? RevokedAt, string? RevocationReason, bool LegalHold, string? LegalHoldReason);

public sealed record TaxEvidenceFileDto(
    TaxStoredFileDto File, TaxFileOwner Owner, Guid OwnerId, Guid? RequirementId, TaxEvidenceType EvidenceType);

public sealed record RevokeTaxFileRequest(string Reason);
public sealed record SetLegalHoldRequest(bool Hold, string Reason);

public sealed record TaxFileStorageStatusDto(bool Available, string State, string Message, long MaxBytes, IReadOnlyList<string> AcceptedExtensions,
    bool ScannerConfigured);

// ── Retention ────────────────────────────────────────────────────────────────

public sealed record TaxRetentionPolicyDto(
    Guid Id, Guid? TenantId, int Version, TaxEvidenceType? EvidenceType, int RetentionMonths, TaxRetentionStart StartsAt, bool Enabled,
    bool AllowTenantOverride, string Rationale, string Reference, string ConfiguredByName, DateTime ConfiguredAt, DateTime? SupersededAt);

public sealed record ConfigureRetentionPolicyRequest(
    TaxEvidenceType? EvidenceType, int RetentionMonths, TaxRetentionStart StartsAt, bool Enabled, bool AllowTenantOverride,
    string Rationale, string Reference);

/// <summary>A file a valid policy would make eligible for disposal — a preview only: nothing is ever discarded automatically.</summary>
public sealed record TaxRetentionCandidateDto(
    Guid FileId, Guid TenantId, string OriginalFileName, TaxEvidenceType EvidenceType, DateTime UploadedAt, DateOnly EligibleFrom,
    Guid PolicyId, int PolicyVersion, string Sha256);

public sealed record TaxRetentionPreviewDto(
    DateOnly AsOf, bool AutomaticDisposal, int FilesConsidered, int OnLegalHold, int WithoutPolicy,
    IReadOnlyList<TaxRetentionCandidateDto> Eligible, string Notice);

// ── Materiality ──────────────────────────────────────────────────────────────

public sealed record TaxMaterialityPolicyDto(
    Guid Id, int Version, decimal Threshold, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Rationale,
    string DecisionReference, string ConfiguredByName, DateTime ConfiguredAt);

public sealed record TaxMaterialityDto(TaxMaterialityPolicyDto? InForce, decimal? InstallationDefault, IReadOnlyList<TaxMaterialityPolicyDto> History);

public sealed record ConfigureMaterialityRequest(
    decimal Threshold, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Rationale, string DecisionReference);

// ── Classification catalogue ─────────────────────────────────────────────────

public sealed record TaxClassificationCodeDto(
    Guid Id, TaxClassificationKind Kind, string Code, string Description, string Source, string? SourceVersion, DateOnly ValidFrom,
    DateOnly? ValidTo, TaxClassificationStatus Status, ProfessionalReviewStatus ProfessionalReview, string? ProfessionalReviewNotes,
    string? Notes, Guid? ImportBatchId, Guid? SourceDocumentId, string CreatedByName, DateTime CreatedAt, string? StatusChangedByName,
    DateTime? StatusChangedAt);

public sealed record TaxClassificationImportItem(string Code, string Description, DateOnly ValidFrom, DateOnly? ValidTo, string? Notes);

/// <summary>Codes taken from a documented source; they enter as Draft and are activated explicitly.</summary>
public sealed record ImportClassificationCodesRequest(
    TaxClassificationKind Kind, string Source, string? SourceVersion, Guid? SourceDocumentId, IReadOnlyList<TaxClassificationImportItem> Items);

public sealed record TaxClassificationImportResultDto(Guid BatchId, int Imported, IReadOnlyList<string> Skipped);

public sealed record ChangeClassificationStatusRequest(TaxClassificationStatus Status, string Reason);
public sealed record RecordClassificationReviewRequest(ProfessionalReviewStatus Status, string Notes);

public sealed record TaxItemClassificationDto(
    Guid? Id, Guid ProductId, string ProductName, string? ProductNcm, TaxItemKind? ItemKind, string? Ncm, string? Nbs, string? CClassTrib,
    TaxClassificationStatus? CatalogStatus, string? Notes, string? SetByName, DateTime? UpdatedAt);

public sealed record SetItemClassificationRequest(TaxItemKind ItemKind, string? Ncm, string? Nbs, string CClassTrib, string? Notes);

// ── Obligations catalogue ────────────────────────────────────────────────────

public sealed record TaxObligationDefinitionDto(Guid Id, string Code, string Name, string Description, string Reference, bool Active, string CreatedByName);
public sealed record CreateObligationDefinitionRequest(string Code, string Name, string? Description, string Reference);
public sealed record SetObligationActiveRequest(bool Active);

public sealed record TaxObligationResultDto(
    string ObligationCode, string ObligationName, ComplianceAssessmentStatus Status, string? EvidenceReference, Guid? EvidenceFileId,
    string? Remediation, DateOnly? RemediationVerifiedOn, string? RemediationVerifiedBy, string? Notes);

public sealed record TaxObligationResultRequest(
    string ObligationCode, ComplianceAssessmentStatus Status, string? EvidenceReference, Guid? EvidenceFileId, string? Remediation,
    DateOnly? RemediationVerifiedOn, string? RemediationVerifiedBy, string? Notes);

// ── Paging, timelines and exports ────────────────────────────────────────────

public sealed record TaxPage<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public sealed record TaxTimelineEventDto(string Action, string Description, string ActorName, DateTime At, string Severity);

public sealed record AssignOpportunityRequest(Guid? UserId, string? UserName);

public sealed record ReplaceEvidenceRequest(string Description, string? DocumentReference, string? Notes);

public sealed record RevokeEvidenceRequest(string Reason);

/// <summary>A refusal of new work, as audited (code and when) — what the screens show as "recusas recentes".</summary>
public sealed record TaxRefusalDto(string Action, string? Code, string Description, string ActorName, DateTime At, Guid? TenantId);
