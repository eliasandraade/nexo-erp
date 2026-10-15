using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public class RestOrder : StoreEntity
{
    private RestOrder() { }
    private RestOrder(Guid tenantId) : base(tenantId) { }

    public int             OrderNumber      { get; private set; }
    public RestOrderStatus Status           { get; private set; }
    public RestOrderType   OrderType        { get; private set; }
    public Guid?           TableId          { get; private set; }  // null for Counter/Takeaway
    public int?            PartySize        { get; private set; }
    public Guid            WaiterId         { get; private set; }
    public Guid?           CustomerId       { get; private set; }
    public Guid?           SaleId           { get; private set; }
    public decimal         CouvertAmount    { get; private set; }
    public decimal         ServiceFeeAmount { get; private set; }
    /// <summary>Taxa de entrega of the delivery order this comanda came from (0 otherwise).</summary>
    public decimal         DeliveryFeeAmount { get; private set; }
    /// <summary>Desconto (cupom) of the delivery order this comanda came from (0 otherwise).</summary>
    public decimal         DiscountAmount    { get; private set; }
    /// <summary>True when the comanda was created by accepting a delivery/portal order (quoted terms).</summary>
    public bool            FromDeliveryOrder { get; private set; }
    public string?         Notes            { get; private set; }

    public DateTime  OpenedAt    { get; private set; }
    public DateTime? ClosedAt    { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public RestTable? Table { get; private set; }

    private readonly List<RestOrderItem> _items = [];
    public IReadOnlyList<RestOrderItem> Items => _items.AsReadOnly();

    // ── Computed ──────────────────────────────────────────────────────────────
    public decimal ItemsSubtotal => _items.Where(i => i.IsActive).Sum(i => i.Total);
    /// <summary>
    /// The coupon never goes beyond what it can discount (items + entrega): cancelling items after the
    /// order was accepted can leave the frozen coupon larger than what is left.
    /// </summary>
    public decimal EffectiveDiscount => Math.Min(DiscountAmount, ItemsSubtotal + DeliveryFeeAmount);
    /// <summary>What the customer pays: items + couvert + serviço + entrega − desconto.</summary>
    public decimal Total         => ItemsSubtotal + CouvertAmount + ServiceFeeAmount + DeliveryFeeAmount - EffectiveDiscount;
    /// <summary>Everything charged on top of the items (couvert, serviço, entrega).</summary>
    public decimal SurchargesAmount => CouvertAmount + ServiceFeeAmount + DeliveryFeeAmount;

    // Keep Subtotal for backward compatibility with existing code
    public decimal Subtotal => ItemsSubtotal;

    public IReadOnlyList<RestOrderItem> ActiveItems => _items.Where(i => i.IsActive).ToList();

    // ── Factory ───────────────────────────────────────────────────────────────
    public static RestOrder Create(
        Guid tenantId, int orderNumber,
        RestOrderType orderType, Guid? tableId,
        int? partySize, Guid waiterId,
        decimal couvertAmount = 0,
        Guid? customerId = null, string? notes = null)
    {
        if (orderType == RestOrderType.DineIn && tableId is null)
            throw new DomainException("DineIn orders require a table.");

        return new RestOrder(tenantId)
        {
            OrderNumber      = orderNumber,
            Status           = RestOrderStatus.Open,
            OrderType        = orderType,
            TableId          = tableId,
            PartySize        = partySize,
            WaiterId         = waiterId,
            CustomerId       = customerId,
            CouvertAmount    = couvertAmount >= 0 ? couvertAmount : 0,
            ServiceFeeAmount = 0,
            Notes            = notes?.Trim(),
            OpenedAt         = DateTime.UtcNow,
        };
    }

    // ── Items ─────────────────────────────────────────────────────────────────
    public RestOrderItem AddItem(
        Guid tenantId, Guid productId,
        decimal quantity, decimal unitPrice, string? notes = null)
    {
        EnsureModifiable();
        var item = RestOrderItem.Create(tenantId, Id, productId, quantity, unitPrice, notes);
        _items.Add(item);
        return item;
    }

    public void CancelItem(Guid itemId)
    {
        EnsureModifiable();
        var item = _items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("OrderItem", itemId);
        item.Cancel();
    }

    // ── Couvert and service fee ───────────────────────────────────────────────
    public void SetPartySize(int partySize)
    {
        if (partySize <= 0) throw new DomainException("Party size must be greater than zero.");
        PartySize = partySize;
        SetUpdatedAt();
    }

    public void SetCouvert(decimal amount)
    {
        CouvertAmount = amount >= 0 ? amount : 0;
        SetUpdatedAt();
    }

    public void SetServiceFee(decimal amount)
    {
        ServiceFeeAmount = amount >= 0 ? amount : 0;
        SetUpdatedAt();
    }

    /// <summary>
    /// The terms the customer accepted on the delivery order (taxa de entrega, desconto do cupom): the
    /// sale must charge exactly what the customer was quoted.
    /// </summary>
    public void ApplyDeliveryTerms(decimal deliveryFee, decimal discount)
    {
        if (deliveryFee < 0 || discount < 0)
            throw new DomainException("Taxa de entrega e desconto não podem ser negativos.");
        DeliveryFeeAmount = Math.Round(deliveryFee, 2, MidpointRounding.AwayFromZero);
        DiscountAmount    = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        FromDeliveryOrder = true;
        SetUpdatedAt();
    }

    // ── State machine ─────────────────────────────────────────────────────────
    public void SetInPreparation()
    {
        if (Status != RestOrderStatus.Open)
            throw new DomainException($"Order must be Open to move to InPreparation (current: {Status}).");
        Status = RestOrderStatus.InPreparation;
        SetUpdatedAt();
    }

    public void SetReady()
    {
        if (Status is not (RestOrderStatus.Open or RestOrderStatus.InPreparation))
            throw new DomainException($"Order cannot be set to Ready from {Status}.");
        Status = RestOrderStatus.Ready;
        SetUpdatedAt();
    }

    public void Close(Guid saleId)
    {
        if (Status is RestOrderStatus.Closed or RestOrderStatus.Cancelled)
            throw new DomainException($"Cannot close an order with status {Status}.");
        if (!ActiveItems.Any())
            throw new DomainException("Cannot close an order with no active items.");
        Status   = RestOrderStatus.Closed;
        SaleId   = saleId;
        ClosedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void MarkPaid()
    {
        if (Status != RestOrderStatus.Closed)
            throw new DomainException($"Cannot mark order as Paid from status {Status}.");
        Status = RestOrderStatus.Paid;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == RestOrderStatus.Closed)
            throw new DomainException("Order is already Closed. Cancel the Sale instead.");
        if (Status == RestOrderStatus.Paid)
            throw new DomainException("Order is already Paid and cannot be cancelled.");
        if (Status == RestOrderStatus.Cancelled)
            throw new DomainException("Order is already Cancelled.");
        Status      = RestOrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public bool IsModifiable =>
        Status is RestOrderStatus.Open or RestOrderStatus.InPreparation or RestOrderStatus.Ready;

    private void EnsureModifiable()
    {
        if (!IsModifiable)
            throw new DomainException($"Order cannot be modified in status {Status}.");
    }
}
