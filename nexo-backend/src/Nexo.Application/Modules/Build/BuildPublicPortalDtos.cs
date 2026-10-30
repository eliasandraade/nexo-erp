using System.Text.Json.Serialization;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// CLIENT PORTAL — public contract v1 (docs/build/client-portal-contract.md).
// Separate from the admin DTOs on purpose: explicit allowlist, no internal ids, no password data,
// no internal notes. A section the company did not enable is absent from the JSON (not even null).
// ═══════════════════════════════════════════════════════════════════════

public sealed record PublicBuildBootstrapDto
{
    public string ContractVersion { get; init; } = PublicBuildPortal.ContractVersion;
    /// <summary>Names of the enabled sections (only the enabled ones are listed).</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];
    public required PublicBuildPortalDto  Portal  { get; init; }
    public required PublicBuildCompanyDto Company { get; init; }
    public required PublicBuildProjectDto Project { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildProgressSummaryDto? Progress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<PublicBuildStageDto>? Stages { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildScheduleDto? Schedule { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildWeatherDto? Weather { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildFinancialSummaryDto? FinancialSummary { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildContactDto? Contact { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<PublicBuildUpdateDto>? LatestUpdates { get; init; }
    /// <summary>v1.2: "construction" until the handover is delivered, then "delivered".</summary>
    public string Phase { get; init; } = PublicBuildPortal.PhaseConstruction;
    /// <summary>v1.2 "Meu Imóvel": only after the delivery and with the flag postConstruction.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicBuildPropertyDto? Property { get; init; }
}

public sealed record PublicBuildPortalDto(
    string? Title,
    string? WelcomeMessage,
    string? PrimaryColor,
    string? AccentColor,
    string? LogoUrl,
    string? CoverUrl,
    string  Access);

public sealed record PublicBuildCompanyDto(string Name, string? LogoUrl);

public sealed record PublicBuildProjectDto(
    string    Name,
    string    Type,
    string    Status,
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate);

public sealed record PublicBuildProgressSummaryDto
{
    public DateOnly AsOf { get; init; }
    public decimal ActualPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? PlannedPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateOnly? ForecastDelivery { get; init; }
    public string Methodology { get; init; } = string.Empty;
}

public sealed record PublicBuildStageDto(string Name, int Order, string Status, decimal ActualPercent);

public sealed record PublicBuildScheduleDto(
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate,
    IReadOnlyList<PublicBuildScheduleStageDto> Stages);

public sealed record PublicBuildScheduleStageDto(string Name, int Order, DateOnly? PlannedStart, DateOnly? PlannedEnd);

/// <summary>Weather forecast at the site (provider data — it can change; never a promise).</summary>
public sealed record PublicBuildWeatherDto(
    DateOnly Date,
    double   TemperatureMax,
    double   TemperatureMin,
    double   PrecipitationMm,
    string   Description,
    string   Source,
    string   Notice);

public sealed record PublicBuildFinancialSummaryDto(
    decimal? ApprovedBudget,
    decimal  RealizedTotal,
    string   Currency,
    DateOnly AsOf);

public sealed record PublicBuildContactDto(string? Name, string? Phone, string? Email, string? WhatsApp);

public sealed record PublicBuildUpdateDto(string Kind, string Title, string Body, DateTimeOffset PublishedAt);

public sealed record PublicBuildProgressDto
{
    public DateOnly AsOf { get; init; }
    public decimal ActualPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? PlannedPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateOnly? ForecastDelivery { get; init; }
    public string Methodology { get; init; } = string.Empty;
    public required PublicBuildCurvesDto Curves { get; init; }
}

public sealed record PublicBuildCurvesDto
{
    public IReadOnlyList<BuildProgressPointDto> Actual { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<BuildProgressPointDto>? Planned { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<BuildProgressPointDto>? Forecast { get; init; }
}

/// <summary>
/// One timeline entry. <c>type</c>: "update" | "daily_log" | "stage_progress" | "stage_completed" | "photo".
/// Only the fields of that type are present.
/// </summary>
public sealed record PublicBuildTimelineItemDto
{
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateOnly? Date { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StageName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Percent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PreviousPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PhotoUrl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Caption { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PhotoCount { get; init; }
}

public sealed record PublicBuildTimelineDto(IReadOnlyList<PublicBuildTimelineItemDto> Items, DateTimeOffset? NextBefore);

public sealed record PublicBuildPhotoDto(string? Url, string? Caption, DateOnly Date, DateTimeOffset UrlExpiresAt);

public sealed record PublicBuildPhotosDto(IReadOnlyList<PublicBuildPhotoDto> Items, int Total, int Skip, int Take);

public sealed record CreatePublicBuildSessionRequest(string? Password);

public sealed record PublicBuildSessionDto(string Token, DateTimeOffset ExpiresAt);

/// <summary>What a locked (password) portal shows before the password: name, logo and title only.</summary>
public sealed record PublicBuildPortalTeaserDto(string Name, string? LogoUrl, string? Title);

/// <summary>
/// Error body of the public portal API (documentation type for OpenAPI — the shape every error of
/// <c>/api/public/build</c> already had): <c>error</c> (Portuguese message) and <c>code</c>; <c>portal</c> only in the 401
/// of a password portal; <c>retryAfterSeconds</c> only in the 429 portal_locked / portal_busy. Rate-limit 429 has no body.
/// </summary>
public sealed record PublicBuildErrorDto(string Error, string Code, PublicBuildPortalTeaserDto? Portal = null, int? RetryAfterSeconds = null);

// ── Wave 2 (contract v1.1): change orders, selections, measurements ─────────

/// <summary>A line of a change order. <c>unitPrice</c>/<c>total</c> only when the portal shows the financial summary (v1.5).</summary>
public sealed record PublicBuildChangeOrderItemDto(string Name, string Kind, decimal Quantity, string Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Total);

/// <summary>
/// A change order shown to the client. <c>id</c> is an opaque public id. <c>status</c>: "Pending" (waiting for
/// the client) | "Approved" | "Rejected". <c>canDecide</c> is true only for a pending order on a password portal.
/// </summary>
public sealed record PublicBuildChangeOrderDto
{
    public Guid Id { get; init; }
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    public string Reason { get; init; } = string.Empty;
    /// <summary>Only when the portal shows the financial summary (v1.5); otherwise absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? CostImpact { get; init; }
    public int ScheduleImpactDays { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StageName { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool CanDecide { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? SentAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DecidedAt { get; init; }
    /// <summary>"Portal" (the client) or "Internal" (recorded by the company).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DecisionChannel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ClientComment { get; init; }
    public IReadOnlyList<PublicBuildChangeOrderItemDto> Items { get; init; } = [];
}

public sealed record PublicBuildChangeOrdersDto(IReadOnlyList<PublicBuildChangeOrderDto> Items);

/// <summary><c>decision</c>: "approve" | "reject". <c>signerName</c> is required (who is deciding).</summary>
public sealed record PublicBuildChangeOrderDecisionRequest(string? Decision, string? Comment, string? SignerName);

public sealed record PublicBuildSelectionOptionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    public decimal PriceDelta { get; init; }
    public bool IsStandard { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ImageUrl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LeadTimeDays { get; init; }
}

/// <summary>
/// A selection shown to the client. <c>status</c>: "Open" | "Submitted" | "Confirmed". <c>canChoose</c>: open,
/// before the deadline, on a password portal.
/// </summary>
public sealed record PublicBuildSelectionDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StageName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateOnly? Deadline { get; init; }
    public bool DeadlinePassed { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool CanChoose { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? ChosenOptionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ClientComment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DecidedAt { get; init; }
    public IReadOnlyList<PublicBuildSelectionOptionDto> Options { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? ImageUrlsExpireAt { get; init; }
}

public sealed record PublicBuildSelectionsDto(IReadOnlyList<PublicBuildSelectionDto> Items);

public sealed record PublicBuildSelectionChoiceRequest(Guid? OptionId, string? Comment);

public sealed record PublicBuildMeasurementItemDto
{
    public string StageName { get; init; } = string.Empty;
    public int PreviousPercent { get; init; }
    public int MeasuredPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? Amount { get; init; }
}

/// <summary>An approved measurement. Amounts only when the financial summary is enabled too.</summary>
public sealed record PublicBuildMeasurementDto
{
    public int Number { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? ApprovedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? TotalAmount { get; init; }
    public IReadOnlyList<PublicBuildMeasurementItemDto> Items { get; init; } = [];
}

public sealed record PublicBuildMeasurementsDto(IReadOnlyList<PublicBuildMeasurementDto> Items);

/// <summary>Constants of the public portal contract.</summary>
public static class PublicBuildPortal
{
    public const string ContractVersion = "1.6";
    public const string SessionHeader   = "X-Build-Portal-Session";

    public const string CodeNotFound         = "portal_not_found";
    public const string CodePasswordRequired = "portal_password_required";
    public const string CodePasswordInvalid  = "portal_password_invalid";
    public const string CodeSessionExpired   = "portal_session_expired";
    public const string CodeLocked           = "portal_locked";
    /// <summary>v1.5: too many password checks running at once — retry in a moment (429 with retryAfterSeconds).</summary>
    public const string CodeBusy             = "portal_busy";
    public const string CodeSectionDisabled  = "portal_section_disabled";
    public const string CodePasswordNotNeeded = "portal_password_not_required";

    // Wave 2 (v1.1)
    public const string CodeDecisionRequiresPassword = "portal_decision_requires_password";
    public const string CodeItemNotFound             = "portal_item_not_found";
    public const string CodeAlreadyDecided           = "change_order_already_decided";
    public const string CodeInvalidDecision          = "invalid_decision";
    public const string CodeSignerRequired           = "signer_name_required";
    public const string CodeSelectionClosed          = "selection_closed";
    public const string CodeSelectionDeadlinePassed  = "selection_deadline_passed";
    public const string CodeInvalidOption            = "invalid_option";

    // Wave 3 (v1.2)
    public const string PhaseConstruction            = "construction";
    public const string PhaseDelivered               = "delivered";
    public const string CodeHandoverNotReady         = "handover_not_ready";
    public const string CodeHandoverAlreadyAccepted  = "handover_already_accepted";
    public const string CodePropertyNotDelivered     = "property_not_delivered";
    public const string CodeInvalidAssistance        = "invalid_assistance_request";
    public const string CodeInvalidCategory          = "invalid_category";
    public const string CodeAssistanceLimit          = "assistance_limit_reached";
    public const string CodeAssistanceClosed         = "assistance_closed";
    public const string CodeAssistancePhotoLimit     = "assistance_photo_limit";
    public const string CodeAssistanceNotResolved    = "assistance_not_resolved";
    public const string CodeFeedbackAlreadyGiven     = "feedback_already_given";
    public const string CodeInvalidRating            = "invalid_rating";
    public const string CodeInvalidFileType          = "invalid_file_type";
    public const string CodeInvalidFileContent       = "invalid_file_content";
    public const string CodeFileTooLarge             = "file_too_large";
    public const string CodeStorageUnavailable       = "storage_unavailable";

    // v1.5
    /// <summary>A write collided with a simultaneous change and the retry collided too (409): try again.</summary>
    public const string CodeConflict                 = "portal_conflict";
    /// <summary>A JSON body beyond 64 KB (413).</summary>
    public const string CodePayloadTooLarge          = "payload_too_large";
}

/// <summary>
/// A refusal of the public portal answered with a specific status and code (404 not found/unpublished —
/// identical bodies —, 401 password/session, 429 locked). Mapped by the public controller.
/// </summary>
public sealed class BuildPortalAccessException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public PublicBuildPortalTeaserDto? Portal { get; }
    public int? RetryAfterSeconds { get; }

    public BuildPortalAccessException(int status, string code, string message,
        PublicBuildPortalTeaserDto? portal = null, int? retryAfterSeconds = null) : base(message)
    {
        Status = status;
        Code = code;
        Portal = portal;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public static BuildPortalAccessException NotFound()
        => new(404, PublicBuildPortal.CodeNotFound, "Portal não encontrado.");

    public static BuildPortalAccessException SectionDisabled()
        => new(404, PublicBuildPortal.CodeSectionDisabled, "Esta seção não está disponível neste portal.");
}
