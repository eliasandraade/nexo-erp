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
            throw new DomainException("MinSelections must be at least 0.");
        if (maxSelections < 1)
            throw new DomainException("MaxSelections must be at least 1.");

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
            throw new DomainException("MinSelections must be at least 0.");
        if (maxSelections < 1)
            throw new DomainException("MaxSelections must be at least 1.");
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
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
