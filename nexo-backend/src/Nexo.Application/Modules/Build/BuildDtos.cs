using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// RESPONSE DTOs
// ═══════════════════════════════════════════════════════════════════════

public record BuildProjectDto(
    Guid               Id,
    string             Name,
    string             ClientName,
    string?            Location,
    string             Status,          // BuildProjectStatus.ToString()
    string             Type,            // BuildProjectType.ToString()
    DateOnly?          StartDate,
    DateOnly?          ExpectedEndDate,
    DateOnly?          ActualEndDate,
    decimal?           BudgetEstimated,
    decimal?           BudgetApproved,
    int                StageCount,
    int                CompletedStageCount,
    int                LogCount,
    DateTimeOffset     CreatedAt,
    DateTimeOffset     UpdatedAt);

public record BuildProjectDetailsDto(
    Guid                        Id,
    string                      Name,
    string                      ClientName,
    string?                     Location,
    string                      Status,
    string                      Type,
    DateOnly?                   StartDate,
    DateOnly?                   ExpectedEndDate,
    DateOnly?                   ActualEndDate,
    decimal?                    BudgetEstimated,
    decimal?                    BudgetApproved,
    IReadOnlyList<BuildStageDto> Stages,
    IReadOnlyList<BuildDailyLogDto> RecentLogs,   // last 5 logs
    DateTimeOffset              CreatedAt,
    DateTimeOffset              UpdatedAt);

public record BuildStageDto(
    Guid            Id,
    Guid            ProjectId,
    string          Name,
    string?         Description,
    int             Order,
    string          Status,              // BuildStageStatus.ToString()
    int             ProgressPercent,
    DateOnly?       PlannedStartDate,
    DateOnly?       PlannedEndDate,
    DateOnly?       ActualStartDate,
    DateOnly?       ActualEndDate,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  UpdatedAt);

public record BuildBudgetDto(
    Guid                          Id,
    Guid?                         ProjectId,
    string                        Name,
    string                        Status,          // BuildBudgetStatus.ToString()
    decimal                       TotalCost,
    decimal                       MarginPercent,
    decimal                       FinalPrice,
    IReadOnlyList<BuildBudgetItemDto> Items,
    DateTimeOffset                CreatedAt,
    DateTimeOffset                UpdatedAt);

public record BuildBudgetItemDto(
    Guid           Id,
    Guid           BudgetId,
    Guid?          StageId,
    string         Name,
    string         Category,
    decimal        Quantity,
    string         Unit,
    decimal        UnitCost,
    decimal        TotalCost,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record BuildDailyLogDto(
    Guid                             Id,
    Guid                             ProjectId,
    DateOnly                         Date,
    string?                          WeatherSummary,
    string                           Notes,
    int                              PhotoCount,
    IReadOnlyList<BuildDailyLogPhotoDto> Photos,
    DateTimeOffset                   CreatedAt,
    DateTimeOffset                   UpdatedAt);

public record BuildDailyLogPhotoDto(
    Guid           Id,
    Guid           DailyLogId,
    string         StorageKey,
    string?        Url,            // composed at read time from StorageKey — NOT persisted
    string?        Caption,
    DateTimeOffset CreatedAt);

public record BuildProjectFinancialSummaryDto(
    Guid     ProjectId,
    decimal? EstimatedBudget,
    decimal? ApprovedBudget,
    decimal  TotalRealizedExpenses,
    int      MovementCount,
    DateOnly? LastMovementDate,
    decimal  VarianceAmount,
    decimal  VariancePercent);

// ═══════════════════════════════════════════════════════════════════════
// DASHBOARD
// ═══════════════════════════════════════════════════════════════════════

public record BuildDashboardDto(
    int      TotalProjects,
    int      PlanningCount,
    int      InProgressCount,
    int      PausedCount,
    int      CompletedCount,
    int      CancelledCount,
    int      OverdueCount,            // ExpectedEndDate < today && status not Completed/Cancelled
    decimal  TotalEstimated,         // Σ BudgetEstimated across all projects
    decimal  TotalApproved,          // Σ BudgetApproved across all projects
    decimal  TotalRealized,          // Σ confirmed Obra Out movements (Core)
    decimal  Balance,                // TotalApproved - TotalRealized
    double   AvgStageProgress,       // mean of all stages' ProgressPercent (0-100)
    IReadOnlyList<BuildRecentExpenseDto> RecentExpenses);

public record BuildRecentExpenseDto(
    Guid     ProjectId,
    string   ProjectName,
    decimal  Amount,
    DateOnly Date,
    string   Description);

// ═══════════════════════════════════════════════════════════════════════
// PAGINATED WRAPPER (shared within module)
// ═══════════════════════════════════════════════════════════════════════

public record BuildPagedResult<T>(
    IReadOnlyList<T> Items,
    int              Total,
    int              Page,
    int              PageSize);

// ═══════════════════════════════════════════════════════════════════════
// COMMANDS / REQUESTS
// ═══════════════════════════════════════════════════════════════════════

// ── Projects ──────────────────────────────────────────────────────────

public record CreateBuildProjectRequest(
    string           Name,
    string           ClientName,
    string           Type,             // BuildProjectType enum name
    string?          Location          = null,
    DateOnly?        StartDate         = null,
    DateOnly?        ExpectedEndDate   = null,
    decimal?         BudgetEstimated   = null);

public record UpdateBuildProjectRequest(
    string           Name,
    string           ClientName,
    string           Type,
    string?          Location,
    DateOnly?        StartDate,
    DateOnly?        ExpectedEndDate,
    decimal?         BudgetEstimated,
    decimal?         BudgetApproved);

// ── Stages ────────────────────────────────────────────────────────────

public record CreateBuildStageRequest(
    string    Name,
    string?   Description    = null,
    DateOnly? PlannedStart   = null,
    DateOnly? PlannedEnd     = null);

public record UpdateBuildStageRequest(
    string    Name,
    string?   Description,
    int       Order,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd);

public record UpdateBuildStageProgressRequest(
    int     ProgressPercent,
    string? Status = null);   // optional explicit BuildStageStatus override

public record ReorderBuildStagesRequest(
    IReadOnlyList<StageOrderEntry> Entries);

public record StageOrderEntry(Guid StageId, int Order);

// ── Budgets ───────────────────────────────────────────────────────────

public record CreateBuildBudgetRequest(
    string   Name,
    Guid?    ProjectId     = null,
    decimal  MarginPercent = 0m);

public record AddBuildBudgetItemRequest(
    string   Name,
    string   Category,
    decimal  Quantity,
    string   Unit,
    decimal  UnitCost,
    Guid?    StageId = null);

public record UpdateBuildBudgetItemRequest(
    string   Name,
    string   Category,
    decimal  Quantity,
    string   Unit,
    decimal  UnitCost,
    Guid?    StageId);

public record SetBudgetMarginRequest(decimal MarginPercent);

public record ConvertBudgetToProjectRequest(
    Guid    ProjectId);   // must be an existing project in Planning/InProgress

// ── Daily Logs ────────────────────────────────────────────────────────

public record CreateDailyLogRequest(
    DateOnly  Date,
    string    Notes,
    string?   WeatherSummary = null);

public record UpdateDailyLogRequest(
    string    Notes,
    string?   WeatherSummary);

public record AddDailyLogPhotoRequest(
    string   StorageKey,
    string?  Caption = null);
