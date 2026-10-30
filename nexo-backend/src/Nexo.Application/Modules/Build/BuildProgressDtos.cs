using System.Text.Json;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// PROGRESS (weighted stages, baseline, snapshots, curves)
// ═══════════════════════════════════════════════════════════════════════

public record BuildProgressStageDto(
    Guid      StageId,
    string    Name,
    int       Order,
    decimal   Weight,
    string    Status,
    decimal?  PlannedPercent,
    decimal   ActualPercent,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    DateOnly? ForecastEnd,
    int?      DelayDays,
    IReadOnlyList<string> Flags);

public record BuildProgressPointDto(DateOnly Date, decimal Percent);

public record BuildProgressCurvesDto(
    IReadOnlyList<BuildProgressPointDto> Planned,
    IReadOnlyList<BuildProgressPointDto> Actual,
    IReadOnlyList<BuildProgressPointDto> Forecast);

/// <param name="PlanSource">"baseline", "revision" or "current" (no frozen plan yet: the stages' current dates).</param>
public record BuildProgressDto(
    Guid      ProjectId,
    DateOnly  Date,
    decimal?  PlannedPercent,
    decimal   ActualPercent,
    decimal?  ForecastPercent,
    DateOnly? PlannedDelivery,
    DateOnly? ForecastDelivery,
    string    Methodology,
    string    PlanSource,
    int?      PlanRevisionNumber,
    IReadOnlyList<BuildProgressStageDto> Stages,
    BuildProgressCurvesDto Curves,
    IReadOnlyList<string> Warnings);

/// <summary>The project as it was on <see cref="Date"/> (the latest snapshot on or before <see cref="RequestedDate"/>).</summary>
public record BuildProgressSnapshotDto(
    Guid           ProjectId,
    DateOnly       RequestedDate,
    DateOnly       Date,
    decimal?       PlannedPercent,
    decimal        ActualPercent,
    decimal?       ForecastPercent,
    DateOnly?      ForecastDeliveryDate,
    string         Methodology,
    JsonElement    Stages,
    DateTimeOffset UpdatedAt);

public record BuildScheduleItemDto(Guid StageId, string Name, DateOnly? PlannedStart, DateOnly? PlannedEnd, decimal Weight);

public record BuildScheduleRevisionDto(
    Guid           Id,
    int            Number,
    string         Kind,
    string?        Reason,
    IReadOnlyList<BuildScheduleItemDto> Items,
    string         CreatedByName,
    DateTimeOffset CreatedAt);

public record CreateScheduleBaselineRequest(string? Reason = null);

/// <summary>
/// Records progress events and keeps today's snapshot current. Implemented in Infrastructure; the stage
/// service stages the event in the same unit of work as the stage change.
/// </summary>
public interface IBuildProgressRecorder
{
    void Stage(BuildStageProgressEvent progressEvent);
    Task RefreshTodaySnapshotAsync(Guid projectId, CancellationToken ct = default);
    /// <summary>Whether the stage has an Open/InTreatment Critical non-conformity (blocks marking it 100%).</summary>
    Task<bool> HasOpenCriticalNonConformityAsync(Guid tenantId, Guid stageId, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════
// COST PER STAGE (single ledger: FinancialMovement.StageId)
// ═══════════════════════════════════════════════════════════════════════

public record AssignMovementStageRequest(Guid? StageId);

public record BuildMovementStageDto(Guid MovementId, Guid ProjectId, Guid? StageId, string? StageName);

/// <param name="Variance">Budgeted − realized (positive = under budget).</param>
public record BuildStageCostDto(
    Guid?   StageId,
    string  StageName,
    int?    Order,
    decimal Budgeted,
    decimal Realized,
    decimal Variance,
    int     MovementCount);

public record BuildCostByStageDto(
    Guid    ProjectId,
    decimal TotalBudgeted,
    decimal TotalRealized,
    decimal TotalVariance,
    IReadOnlyList<BuildStageCostDto> Stages);
