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
    /// <summary>Material / labor / service / equipment / other. Legacy rows default to Other.</summary>
    public BuildCostKind Kind { get; private set; } = BuildCostKind.Other;
    /// <summary>
    /// Stage name for budgets not yet linked to a project (or coming from a template). On conversion, a project
    /// without stages gets one stage per distinct name.
    /// </summary>
    public string? StageName { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static BuildBudgetItem Create(
        Guid    tenantId,
        Guid    budgetId,
        string  name,
        string  category,
        decimal quantity,
        string  unit,
        decimal unitCost,
        Guid?   stageId = null,
        BuildCostKind kind = BuildCostKind.Other,
        string? stageName = null)
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
            TotalCost = BuildMoney.Round(quantity * unitCost),
            Kind      = BuildMoney.Kind(kind),
            StageName = BuildMoney.Clean(stageName, 200, "O nome da etapa"),
        };
    }

    /// <summary>The same line in another budget (duplicate / new version / save as template round trip).</summary>
    public BuildBudgetItem CopyTo(Guid budgetId)
        => Create(TenantId, budgetId, Name, Category, Quantity, Unit, UnitCost, StageId, Kind, StageName);

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>On conversion, a line with a stage name is linked to the project stage created from that name.</summary>
    public void LinkStage(Guid stageId)
    {
        if (stageId == Guid.Empty) throw new DomainException("Etapa inválida.");
        StageId = stageId;
        SetUpdatedAt();
    }

    public void Update(
        string  name,
        string  category,
        decimal quantity,
        string  unit,
        decimal unitCost,
        Guid?   stageId,
        BuildCostKind? kind = null,
        string? stageName = null)
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
        TotalCost = BuildMoney.Round(quantity * unitCost);
        StageId   = stageId;
        if (kind is { } k) Kind = BuildMoney.Kind(k);
        // null keeps the stage name (older clients do not send it); an empty string clears it
        if (stageName is not null) StageName = BuildMoney.Clean(stageName, 200, "O nome da etapa");
        SetUpdatedAt();
    }
}
