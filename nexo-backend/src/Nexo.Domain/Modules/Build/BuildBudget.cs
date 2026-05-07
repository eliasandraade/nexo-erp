using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A cost estimate for a project (or a standalone pre-sale estimate).
/// ProjectId is nullable — a budget can exist before a project is created.
/// When the client approves, Convert() links it to a project.
///
/// TotalCost and FinalPrice are computed from items (RecalculateTotals),
/// but also stored directly so they survive item edits in place.
/// </summary>
public class BuildBudget : TenantEntity
{
    private BuildBudget() { }
    private BuildBudget(Guid tenantId) : base(tenantId) { }

    public Guid?             ProjectId     { get; private set; }
    public string            Name          { get; private set; } = string.Empty;
    public BuildBudgetStatus Status        { get; private set; }
    public decimal           TotalCost     { get; private set; }
    public decimal           MarginPercent { get; private set; }
    public decimal           FinalPrice    { get; private set; }
    public Guid              CreatedBy     { get; private set; }

    // Navigation
    public ICollection<BuildBudgetItem> Items { get; private set; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildBudget Create(
        Guid    tenantId,
        Guid    createdBy,
        string  name,
        Guid?   projectId     = null,
        decimal marginPercent = 0m)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Budget name is required.");
        if (createdBy == Guid.Empty)         throw new DomainException("CreatedBy is required.");
        if (marginPercent < 0)               throw new DomainException("Margin cannot be negative.");

        return new BuildBudget(tenantId)
        {
            ProjectId     = projectId,
            Name          = name.Trim(),
            Status        = BuildBudgetStatus.Draft,
            TotalCost     = 0m,
            MarginPercent = marginPercent,
            FinalPrice    = 0m,
            CreatedBy     = createdBy,
        };
    }

    // ── Totals ────────────────────────────────────────────────────────────────

    /// <summary>Recomputes TotalCost and FinalPrice from supplied item list.</summary>
    public void RecalculateTotals(IEnumerable<BuildBudgetItem> items)
    {
        TotalCost  = items.Sum(i => i.TotalCost);
        FinalPrice = TotalCost * (1 + MarginPercent / 100m);
        SetUpdatedAt();
    }

    public void SetMargin(decimal marginPercent)
    {
        if (marginPercent < 0) throw new DomainException("Margin cannot be negative.");
        MarginPercent = marginPercent;
        FinalPrice    = TotalCost * (1 + marginPercent / 100m);
        SetUpdatedAt();
    }

    // ── State transitions ─────────────────────────────────────────────────────

    public void MarkSent()
    {
        if (Status != BuildBudgetStatus.Draft)
            throw new DomainException($"Only Draft budgets can be marked Sent. Current: {Status}.");
        Status = BuildBudgetStatus.Sent;
        SetUpdatedAt();
    }

    public void Approve()
    {
        if (Status is not (BuildBudgetStatus.Draft or BuildBudgetStatus.Sent))
            throw new DomainException($"Cannot approve budget in status '{Status}'.");
        Status = BuildBudgetStatus.Approved;
        SetUpdatedAt();
    }

    public void Reject()
    {
        if (Status is not (BuildBudgetStatus.Draft or BuildBudgetStatus.Sent))
            throw new DomainException($"Cannot reject budget in status '{Status}'.");
        Status = BuildBudgetStatus.Rejected;
        SetUpdatedAt();
    }

    /// <summary>
    /// Approved → Converted. Links to a project (which may have been created from this budget).
    /// </summary>
    public void Convert(Guid projectId)
    {
        if (Status != BuildBudgetStatus.Approved)
            throw new DomainException("Only Approved budgets can be converted to a project.");
        if (projectId == Guid.Empty)
            throw new DomainException("ProjectId is required when converting a budget.");
        ProjectId = projectId;
        Status    = BuildBudgetStatus.Converted;
        SetUpdatedAt();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Budget name is required.");
        EnsureEditable();
        Name = name.Trim();
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    public bool IsEditable =>
        Status is BuildBudgetStatus.Draft or BuildBudgetStatus.Sent;

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new DomainException($"Budget '{Name}' in status '{Status}' cannot be modified.");
    }
}
