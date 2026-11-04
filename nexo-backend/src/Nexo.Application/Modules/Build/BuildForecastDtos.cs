namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// Wave 6 — forecast, weather per activity, portfolio dashboard, platform observability
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// A delivery forecast (methodology <c>productivity/1</c>). <c>Stored</c> false = computed on the fly for this request (no
/// forecast stored today); <c>Source</c> Scheduled (daily job), Manual (POST) or OnTheFly.
/// </summary>
public sealed record BuildForecastDto(
    Guid? Id,
    Guid ProjectId,
    DateOnly Date,
    DateTimeOffset GeneratedAt,
    string Source,
    bool Stored,
    string Methodology,
    int Version,
    DateOnly? ForecastDelivery,
    DateOnly? PlannedDelivery,
    /// <summary>Forecast − planned delivery (calendar days; positive = late).</summary>
    int? DelayDays,
    /// <summary>The project's contractual expected end, when set.</summary>
    DateOnly? ExpectedEnd,
    /// <summary>Forecast − expected end (calendar days; positive = late).</summary>
    int? DaysLateVsContract,
    decimal Confidence,
    IReadOnlyList<BuildForecastStageResult> Stages,
    IReadOnlyList<BuildForecastFactor> Factors,
    BuildForecastCalendarDto Calendar,
    string? CreatedByName);

public sealed record BuildForecastCalendarDto(IReadOnlyList<string> WorkingDays, string Holidays, int WindowDays);

public sealed record BuildForecastHistoryItemDto(
    Guid Id,
    DateOnly Date,
    DateTimeOffset GeneratedAt,
    string Source,
    string Methodology,
    int Version,
    DateOnly? ForecastDelivery,
    DateOnly? PlannedDelivery,
    int? DelayDays,
    decimal Confidence,
    string? CreatedByName);

// ── Weather per activity ─────────────────────────────────────────────────────

/// <summary>
/// Weather at the site: today + the next 7 days, with the risk of each day for the sensitive stages in the field. Available
/// false (still 200) with <c>Reason</c> <c>no_coordinates</c>, <c>weather_disabled</c> or <c>provider_unavailable</c>.
/// </summary>
public sealed record BuildWeatherDto(
    Guid ProjectId,
    bool Available,
    string? Reason,
    string? Message,
    string Source,
    string Disclaimer,
    BuildWeatherDayDto? Today,
    IReadOnlyList<BuildWeatherDayDto> Days,
    IReadOnlyList<BuildWeatherStageDto> Stages);

public sealed record BuildWeatherDayDto(
    DateOnly Date,
    double? TemperatureMax,
    double? TemperatureMin,
    double? PrecipitationMm,
    int? PrecipitationProbability,
    double? WindSpeedMaxKmh,
    string? Description,
    /// <summary>Worst condition of the day: none, medium or high.</summary>
    string Level,
    IReadOnlyList<BuildWeatherRiskDto> Risks);

public sealed record BuildWeatherRiskDto(Guid StageId, string StageName, string Kind, string Level, string Message);

/// <summary>Weather sensitivity of a stage: basis explicit (set by the company), keyword (name matched) or none.</summary>
public sealed record BuildWeatherStageDto(Guid StageId, string Name, bool Sensitive, string Basis, string? Keyword, bool? ExplicitSetting,
    DateOnly? ActiveFrom, DateOnly? ActiveTo);

/// <summary>true/false sets the stage's weather sensitivity; null returns it to the keyword default.</summary>
public sealed record SetBuildStageWeatherSensitivityRequest(bool? WeatherSensitive);

// ── Portfolio dashboard (additive fields of GET /api/v1/build/dashboard) ─────

