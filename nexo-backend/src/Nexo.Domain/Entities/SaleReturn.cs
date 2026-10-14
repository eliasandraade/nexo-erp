using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// A devolução (total ou parcial) of a PAID sale. Nothing about the original sale is rewritten:
/// the return is its own record, with the items and quantities given back, how the money went back
/// to the customer and why. Its effects — stock re-entry, cash leaving the drawer or a counter-entry
/// in the financeiro — all reference it (ReferenceType "SaleReturn").
/// </summary>
public class SaleReturn : TenantEntity
{
    private SaleReturn() { }
    private SaleReturn(Guid tenantId) : base(tenantId) { }

    private readonly List<SaleReturnItem> _items = [];

    public Guid SaleId { get; private set; }
    public PaymentMethod RefundMethod { get; private set; }
    public decimal RefundAmount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    /// <summary>Client-generated key: retrying the same devolução returns it instead of refunding twice.</summary>
    public Guid? IdempotencyKey { get; private set; }
    /// <summary>Drawer the cash left from (cash refunds only).</summary>
    public Guid? CashSessionId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public IReadOnlyCollection<SaleReturnItem> Items => _items;

    public static readonly PaymentMethod[] AllowedRefundMethods =
        [PaymentMethod.Cash, PaymentMethod.Pix, PaymentMethod.Debit, PaymentMethod.Credit, PaymentMethod.Transfer];

    public static SaleReturn Create(
        Guid tenantId, Guid saleId, PaymentMethod refundMethod, decimal refundAmount,
        string reason, Guid createdByUserId, Guid? idempotencyKey, Guid? cashSessionId)
    {
        if (!AllowedRefundMethods.Contains(refundMethod))
            throw new DomainException("Forma de reembolso inválida.");
        if (refundAmount <= 0 || decimal.Round(refundAmount, 2) != refundAmount)
            throw new DomainException("O valor da devolução deve ser positivo, em centavos.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Informe o motivo da devolução.");
        if (reason.Trim().Length > 500)
            throw new DomainException("O motivo da devolução tem no máximo 500 caracteres.");
        if ((refundMethod == PaymentMethod.Cash) != cashSessionId.HasValue)
            throw new DomainException("Reembolso em dinheiro sai de um caixa; os demais, não.");

        return new SaleReturn(tenantId)
        {
            SaleId          = saleId,
            RefundMethod    = refundMethod,
            RefundAmount    = refundAmount,
            Reason          = reason.Trim(),
            CreatedByUserId = createdByUserId,
            IdempotencyKey  = idempotencyKey,
            CashSessionId   = cashSessionId,
        };
    }

    public SaleReturnItem AddItem(Guid saleItemId, Guid productId, decimal quantity, decimal amount)
    {
        var item = SaleReturnItem.Create(TenantId, Id, saleItemId, productId, quantity, amount);
        _items.Add(item);
        return item;
    }
}

public class SaleReturnItem : TenantEntity
{
    private SaleReturnItem() { }
    private SaleReturnItem(Guid tenantId) : base(tenantId) { }

    public Guid SaleReturnId { get; private set; }
    public Guid SaleItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    /// <summary>Share of the refund attributed to this line.</summary>
    public decimal Amount { get; private set; }

    internal static SaleReturnItem Create(
        Guid tenantId, Guid saleReturnId, Guid saleItemId, Guid productId, decimal quantity, decimal amount)
    {
        if (quantity <= 0)
            throw new DomainException("A quantidade devolvida deve ser positiva.");
        if (amount < 0)
            throw new DomainException("O valor do item devolvido não pode ser negativo.");

        return new SaleReturnItem(tenantId)
        {
            SaleReturnId = saleReturnId,
            SaleItemId   = saleItemId,
            ProductId    = productId,
            Quantity     = quantity,
            Amount       = amount,
        };
    }
}
