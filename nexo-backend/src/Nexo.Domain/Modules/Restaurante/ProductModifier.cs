using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// A single option within a modifier group (e.g. "Ao ponto", "Extra queijo").
/// PriceAdjustment is a flat delta: +2.50, 0, or -1.00. No conditional logic.
/// </summary>
public class ProductModifier : TenantEntity
{
    private ProductModifier() { }
    private ProductModifier(Guid tenantId) : base(tenantId) { }

    public Guid    GroupId         { get; private set; }
    public string  Name            { get; private set; } = string.Empty;
    public decimal PriceAdjustment { get; private set; }
    public short   SortOrder       { get; private set; }
    public bool    IsActive        { get; private set; }

    // Navigation
    public ProductModifierGroup? Group { get; private set; }

    public static ProductModifier Create(
        Guid tenantId, Guid groupId, string name,
        decimal priceAdjustment = 0, short sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier name is required.");

        return new ProductModifier(tenantId)
        {
            GroupId         = groupId,
            Name            = name.Trim(),
            PriceAdjustment = priceAdjustment,
            SortOrder       = sortOrder,
            IsActive        = true,
        };
    }

    public void Update(string name, decimal priceAdjustment, short sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier name is required.");
        Name            = name.Trim();
        PriceAdjustment = priceAdjustment;
        SortOrder       = sortOrder;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