public sealed record BuildDashboardProjectDto(
    Guid ProjectId,
    string Name,
    string ClientName,
    string Status,
    decimal ActualPercent,
    decimal? PlannedPercent,
    DateOnly? ExpectedEnd,
    DateOnly? ForecastDelivery,
    /// <summary>Forecast − expected end (calendar days, positive = late); null without both dates.</summary>
    int? DaysLate,
    string ForecastMethodology,
    decimal CostRealized,
    /// <summary>Approved budget (estimated when not approved) + approved change orders.</summary>
    decimal? ContractTotal,
    /// <summary>(actual % × contract total) ÷ realized; null without realized cost or contract.</summary>
    decimal? CpiLike,
    IReadOnlyDictionary<string, int> OpenNonConformities,
    int OpenAssistance,
    int PendingChangeOrders,
    int LateMaterials,
    bool PortalPublished,
    DateOnly? LastDailyLogDate,
    int PendingAiSuggestions);

public sealed record BuildDashboardTotalsDto(
    int ActiveProjects,
    int LateProjects,
    decimal CostRealized,
    decimal ContractTotal,
    int OpenNonConformities,
    int OpenCriticalNonConformities,
    int OpenAssistance,
    int PendingChangeOrders,
    int LateMaterials,
    int PublishedPortals,
    int PendingAiSuggestions);

public sealed record BuildDashboardRiskDto(Guid ProjectId, string ProjectName, string Kind, string Severity, string Message, decimal? Value);

// ── Platform (SuperAdmin) observability ──────────────────────────────────────

public sealed record BuildPlatformOverviewDto(DateTimeOffset GeneratedAt, IReadOnlyList<BuildPlatformTenantDto> Tenants, BuildPlatformTotalsDto Totals);

public sealed record BuildPlatformTotalsDto(int Tenants, int ProjectsActive, int ProjectsTotal, int PublishedPortals, int JobsFailed,
    int JobsPending, long StorageBytes, int AiSuggestions30d, long AiCostMicros30d);

public sealed record BuildPlatformTenantDto(
    Guid TenantId,
    string CompanyName,
    /// <summary>Status of the "build" module subscription (Active, Trialing…), null when only data exists.</summary>
    string? ModuleStatus,
    int ProjectsActive,
    int ProjectsTotal,
    int PublishedPortals,
    IReadOnlyDictionary<string, int> ModelVersionsByStatus,
    int JobsFailed,
    int JobsPending,
    /// <summary>Last processing error, sanitized (no file names, paths or storage keys).</summary>
    string? LastJobError,
    DateTimeOffset? LastJobErrorAt,
    BuildPlatformStorageDto Storage,
    BuildPlatformAiUsageDto Ai30d,
    BuildPlatformFlagsDto Flags);

/// <summary>Bytes from the stored SizeBytes columns. Daily-log photos have no size column: only their count.</summary>
public sealed record BuildPlatformStorageDto(long ModelsBytes, long RealityCaptureBytes, long DocumentsBytes, long? PhotosBytes,
    int PhotosCount, long TotalBytes);

public sealed record BuildPlatformAiUsageDto(int Suggestions, int Accepted, int Rejected, decimal? AcceptedRate, long InputTokens,
    long OutputTokens, long CostMicros);

public sealed record BuildPlatformFlagsDto(bool Build3D, bool BuildAi, bool BuildVision, bool BuildPredict, bool Customized);

public sealed record BuildPlatformJobDto(
    Guid Id,
    Guid TenantId,
    string CompanyName,
    Guid ProjectId,
    Guid VersionId,
    int VersionNumber,
    string SourceFormat,
    string Kind,
    string Status,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

// ── Gaps of the frontend ─────────────────────────────────────────────────────

/// <summary>Short-lived (15 min) signed URL of a version's private source file — company users only, audited, never in the portal.</summary>
public sealed record BuildModelSourceDto(Guid VersionId, string FileName, string SourceFormat, long SizeBytes, string? Sha256, string Url,
    DateTimeOffset ExpiresAt);

/// <summary>State of the project's manual zones on a date (through zone bindings), without a model version.</summary>
public sealed record BuildZoneStatesDto(DateOnly Date, IReadOnlyList<BuildZoneStateDto> Zones, IReadOnlyDictionary<string, int> Counts);

public sealed record BuildZoneStateDto(Guid ZoneId, string Name, string Kind, Guid? ParentZoneId, string? State, Guid? StageId, string? StageName);
