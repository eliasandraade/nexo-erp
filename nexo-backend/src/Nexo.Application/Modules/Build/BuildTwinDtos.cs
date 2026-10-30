using System.Text.Json.Serialization;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 4 — DIGITAL TWIN: company-side DTOs (docs/build/internal-api.md, # Wave 4) and the public portal twin (contract v1.3).
// Storage keys never leave in these DTOs: files only ever go out as short-lived signed URLs.
// ═══════════════════════════════════════════════════════════════════════

// ── Tenant feature flags ──────────────────────────────────────────────────────

public sealed record BuildTenantSettingsDto(
    Guid TenantId,
    bool Build3DEnabled,
    bool BuildAiEnabled,
    bool BuildVisionEnabled,
    bool BuildPredictEnabled,
    /// <summary>False when no row exists yet (the defaults apply).</summary>
    bool Customized,
    string? UpdatedByName,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateBuildTenantSettingsRequest(
    bool? Build3DEnabled,
    bool? BuildAiEnabled,
    bool? BuildVisionEnabled,
    bool? BuildPredictEnabled);

// ── Models ────────────────────────────────────────────────────────────────────

public sealed record BuildModelVersionDto(
    Guid Id,
    Guid ModelId,
    int Number,
    string SourceFormat,
    string SourceFileName,
    long SizeBytes,
    string? Sha256,
    string GeometrySource,
    string ViewerSource,
    bool HasViewer,
    string Status,
    string? StatusMessage,
    int ElementCount,
    int StoreyCount,
    string? Schema,
    IReadOnlyList<string>? Layers,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset CreatedAt);

public sealed record BuildModelDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Discipline,
    Guid? CurrentVersionId,
    IReadOnlyList<BuildModelVersionDto> Versions,
    DateTimeOffset CreatedAt);

/// <summary>An upload's answer: the model and the version created (Queued, Ready or Rejected).</summary>
public sealed record BuildModelUploadResultDto(BuildModelDto Model, BuildModelVersionDto Version);

/// <summary>Short-lived signed URL of the viewer derivative (GLB) — the internal viewer's download; never the source file.</summary>
public sealed record BuildModelViewerDto(Guid VersionId, string? ViewerUrl, DateTimeOffset? ExpiresAt, string GeometrySource, string ViewerSource);

public sealed record BuildModelElementDto(
    string ExternalId,
    string Category,
    string? Name,
    string? TypeName,
    string? Material,
    Guid? ZoneId,
    string? ZoneName,
    System.Text.Json.JsonElement? Properties,
    string? State,
    Guid? StageId,
    bool ExplicitState);

public sealed record BuildModelElementPageDto(IReadOnlyList<BuildModelElementDto> Items, int Total, int Skip, int Take,
    IReadOnlyList<string> Categories);

public sealed record BuildModelZoneDto(
    Guid Id,
    Guid? ModelVersionId,
    string Kind,
    string Name,
    string? ExternalId,
    Guid? ParentZoneId,
    decimal? Elevation,
    int Order,
    IReadOnlyList<BuildZonePoint>? Outline,
    Guid? SheetVersionId,
    BuildSimplifiedBox? SimplifiedBox,
    int ElementCount);

public sealed record SaveBuildZoneRequest(
    string Kind,
    string Name,
    int Order = 0,
    Guid? ParentZoneId = null,
    IReadOnlyList<BuildZonePoint>? Outline = null,
    Guid? SheetVersionId = null,
    BuildSimplifiedBox? SimplifiedBox = null);

// ── State (4D) ────────────────────────────────────────────────────────────────

/// <summary>Compact state of a version on a date: counts per state, external ids per state, zone ids per state.</summary>
public sealed record BuildTwinStateDto(
    DateOnly Date,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> States,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> ZoneStates,
    int Unbound,
    int Total);

// ── Bindings and events ───────────────────────────────────────────────────────

public sealed record BuildModelStageBindingDto(
    Guid Id,
    Guid StageId,
    string StageName,
    string Scope,
    Guid? ModelVersionId,
    string? ElementExternalId,
    Guid? ZoneId,
    string? Category,
    string Source,
    DateTimeOffset CreatedAt);

public sealed record CreateBuildBindingRequest(
    Guid StageId,
    string Scope,
    Guid? ModelVersionId = null,
    string? ElementExternalId = null,
    Guid? ZoneId = null,
    string? Category = null);

/// <summary>
/// Binds every element of the version matching the filter (category and/or zone — the zone includes its sub-zones — and/or
/// explicit ids) to the stage, as Element bindings. At least one filter is required.
/// </summary>
public sealed record BulkBuildBindingRequest(
    Guid StageId,
    Guid ModelVersionId,
    string? Category = null,
    Guid? ZoneId = null,
    IReadOnlyList<string>? ExternalIds = null,
    /// <summary>Replace existing Element bindings of the matched elements (default) or keep them alongside.</summary>
    bool ReplaceExisting = true);

public sealed record BulkBuildBindingResultDto(int Matched, int Created, int Replaced);

public sealed record RecordBuildElementEventsRequest(
    Guid ModelVersionId,
    IReadOnlyList<string> ExternalIds,
    string State,
    string? Note = null,
    DateOnly? EffectiveDate = null,
    string? Source = null,
    Guid? SourceRef = null);

public sealed record BuildElementEventDto(
    Guid Id,
    Guid ModelVersionId,
    string ElementExternalId,
    string State,
    string Source,
    Guid? SourceRef,
    string? Note,
    DateOnly EffectiveDate,
    string RecordedByName,
    DateTimeOffset RecordedAt);

// ── Overview and 5D prep ──────────────────────────────────────────────────────

public sealed record BuildTwinStageDto(Guid StageId, string Name, int Order, int ProgressPercent, string Status, int BoundElements);

public sealed record BuildTwinOverviewDto(
    Guid ProjectId,
    bool Build3DEnabled,
    string GeometrySource,
    IReadOnlyList<BuildModelDto> Models,
    int ManualZones,
    int TotalElements,
    int BoundElements,
    decimal BindingCoveragePercent,
    int PendingJobs,
    IReadOnlyList<BuildTwinStageDto> Stages);

public sealed record BuildTwinStageCostDto(
    Guid StageId,
    string Name,
    int Order,
    decimal Budgeted,
    decimal Realized,
    int BoundElements,
    decimal? Volume,
    decimal? Area,
    decimal? Length,
    decimal? BudgetedPerElement,
    decimal? RealizedPerElement,
    decimal? RealizedPerCubicMeter,
    decimal? RealizedPerSquareMeter);

/// <summary>5D preparation — "rateio por quantidade": each stage's cost spread over its bound elements and their quantities.</summary>
public sealed record BuildTwinCostDto(
    Guid ProjectId,
    DateOnly Date,
    Guid? ModelVersionId,
    string Methodology,
    IReadOnlyList<BuildTwinStageCostDto> Stages);

// ── Reality capture ───────────────────────────────────────────────────────────

public sealed record BuildRealityCaptureDto(
    Guid Id,
    Guid ProjectId,
    string Type,
    DateTimeOffset CapturedAt,
    Guid? StageId,
    Guid? ZoneId,
    string? ElementExternalId,
    string Title,
    string? Notes,
    string ContentType,
    long SizeBytes,
    bool ClientVisible,
    string? Url,
    DateTimeOffset? UrlExpiresAt,
    bool Processed,
    DateTimeOffset CreatedAt);

public sealed record UpdateBuildRealityCaptureRequest(
    string Title,
    string? Notes,
    DateTimeOffset? CapturedAt,
    Guid? StageId,
    Guid? ZoneId,
    string? ElementExternalId);

public sealed record SetRealityCaptureVisibilityRequest(bool ClientVisible);

// ── Public portal (contract v1.3) ─────────────────────────────────────────────

public sealed record PublicBuildTwinZoneDto(
    string Id,
    string Kind,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Elevation,
    int Order,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BuildSimplifiedBox? SimplifiedBox,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalId);

public sealed record PublicBuildTwinStateDto(
    DateOnly Date,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> States,
    /// <summary>Zone id (as in <see cref="PublicBuildTwinZoneDto.Id"/>) per state.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ZoneStates,
    int Unbound);

public sealed record PublicBuildTwinDto
{
    /// <summary>NativeBim | Converted3D | AssistedMapping | SimplifiedRepresentation, or null when there is nothing to show yet.</summary>
    public string? GeometrySource { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ModelName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Discipline { get; init; }
    /// <summary>Signed URL (≈15 min) of the viewer GLB — never the original model file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ViewerUrl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? ViewerExpiresAt { get; init; }
    public IReadOnlyList<PublicBuildTwinZoneDto> Zones { get; init; } = [];
    public required PublicBuildTwinStateDto State { get; init; }
    public DateOnly? MinDate { get; init; }
    public DateOnly MaxDate { get; init; }
    public string Disclaimer { get; init; } = string.Empty;
}

public sealed record PublicBuildRealityCaptureDto(
    string Type,
    DateTimeOffset CapturedAt,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StageName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ZoneName,
    string ContentType,
    string? Url,
    DateTimeOffset? UrlExpiresAt);

public sealed record PublicBuildRealityCapturesDto(IReadOnlyList<PublicBuildRealityCaptureDto> Items, int Total, int Skip, int Take);

public static class BuildTwinTexts
{
    public static string Disclaimer(BuildGeometrySource? source) => source switch
    {
        BuildGeometrySource.NativeBim => "Modelo BIM do projeto. As cores mostram o andamento registrado pela construtora; não substituem a vistoria.",
        BuildGeometrySource.Converted3D => "Modelo 3D fornecido pela construtora. As cores mostram o andamento registrado; não substituem a vistoria.",
        BuildGeometrySource.AssistedMapping => "Mapeamento sobre a planta (2D), feito pela construtora. Não é um modelo 3D do projeto.",
        BuildGeometrySource.SimplifiedRepresentation => "Representação simplificada; não corresponde ao projeto arquitetônico.",
        _ => "O modelo da obra ainda não está disponível.",
    };
}
