using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Append-only record of a balance consumption. May reference an order/order-item for operational
/// traceability — that link NEVER changes the order's total or status.
/// </summary>
public class SvcPackageUsage : StoreEntity
{
    private SvcPackageUsage() { }
    private SvcPackageUsage(Guid tenantId) : base(tenantId) { }

    public Guid    CustomerPackageId     { get; private set; }
    public Guid    CustomerPackageItemId { get; private set; }
    public Guid    CatalogItemId         { get; private set; }
    public Guid?   OrderId               { get; private set; }
    public Guid?   OrderItemId           { get; private set; }
    public decimal Quantity              { get; private set; }
    public string? Notes                 { get; private set; }

    public static SvcPackageUsage Create(
        Guid tenantId, Guid customerPackageId, Guid customerPackageItemId, Guid catalogItemId,
        decimal quantity, Guid? orderId, Guid? orderItemId, string? notes)
    {
        if (customerPackageId == Guid.Empty)     throw new DomainException("CustomerPackageId is required.");
        if (customerPackageItemId == Guid.Empty) throw new DomainException("CustomerPackageItemId is required.");
        if (catalogItemId == Guid.Empty)         throw new DomainException("CatalogItemId is required.");
        if (quantity <= 0m)                      throw new DomainException("Quantity must be positive.");
        return new SvcPackageUsage(tenantId)
        {
            CustomerPackageId = customerPackageId, CustomerPackageItemId = customerPackageItemId,
            CatalogItemId = catalogItemId, OrderId = orderId, OrderItemId = orderItemId,
            Quantity = quantity, Notes = notes?.Trim(),
        };
    }
}
