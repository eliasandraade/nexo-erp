using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>A consumable balance line of a <see cref="SvcCustomerPackage"/>. RemainingQuantity never goes negative.</summary>
public class SvcCustomerPackageItem : StoreEntity
{
    private SvcCustomerPackageItem() { }
    private SvcCustomerPackageItem(Guid tenantId) : base(tenantId) { }

    public Guid    CustomerPackageId { get; private set; }
    public Guid    CatalogItemId     { get; private set; }
    public string  NameSnapshot      { get; private set; } = string.Empty;
    public decimal TotalQuantity     { get; private set; }
    public decimal RemainingQuantity { get; private set; }

    public static SvcCustomerPackageItem Create(Guid tenantId, Guid customerPackageId, Guid catalogItemId, string nameSnapshot, decimal totalQuantity)
    {
        if (customerPackageId == Guid.Empty)         throw new DomainException("CustomerPackageId is required.");
        if (catalogItemId == Guid.Empty)             throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (totalQuantity <= 0m)                     throw new DomainException("TotalQuantity must be positive.");
        return new SvcCustomerPackageItem(tenantId)
        {
            CustomerPackageId = customerPackageId, CatalogItemId = catalogItemId,
            NameSnapshot = nameSnapshot.Trim(), TotalQuantity = totalQuantity, RemainingQuantity = totalQuantity,
        };
    }

    public void Consume(decimal quantity)
    {
        if (quantity <= 0m)                throw new DomainException("Quantity must be positive.");
        if (quantity > RemainingQuantity)  throw new DomainException("Insufficient package balance.");
        RemainingQuantity -= quantity; SetUpdatedAt();
    }
}
