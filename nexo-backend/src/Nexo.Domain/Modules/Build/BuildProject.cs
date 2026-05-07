using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// Aggregate root for a construction project (obra).
///
/// Financial integration rule:
///   All financial movements for this project must reference
///   ContextType = FinancialContextType.Obra and ContextId = this.Id.
///   BuildProject never owns FinancialMovement — it is read via the Core module.
///
/// State machine:
///   Planning → InProgress → Completed (terminal)
///   Planning | InProgress → Paused → InProgress
///   Planning | InProgress | Paused → Cancelled (terminal)
/// </summary>
public class BuildProject : TenantEntity
{
    private BuildProject() { }
    private BuildProject(Guid tenantId) : base(tenantId) { }

    public string              Name              { get; private set; } = string.Empty;
    public string              ClientName        { get; private set; } = string.Empty;
    public string?             Location          { get; private set; }
    public BuildProjectStatus  Status            { get; private set; }
    public BuildProjectType    Type              { get; private set; }
    public DateOnly?           StartDate         { get; private set; }
    public DateOnly?           ExpectedEndDate   { get; private set; }
    public DateOnly?           ActualEndDate     { get; private set; }
    public decimal?            BudgetEstimated   { get; private set; }
    public decimal?            BudgetApproved    { get; private set; }
    public Guid                CreatedBy         { get; private set; }

    // Navigation (loaded explicitly — no lazy loading)
    public ICollection<BuildStage>    Stages    { get; private set; } = [];
    public ICollection<BuildDailyLog> DailyLogs { get; private set; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildProject Create(
        Guid              tenantId,
        Guid              createdBy,
        string            name,
        string            clientName,
        BuildProjectType  type,
        string?           location        = null,
        DateOnly?         startDate       = null,
        DateOnly?         expectedEndDate = null,
        decimal?          budgetEstimated = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Project name is required.");
        if (string.IsNullOrWhiteSpace(clientName))
            throw new DomainException("Client name is required.");
        if (createdBy == Guid.Empty)
            throw new DomainException("CreatedBy is required.");

        return new BuildProject(tenantId)
        {
            Name            = name.Trim(),
            ClientName      = clientName.Trim(),
            Type            = type,
            Status          = BuildProjectStatus.Planning,
            Location        = location?.Trim(),
            StartDate       = startDate,
            ExpectedEndDate = expectedEndDate,
            BudgetEstimated = budgetEstimated,
            CreatedBy       = createdBy,
        };
    }

    // ── State transitions ─────────────────────────────────────────────────────

    public void Start()
    {
        EnsureNotTerminal();
        if (Status != BuildProjectStatus.Planning && Status != BuildProjectStatus.Paused)
            throw new DomainException($"Cannot start project in status '{Status}'.");
        Status = BuildProjectStatus.InProgress;
        if (StartDate is null) StartDate = DateOnly.FromDateTime(DateTime.UtcNow);
        SetUpdatedAt();
    }

    public void Pause()
    {
        if (Status != BuildProjectStatus.InProgress)
            throw new DomainException("Only in-progress projects can be paused.");
        Status = BuildProjectStatus.Paused;
        SetUpdatedAt();
    }

    public void Complete()
    {
        if (Status == BuildProjectStatus.Completed)
            throw new DomainException("Project is already completed.");
        EnsureNotTerminal();
        Status        = BuildProjectStatus.Completed;
        ActualEndDate = DateOnly.FromDateTime(DateTime.UtcNow);
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == BuildProjectStatus.Cancelled)
            throw new DomainException("Project is already cancelled.");
        if (Status == BuildProjectStatus.Completed)
            throw new DomainException("Completed projects cannot be cancelled.");
        Status = BuildProjectStatus.Cancelled;
        SetUpdatedAt();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void UpdateDetails(
        string           name,
        string           clientName,
        BuildProjectType type,
        string?          location,
        DateOnly?        startDate,
        DateOnly?        expectedEndDate,
        decimal?         budgetEstimated,
        decimal?         budgetApproved)
    {
        EnsureNotTerminal();
        if (string.IsNullOrWhiteSpace(name))       throw new DomainException("Project name is required.");
        if (string.IsNullOrWhiteSpace(clientName)) throw new DomainException("Client name is required.");

        Name            = name.Trim();
        ClientName      = clientName.Trim();
        Type            = type;
        Location        = location?.Trim();
        StartDate       = startDate;
        ExpectedEndDate = expectedEndDate;
        BudgetEstimated = budgetEstimated;
        BudgetApproved  = budgetApproved;
        SetUpdatedAt();
    }

    public void ApproveBudget(decimal amount)
    {
        if (amount < 0) throw new DomainException("Budget amount cannot be negative.");
        BudgetApproved = amount;
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    public bool IsActive    => Status is BuildProjectStatus.Planning or BuildProjectStatus.InProgress or BuildProjectStatus.Paused;
    public bool IsCompleted => Status == BuildProjectStatus.Completed;
    public bool IsCancelled => Status == BuildProjectStatus.Cancelled;

    private void EnsureNotTerminal()
    {
        if (Status is BuildProjectStatus.Completed or BuildProjectStatus.Cancelled)
            throw new DomainException($"Project '{Name}' is in terminal status '{Status}' and cannot be modified.");
    }
}
