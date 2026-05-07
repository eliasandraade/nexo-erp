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

    public void UpdateProgress(int percent)
    {
        if (percent < 0 || percent > 100)
            throw new DomainException("Progress must be between 0 and 100.");
        ProgressPercent = percent;
        // Auto-transition: 100% → Completed if not already
        if (percent == 100 && Status != BuildStageStatus.Completed)
            Status = BuildStageStatus.Completed;
        SetUpdatedAt();
    }

    public void Reorder(int newOrder)
    {
        if (newOrder < 0) throw new DomainException("Order cannot be negative.");
        Order = newOrder;
        SetUpdatedAt();
    }
}
