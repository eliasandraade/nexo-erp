using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>A template line of a <see cref="SvcPackage"/>: a catalog service + included quantity. Store-scoped.</summary>
public class SvcPackageItem : StoreEntity
{
    private SvcPackageItem() { }
    private SvcPackageItem(Guid tenantId) : base(tenantId) { }

    public Guid    PackageId        { get; private set; }
    public Guid    CatalogItemId    { get; private set; }
    public string  NameSnapshot     { get; private set; } = string.Empty;
    public decimal IncludedQuantity { get; private set; }

    public static SvcPackageItem Create(Guid tenantId, Guid packageId, Guid catalogItemId, string nameSnapshot, decimal includedQuantity)
    {
        if (packageId == Guid.Empty)                 throw new DomainException("PackageId is required.");
        if (catalogItemId == Guid.Empty)             throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (includedQuantity <= 0m)                  throw new DomainException("IncludedQuantity must be positive.");
        return new SvcPackageItem(tenantId)
        {
            PackageId = packageId, CatalogItemId = catalogItemId,
            NameSnapshot = nameSnapshot.Trim(), IncludedQuantity = includedQuantity,
        };
    }

    public void UpdateQuantity(decimal includedQuantity)
    {
        if (includedQuantity <= 0m) throw new DomainException("IncludedQuantity must be positive.");
        IncludedQuantity = includedQuantity; SetUpdatedAt();
    }
}
