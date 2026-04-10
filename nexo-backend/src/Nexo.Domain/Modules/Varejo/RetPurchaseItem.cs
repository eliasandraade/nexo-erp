using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Item de uma nota de compra.
/// Imutável após confirmação da compra pai.
/// </summary>
public class RetPurchaseItem : TenantEntity
{
    private RetPurchaseItem() { }
    private RetPurchaseItem(Guid tenantId) : base(tenantId) { }

    public Guid PurchaseId { get; private set; }
    public Guid ProductId  { get; private set; }

    public decimal Quantity  { get; private set; }
    public decimal UnitCost  { get; private set; }   // custo unitário de compra
    public decimal Total     { get; private set; }   // Quantity × UnitCost
    public string? Notes     { get; private set; }

    // Navigation
    public RetPurchase? Purchase { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RetPurchaseItem Create(
        Guid tenantId,
        Guid purchaseId,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("Purchase item quantity must be greater than zero.");

        if (unitCost < 0)
            throw new DomainException("Unit cost cannot be negative.");

        return new RetPurchaseItem(tenantId)
        {
            PurchaseId = purchaseId,
            ProductId  = productId,
            Quantity   = quantity,
            UnitCost   = unitCost,
            Total      = quantity * unitCost,
            Notes      = notes?.Trim(),
        };
    }
}
