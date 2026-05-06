using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

public class ProductPurchasePrice : TenantEntity
{
    private ProductPurchasePrice() { }
    private ProductPurchasePrice(Guid tenantId) : base(tenantId) { }

    public Guid     ProductId   { get; private set; }
    public decimal  Price       { get; private set; }
    public DateOnly PurchasedAt { get; private set; }

    public static ProductPurchasePrice Create(Guid tenantId, Guid productId, decimal price, DateOnly purchasedAt)
    {
        if (price < 0)
            throw new DomainException("Purchase price cannot be negative.");
        return new ProductPurchasePrice(tenantId)
        {
            ProductId   = productId,
            Price       = price,
            PurchasedAt = purchasedAt,
        };
    }
}
