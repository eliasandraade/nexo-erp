using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Immutable record of a single payment line on a confirmed sale.
/// A sale can have multiple payments (mixed à vista + a prazo).
///
/// PaymentType.Credit (a prazo) → pending FinancialTransaction/Receivable.
/// PaymentType.Cash   (à vista)  → by method: only PaymentMethod.Cash (physical money) moves the
///                                 cash drawer (CashMovement); Pix/cards/transfer are recognised in
///                                 the financeiro as settled revenue and never touch the drawer.
///
/// <see cref="Amount"/> is always the value APPLIED to the sale. For physical cash the customer may
/// hand over more: <see cref="TenderedAmount"/> is what was handed over and <see cref="ChangeAmount"/>
/// the troco given back (Tendered = Amount + Change).
/// </summary>
public class SalePayment : TenantEntity
{
    private SalePayment() { }
    private SalePayment(Guid tenantId) : base(tenantId) { }

    public Guid SaleId { get; private set; }
    public PaymentMethod Method { get; private set; }   // como paga (Cash, Pix, Credit card…)
    public PaymentType Type { get; private set; }       // quando entra (now vs future)
    public decimal Amount { get; private set; }
    public DateTime? DueDate { get; private set; }       // required when Type = Credit
    public decimal? TenderedAmount { get; private set; } // physical cash handed over (cash only)
    public decimal ChangeAmount { get; private set; }    // troco given back (cash only)

    /// <summary>True when this line is physical money that enters the cash drawer.</summary>
    public bool MovesCashDrawer => Method == PaymentMethod.Cash && Type == PaymentType.Cash;

    // Navigation
    public Sale? Sale { get; private set; }

    public static SalePayment Create(
        Guid tenantId,
        Guid saleId,
        PaymentMethod method,
        PaymentType type,
        decimal amount,
        DateTime? dueDate = null,
        decimal changeAmount = 0)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be positive.", nameof(amount));

        if (type == PaymentType.Credit && dueDate is null)
            throw new ArgumentException("DueDate is required for credit payments.", nameof(dueDate));

        if (changeAmount < 0)
            throw new ArgumentException("Change cannot be negative.", nameof(changeAmount));

        var isPhysicalCash = method == PaymentMethod.Cash && type == PaymentType.Cash;
        if (changeAmount > 0 && !isPhysicalCash)
            throw new ArgumentException("Only physical cash payments can give change.", nameof(changeAmount));

        return new SalePayment(tenantId)
        {
            SaleId         = saleId,
            Method         = method,
            Type           = type,
            Amount         = amount,
            DueDate        = dueDate,
            TenderedAmount = isPhysicalCash ? amount + changeAmount : null,
            ChangeAmount   = changeAmount,
        };
    }
}
