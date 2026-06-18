using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A line item of a <see cref="SvcOrder"/>. Store-scoped. Name/Description/UnitPrice/Commission
/// are snapshots copied from the catalog at add time and never change with later catalog edits;
/// only Quantity and the executing Professional are mutable. TotalAmount = Quantity × UnitPriceSnapshot.
/// </summary>
public class SvcOrderItem : StoreEntity
{
    private SvcOrderItem() { }                                   // EF Core
    private SvcOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid     OrderId                   { get; private set; }
    public Guid     CatalogItemId             { get; private set; }
    public Guid?    ProfessionalId            { get; private set; }
    public string   NameSnapshot              { get; private set; } = string.Empty;
    public string?  DescriptionSnapshot       { get; private set; }
    public decimal  Quantity                  { get; private set; }
    public decimal  UnitPriceSnapshot         { get; private set; }
    public decimal? CommissionPercentSnapshot { get; private set; }
    public decimal  TotalAmount               { get; private set; }

    public static SvcOrderItem Create(
        Guid tenantId, Guid orderId, Guid catalogItemId, Guid? professionalId,
        string nameSnapshot, string? descriptionSnapshot, decimal quantity,
        decimal unitPriceSnapshot, decimal? commissionPercentSnapshot)
    {
        if (orderId == Guid.Empty)                   throw new DomainException("OrderId is required.");
        if (catalogItemId == Guid.Empty)             throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (quantity <= 0m)                          throw new DomainException("Quantity must be positive.");
        if (unitPriceSnapshot < 0m)                  throw new DomainException("Unit price cannot be negative.");

        return new SvcOrderItem(tenantId)
        {
            OrderId                   = orderId,
            CatalogItemId             = catalogItemId,
            ProfessionalId            = professionalId,
            NameSnapshot              = nameSnapshot.Trim(),
            DescriptionSnapshot       = descriptionSnapshot?.Trim(),
            Quantity                  = quantity,
            UnitPriceSnapshot         = unitPriceSnapshot,
            CommissionPercentSnapshot = commissionPercentSnapshot,
            TotalAmount               = quantity * unitPriceSnapshot,
        };
    }

    /// <summary>Updates the quantity and executing professional. Price snapshot is immutable.</summary>
    public void Update(decimal quantity, Guid? professionalId)
    {
        if (quantity <= 0m) throw new DomainException("Quantity must be positive.");
        Quantity       = quantity;
        ProfessionalId = professionalId;
        TotalAmount    = quantity * UnitPriceSnapshot;
        SetUpdatedAt();
    }
}
