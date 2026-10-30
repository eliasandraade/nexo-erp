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

/// <summary>Constants of the public portal contract.</summary>
public static class PublicBuildPortal
{
    public const string ContractVersion = "1";
    public const string SessionHeader   = "X-Build-Portal-Session";

    public const string CodeNotFound         = "portal_not_found";
    public const string CodePasswordRequired = "portal_password_required";
    public const string CodePasswordInvalid  = "portal_password_invalid";
    public const string CodeSessionExpired   = "portal_session_expired";
    public const string CodeLocked           = "portal_locked";
    public const string CodeSectionDisabled  = "portal_section_disabled";
    public const string CodePasswordNotNeeded = "portal_password_not_required";
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
