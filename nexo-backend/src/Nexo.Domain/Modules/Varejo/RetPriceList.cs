using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Lista de preços por tenant.
/// Permite preços diferenciados por cliente, tipo de cliente ou canal de venda.
///
/// Resolução de preço no PDV:
///   1. Lista vinculada ao cliente
///   2. Lista padrão do tenant (IsDefault = true)
///   3. Fallback: product.SalePrice
/// </summary>
public class RetPriceList : TenantEntity
{
    private RetPriceList() { }
    private RetPriceList(Guid tenantId) : base(tenantId) { }

    public string Name        { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>
    /// Indica que esta é a lista padrão do tenant.
    /// Só pode existir uma por tenant. Aplicada quando o cliente não tem lista vinculada.
    /// </summary>
    public bool IsDefault  { get; private set; }
    public bool IsActive   { get; private set; }

    // Navigation
    private readonly List<RetPriceListItem> _items = [];
    public IReadOnlyList<RetPriceListItem> Items => _items.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RetPriceList Create(
        Guid tenantId,
        string name,
        string? description = null,
        bool isDefault = false)
    {
        return new RetPriceList(tenantId)
        {
            Name        = name.Trim(),
            Description = description?.Trim(),
            IsDefault   = isDefault,
            IsActive    = true,
        };
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    public void Update(string name, string? description)
    {
        Name        = name.Trim();
        Description = description?.Trim();
        SetUpdatedAt();
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        SetUpdatedAt();
    }

    public void UnsetDefault()
    {
        IsDefault = false;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    // ── Items ─────────────────────────────────────────────────────────────────

    public RetPriceListItem SetProductPrice(Guid tenantId, Guid productId, decimal price)
    {
        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        var existing = _items.FirstOrDefault(x => x.ProductId == productId);
        if (existing is not null)
        {
            existing.UpdatePrice(price);
            return existing;
        }

        var item = RetPriceListItem.Create(tenantId, Id, productId, price);
        _items.Add(item);
        return item;
    }

    public void RemoveProductPrice(Guid productId)
    {
        var item = _items.FirstOrDefault(x => x.ProductId == productId);
        if (item is not null)
            _items.Remove(item);
    }
}
