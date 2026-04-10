using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Immutable record of a single payment line on a confirmed sale.
/// A sale can have multiple payments (mixed à vista + a prazo).
///
/// PaymentType.Cash   → generates CashMovement (immediate)
/// PaymentType.Credit → generates FinancialTransaction/Receivable (future)
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

    // Navigation
    public Sale? Sale { get; private set; }

    public static SalePayment Create(
        Guid tenantId,
        Guid saleId,
        PaymentMethod method,
        PaymentType type,
        decimal amount,
        DateTime? dueDate = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be positive.", nameof(amount));

        if (type == PaymentType.Credit && dueDate is null)
            throw new ArgumentException("DueDate is required for credit payments.", nameof(dueDate));

        return new SalePayment(tenantId)
        {
            SaleId  = saleId,
            Method  = method,
            Type    = type,
            Amount  = amount,
            DueDate = dueDate,
        };
    }
}
