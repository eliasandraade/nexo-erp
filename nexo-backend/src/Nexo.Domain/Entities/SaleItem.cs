using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

public class SaleItem : TenantEntity
{
    private SaleItem() { }
    private SaleItem(Guid tenantId) : base(tenantId) { }

    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }   // snapshot: sale price at time of sale
    public decimal CostPrice { get; private set; }   // snapshot: cost price at time of sale
    public decimal DiscountAmount { get; private set; }
    public decimal Total { get; private set; }        // Quantity * UnitPrice - DiscountAmount
    public string? Notes { get; private set; }

    // Navigation
    public Sale? Sale { get; private set; }
    public Product? Product { get; private set; }

    public static SaleItem Create(
        Guid tenantId,
        Guid saleId,
        Guid productId,
        decimal quantity,
        decimal unitPrice,
        decimal costPrice,
        decimal discountAmount = 0,
        string? notes = null)
    {
        // A sale line can never reduce revenue below zero or put stock back: returns do not go
        // through a negative sale line (there is no return flow yet).
        if (quantity <= 0m)     throw new DomainException("Item quantity must be positive.");
        if (unitPrice < 0m)     throw new DomainException("Item unit price cannot be negative.");
        if (discountAmount < 0m) throw new DomainException("Item discount cannot be negative.");
        var total = quantity * unitPrice - discountAmount;
        if (total < 0m)         throw new DomainException("Item discount cannot exceed the item total.");
        return new SaleItem(tenantId)
        {
            SaleId         = saleId,
            ProductId      = productId,
            Quantity       = quantity,
            UnitPrice      = unitPrice,
            CostPrice      = costPrice,
            DiscountAmount = discountAmount,
            Total          = total,
            Notes          = notes?.Trim(),
        };
    }

    public void UpdateQuantity(decimal quantity)
    {
        Quantity = quantity;
        Total    = Quantity * UnitPrice - DiscountAmount;
        SetUpdatedAt();
    }

    public void UpdateDiscount(decimal discountAmount)
    {
        DiscountAmount = discountAmount;
        Total          = Quantity * UnitPrice - DiscountAmount;
        SetUpdatedAt();
    }
}
