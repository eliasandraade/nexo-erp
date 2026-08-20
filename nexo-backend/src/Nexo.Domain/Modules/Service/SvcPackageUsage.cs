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
    /// <summary>
    /// Who performed the consumed service. Optional: a package can be consumed without naming a
    /// professional, and in that case no commission is earned — there is nobody to pay.
    /// </summary>
    public Guid?   ProfessionalId        { get; private set; }
    /// <summary>
    /// Value attributed to this consumption, prorated from the customer package price at the
    /// moment it happened. The customer paid up front when the package was sold, so this is the
    /// only sensible commission base — and freezing it keeps later package edits out of it.
    /// </summary>
    public decimal? BaseAmountSnapshot   { get; private set; }
    /// <summary>Commission rate in force at consumption. Null means this usage earns none.</summary>
    public decimal? CommissionPercentSnapshot { get; private set; }

    public static SvcPackageUsage Create(
        Guid tenantId, Guid customerPackageId, Guid customerPackageItemId, Guid catalogItemId,
        decimal quantity, Guid? orderId, Guid? orderItemId, string? notes,
        Guid? professionalId = null, decimal? baseAmountSnapshot = null,
        decimal? commissionPercentSnapshot = null)
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
            ProfessionalId = professionalId, BaseAmountSnapshot = baseAmountSnapshot,
            CommissionPercentSnapshot = commissionPercentSnapshot,
        };
    }
}
