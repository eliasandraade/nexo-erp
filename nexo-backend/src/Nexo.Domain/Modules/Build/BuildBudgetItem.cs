using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A line item in a BuildBudget.
/// TotalCost = Quantity × UnitCost (always recomputed on update).
/// StageId is optional — allows grouping items by stage.
/// </summary>
public class BuildBudgetItem : TenantEntity
{
    private BuildBudgetItem() { }
    private BuildBudgetItem(Guid tenantId) : base(tenantId) { }

    public Guid    BudgetId { get; private set; }
    public Guid?   StageId  { get; private set; }
    public string  Name     { get; private set; } = string.Empty;
    public string  Category { get; private set; } = string.Empty; // free text: "Material", "Mão de obra", "Equipamento"
    public decimal Quantity { get; private set; }
    public string  Unit     { get; private set; } = string.Empty; // "m²", "un", "kg", "h"
    public decimal UnitCost { get; private set; }
    public decimal TotalCost { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildBudgetItem Create(
        Guid    tenantId,
        Guid    budgetId,
        string  name,
        string  category,
        decimal quantity,
        string  unit,
        decimal unitCost,
        Guid?   stageId = null)
    {
        if (budgetId == Guid.Empty)              throw new DomainException("BudgetId is required.");
        if (string.IsNullOrWhiteSpace(name))     throw new DomainException("Item name is required.");
        if (string.IsNullOrWhiteSpace(category)) throw new DomainException("Category is required.");
        if (string.IsNullOrWhiteSpace(unit))     throw new DomainException("Unit is required.");
        if (quantity <= 0)                       throw new DomainException("Quantity must be positive.");
        if (unitCost < 0)                        throw new DomainException("Unit cost cannot be negative.");

        return new BuildBudgetItem(tenantId)
        {
            BudgetId  = budgetId,
            StageId   = stageId,
            Name      = name.Trim(),
            Category  = category.Trim(),
            Quantity  = quantity,
            Unit      = unit.Trim(),
            UnitCost  = unitCost,
            TotalCost = quantity * unitCost,
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void Update(
        string  name,
        string  category,
        decimal quantity,
        string  unit,
        decimal unitCost,
        Guid?   stageId)
    {
        if (string.IsNullOrWhiteSpace(name))     throw new DomainException("Item name is required.");
        if (string.IsNullOrWhiteSpace(category)) throw new DomainException("Category is required.");
        if (string.IsNullOrWhiteSpace(unit))     throw new DomainException("Unit is required.");
        if (quantity <= 0)                       throw new DomainException("Quantity must be positive.");
        if (unitCost < 0)                        throw new DomainException("Unit cost cannot be negative.");

        Name      = name.Trim();
        Category  = category.Trim();
        Quantity  = quantity;
        Unit      = unit.Trim();
        UnitCost  = unitCost;
        TotalCost = quantity * unitCost;
        StageId   = stageId;
        SetUpdatedAt();
    }
}
