using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Preço específico de um produto em uma lista de preços.
/// Um produto pode ter no máximo um preço por lista.
/// </summary>
public class RetPriceListItem : TenantEntity
{
    private RetPriceListItem() { }
    private RetPriceListItem(Guid tenantId) : base(tenantId) { }

    public Guid PriceListId { get; private set; }
    public Guid ProductId   { get; private set; }
    public decimal Price    { get; private set; }

    // Navigation
    public RetPriceList? PriceList { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RetPriceListItem Create(
        Guid tenantId,
        Guid priceListId,
        Guid productId,
        decimal price)
    {
        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        return new RetPriceListItem(tenantId)
        {
            PriceListId = priceListId,
            ProductId   = productId,
            Price       = price,
        };
    }

    public void UpdatePrice(decimal price)
    {
        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        Price = price;
        SetUpdatedAt();
    }
}
