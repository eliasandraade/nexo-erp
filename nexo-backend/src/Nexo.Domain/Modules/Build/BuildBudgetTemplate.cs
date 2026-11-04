using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

/// <summary>
/// A reusable budget composition (e.g. "Casa térrea padrão"). Its items carry either a quantity per m²
/// (scaled by the area when a budget is created from the template) or a fixed quantity.
/// </summary>
public class BuildBudgetTemplate : TenantEntity
{
    private BuildBudgetTemplate() { }
    private BuildBudgetTemplate(Guid tenantId) : base(tenantId) { }

    public string  Name                 { get; private set; } = string.Empty;
    public string? Description          { get; private set; }
    public decimal DefaultMarginPercent { get; private set; }
    public Guid    CreatedBy            { get; private set; }

    public static BuildBudgetTemplate Create(Guid tenantId, Guid createdBy, string name, string? description,
        decimal defaultMarginPercent)
    {
        var t = new BuildBudgetTemplate(tenantId) { CreatedBy = createdBy };
        t.Update(name, description, defaultMarginPercent);
        return t;
    }

    public void Update(string name, string? description, decimal defaultMarginPercent)
    {
        if (defaultMarginPercent is < 0m or > 1000m) throw new DomainException("A margem precisa ficar entre 0 e 1000%.");
        Name                 = BuildMoney.Required(name, 200, "O nome do modelo");
        Description          = BuildMoney.Clean(description, 2000, "A descrição");
        DefaultMarginPercent = defaultMarginPercent;
        SetUpdatedAt();
    }
}

/// <summary>
/// A line of a budget template: exactly one of <see cref="QuantityPerM2"/> (scaled by the budget area) or
/// <see cref="FixedQuantity"/>; optional stage name (becomes a project stage on conversion).
/// </summary>
public class BuildBudgetTemplateItem : TenantEntity
{
    private BuildBudgetTemplateItem() { }
    private BuildBudgetTemplateItem(Guid tenantId) : base(tenantId) { }

    public Guid          TemplateId    { get; private set; }
    public int           Order         { get; private set; }
    public string        Name          { get; private set; } = string.Empty;
    public BuildCostKind Kind          { get; private set; }
    public string        Category      { get; private set; } = string.Empty;
    public string        Unit          { get; private set; } = string.Empty;
    public decimal?      QuantityPerM2 { get; private set; }
    public decimal?      FixedQuantity { get; private set; }
    public decimal       UnitCost      { get; private set; }
    public string?       StageName     { get; private set; }

    public static BuildBudgetTemplateItem Create(Guid tenantId, Guid templateId, int order, string name,
        BuildCostKind kind, string? category, string unit, decimal? quantityPerM2, decimal? fixedQuantity,
        decimal unitCost, string? stageName)
    {
        if (templateId == Guid.Empty) throw new DomainException("Modelo inválido.");
        if (quantityPerM2.HasValue == fixedQuantity.HasValue)
            throw new DomainException("Informe a quantidade por m² ou a quantidade fixa do item (uma das duas).",
                "template_item_quantity");
        if (quantityPerM2 is <= 0m || fixedQuantity is <= 0m)
            throw new DomainException("A quantidade do item precisa ser maior que zero.");
        if (unitCost < 0m) throw new DomainException("O custo unitário não pode ser negativo.");
        var k = BuildMoney.Kind(kind);
        return new BuildBudgetTemplateItem(tenantId)
        {
            TemplateId    = templateId,
            Order         = Math.Max(0, order),
            Name          = BuildMoney.Required(name, 300, "O nome do item"),
            Kind          = k,
            Category      = BuildMoney.Clean(category, 100, "A categoria") ?? KindLabel(k),
            Unit          = BuildMoney.Required(unit, 20, "A unidade"),
            QuantityPerM2 = quantityPerM2,
            FixedQuantity = fixedQuantity,
            UnitCost      = BuildMoney.Round(unitCost),
            StageName     = BuildMoney.Clean(stageName, 200, "O nome da etapa"),
        };
    }

    /// <summary>
    /// The quantity of this line for a budget of <paramref name="areaM2"/>: the fixed quantity, or
    /// quantity per m² × area (3 decimals). A per-m² line needs an area greater than zero.
    /// </summary>
    public decimal QuantityFor(decimal? areaM2)
    {
        if (FixedQuantity is { } fixedQty) return fixedQty;
        if (areaM2 is not > 0m)
            throw new DomainException("Este modelo tem itens por m²: informe a área (m²) do orçamento.", "template_area_required");
        var q = BuildMoney.RoundQuantity(QuantityPerM2!.Value * areaM2.Value);
        return q > 0m ? q : 0.001m;
    }

    /// <summary>Default free-text category for a kind (the legacy column is required).</summary>
    public static string KindLabel(BuildCostKind kind) => kind switch
    {
        BuildCostKind.Material  => "Material",
        BuildCostKind.Labor     => "Mão de obra",
        BuildCostKind.Service   => "Serviço",
        BuildCostKind.Equipment => "Equipamento",
        _                       => "Outros",
    };
}
