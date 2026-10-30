using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A named phase within a BuildProject (e.g. Foundation, Structure, Finishing).
/// Order is explicit (int) so the user can reorder stages freely.
/// ProgressPercent is manually set — no auto-calculation from tasks.
/// </summary>
public class BuildStage : TenantEntity
{
    private BuildStage() { }
    private BuildStage(Guid tenantId) : base(tenantId) { }

    public Guid             ProjectId        { get; private set; }
    public string           Name             { get; private set; } = string.Empty;
    public string?          Description      { get; private set; }
    public int              Order            { get; private set; }
    public BuildStageStatus Status           { get; private set; }
    public DateOnly?        PlannedStartDate { get; private set; }
    public DateOnly?        PlannedEndDate   { get; private set; }
    public DateOnly?        ActualStartDate  { get; private set; }
    public DateOnly?        ActualEndDate    { get; private set; }
    public int              ProgressPercent  { get; private set; } // 0-100
    /// <summary>
    /// Relative weight of the stage in the project's overall progress (overall = Σ(weight·%) / Σweight).
    /// Must be &gt; 0; existing rows default to 1 (all stages equal).
    /// </summary>
    public decimal          Weight           { get; private set; } = 1m;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildStage Create(
        Guid    tenantId,
        Guid    projectId,
        string  name,
        int     order,
        string? description      = null,
        DateOnly? plannedStart   = null,
        DateOnly? plannedEnd     = null)
    {
        if (projectId == Guid.Empty)   throw new DomainException("ProjectId is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Stage name is required.");
        if (order < 0)                 throw new DomainException("Order cannot be negative.");

        return new BuildStage(tenantId)
        {
            ProjectId        = projectId,
            Name             = name.Trim(),
            Description      = description?.Trim(),
            Order            = order,
            Status           = BuildStageStatus.Pending,
            PlannedStartDate = plannedStart,
            PlannedEndDate   = plannedEnd,
            ProgressPercent  = 0,
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void UpdateDetails(
        string    name,
        string?   description,
        int       order,
        DateOnly? plannedStart,
        DateOnly? plannedEnd)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Stage name is required.");
        Name             = name.Trim();
        Description      = description?.Trim();
        Order            = order;
        PlannedStartDate = plannedStart;
        PlannedEndDate   = plannedEnd;
        SetUpdatedAt();
    }

    public void UpdateStatus(BuildStageStatus status)
    {
        Status = status;
        if (status == BuildStageStatus.InProgress && ActualStartDate is null)
            ActualStartDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (status == BuildStageStatus.Completed && ActualEndDate is null)
            ActualEndDate = DateOnly.FromDateTime(DateTime.UtcNow);
        SetUpdatedAt();
    }

    /// <summary>
    /// Records the stage's real progress. The first progress above zero marks the actual start (on
    /// <paramref name="effectiveDate"/>, default today) and moves a pending stage to in progress; 100%
    /// completes it and marks the actual end.
    /// </summary>
    public void UpdateProgress(int percent, DateOnly? effectiveDate = null)
    {
        if (percent < 0 || percent > 100)
            throw new DomainException("Progress must be between 0 and 100.");
        var day = effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        ProgressPercent = percent;
        if (percent > 0 && ActualStartDate is null)
            ActualStartDate = day;
        if (percent > 0 && percent < 100 && Status == BuildStageStatus.Pending)
            Status = BuildStageStatus.InProgress;
        // Auto-transition: 100% → Completed if not already
        if (percent == 100)
        {
            if (Status != BuildStageStatus.Completed) Status = BuildStageStatus.Completed;
            ActualEndDate ??= day;
        }
        SetUpdatedAt();
    }

    /// <summary>Sets the stage's weight in the overall progress (must be greater than zero).</summary>
    public void SetWeight(decimal weight)
    {
        if (weight <= 0m) throw new DomainException("O peso da etapa precisa ser maior que zero.");
        if (weight > 1_000_000m) throw new DomainException("Peso da etapa muito alto.");
        Weight = weight;
        SetUpdatedAt();
    }

    public void Reorder(int newOrder)
    {
        if (newOrder < 0) throw new DomainException("Order cannot be negative.");
        Order = newOrder;
        SetUpdatedAt();
    }
}
