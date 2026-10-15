using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Groups modifier options for a product (e.g. "Ponto da carne", "Adicionais").
/// Tenant-scoped (not store-scoped) — modifier groups belong to the product catalog.
/// IsRequired=true means the waiter must select at least one option before adding the item.
/// MaxSelections=1 → radio; MaxSelections>1 → multi-select.
/// v1: flat price adjustment only. No conditional logic.
/// </summary>
public class ProductModifierGroup : TenantEntity
{
    private ProductModifierGroup() { }
    private ProductModifierGroup(Guid tenantId) : base(tenantId) { }

    public Guid   ProductId     { get; private set; }
    public string Name          { get; private set; } = string.Empty;
    public bool   IsRequired    { get; private set; }
    public short  MinSelections { get; private set; }
    public short  MaxSelections { get; private set; }
    public short  SortOrder     { get; private set; }
    public bool   IsActive      { get; private set; }

    private readonly List<ProductModifier> _modifiers = [];
    public IReadOnlyList<ProductModifier> Modifiers => _modifiers.AsReadOnly();

    public static ProductModifierGroup Create(
        Guid tenantId, Guid productId, string name,
        bool isRequired = false, short minSelections = 0, short maxSelections = 1, short sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier group name is required.");
        if (minSelections < 0)
            throw new DomainException("O mínimo de escolhas não pode ser negativo.");
        if (maxSelections < 1)
            throw new DomainException("O máximo de escolhas precisa ser ao menos 1.");
        if (minSelections > maxSelections)
            throw new DomainException("O mínimo de escolhas não pode passar do máximo.");

        return new ProductModifierGroup(tenantId)
        {
            ProductId     = productId,
            Name          = name.Trim(),
            IsRequired    = isRequired,
            MinSelections = minSelections,
            MaxSelections = maxSelections,
            SortOrder     = sortOrder,
            IsActive      = true,
        };
    }

    public void Update(string name, bool isRequired, short minSelections, short maxSelections, short sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier group name is required.");
        if (minSelections < 0)
            throw new DomainException("O mínimo de escolhas não pode ser negativo.");
        if (maxSelections < 1)
            throw new DomainException("O máximo de escolhas precisa ser ao menos 1.");
        if (minSelections > maxSelections)
            throw new DomainException("O mínimo de escolhas não pode passar do máximo.");
        Name          = name.Trim();
        IsRequired    = isRequired;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        SortOrder     = sortOrder;
        SetUpdatedAt();
    }

    public void AddModifier(ProductModifier modifier)
    {
        _modifiers.Add(modifier);
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    /// <summary>Removes the group from the product, with its options (old orders keep their snapshots).</summary>
    public void Deactivate()
    {
        IsActive = false;
        foreach (var m in Modifiers) m.Deactivate();
        SetUpdatedAt();
    }
}
