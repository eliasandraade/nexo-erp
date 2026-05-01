using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Staging entity para pedidos externos (portal, iFood, WhatsApp, etc.).
/// Nenhum pedido externo cria RestOrder diretamente — tudo passa por aqui.
/// RestOrder só é criado após aceite explícito do operador via AcceptAsync.
///
/// TrackingToken: gerado no Create(), UUID hex 32 chars, URL-safe.
/// OrderNumber: sequencial por store, gerado pelo serviço antes de Create().
/// </summary>
public class RestDeliveryOrder : StoreEntity
{
    private RestDeliveryOrder() { }
    private RestDeliveryOrder(Guid tenantId) : base(tenantId) { }

    // ── Identificação ────────────────────────────────────────────────────────
    public int    OrderNumber       { get; private set; }
    public string TrackingToken     { get; private set; } = string.Empty;  // UUID hex 32 chars
    public string? ExternalOrderId  { get; private set; }   // ID no sistema externo
    public string? ExternalEventType { get; private set; }  // tipo do evento externo
    public string? RawPayload       { get; private set; }   // JSON bruto do canal externo

    // ── Canal ────────────────────────────────────────────────────────────────
    public DeliveryChannel   Channel   { get; private set; }
    public DeliveryOrderType OrderType { get; private set; }

    // ── Status ───────────────────────────────────────────────────────────────
    public DeliveryOrderStatus Status          { get; private set; }
    public string?             RejectionReason { get; private set; }

    // ── Cliente (snapshot no momento do pedido) ───────────────────────────────
    public string  CustomerName         { get; private set; } = string.Empty;
    public string  CustomerPhone        { get; private set; } = string.Empty;  // dígitos normalizados
    public string? CustomerEmail        { get; private set; }
    public Guid?   CustomerId           { get; private set; }   // resolvido via find-or-create
    public string? DeliveryAddressJson  { get; private set; }   // snapshot; só para Delivery

    // ── Financeiro ───────────────────────────────────────────────────────────
    public decimal DeliveryFee { get; private set; }    // 0 para Takeaway
    public string?  CouponCode      { get; private set; }
    public decimal  DiscountAmount  { get; private set; }

    // ── Logística ────────────────────────────────────────────────────────────
    public int?    EstimatedMinutes { get; private set; }
    public string? RiderName        { get; private set; }
    public string? RiderPhone       { get; private set; }

    // ── Vínculo com operação interna ──────────────────────────────────────────
    public Guid? RestOrderId { get; private set; }   // null até AcceptAsync

    // ── Observações ───────────────────────────────────────────────────────────
    public string? Notes { get; private set; }

    // ── Timestamps ────────────────────────────────────────────────────────────
    public DateTime  ReceivedAt    { get; private set; }
    public DateTime? AcceptedAt    { get; private set; }
    public DateTime? ReadyAt       { get; private set; }
    public DateTime? DispatchedAt  { get; private set; }
    public DateTime? DeliveredAt   { get; private set; }
    public DateTime? CancelledAt   { get; private set; }

    // ── Computed ─────────────────────────────────────────────────────────────
    public decimal ItemsSubtotal => _items.Sum(i => i.LineTotal);
    public decimal Total         => ItemsSubtotal + DeliveryFee - DiscountAmount;

    private readonly List<RestDeliveryOrderItem> _items = [];
    public IReadOnlyList<RestDeliveryOrderItem> Items => _items.AsReadOnly();

    // ── Factory ──────────────────────────────────────────────────────────────
    public static RestDeliveryOrder Create(
        Guid tenantId,
        int orderNumber,
        DeliveryChannel channel,
        DeliveryOrderType orderType,
        string customerName,
        string customerPhone,
        string? customerEmail = null,
        Guid? customerId = null,
        string? deliveryAddressJson = null,
        decimal deliveryFee = 0,
        int? estimatedMinutes = null,
        string? notes = null,
        string? externalOrderId = null,
        string? externalEventType = null,
        string? rawPayload = null,
        Guid? storeId = null)            // explicit for portal (no JWT store context)
    {
        if (orderType == DeliveryOrderType.Delivery && deliveryAddressJson is null)
            throw new DomainException("Pedidos de entrega exigem endereço.");

        var order = new RestDeliveryOrder(tenantId)
        {
            OrderNumber       = orderNumber,
            TrackingToken     = Guid.NewGuid().ToString("N"),
            Channel           = channel,
            OrderType         = orderType,
            Status            = DeliveryOrderStatus.Received,
            CustomerName      = customerName.Trim(),
            CustomerPhone     = NormalizePhone(customerPhone),
            CustomerEmail     = customerEmail?.Trim().ToLowerInvariant(),
            CustomerId        = customerId,
            DeliveryAddressJson = deliveryAddressJson,
            DeliveryFee       = deliveryFee >= 0 ? deliveryFee : 0,
            EstimatedMinutes  = estimatedMinutes,
            Notes             = notes?.Trim(),
            ExternalOrderId   = externalOrderId,
            ExternalEventType = externalEventType,
            RawPayload        = rawPayload,
            ReceivedAt        = DateTime.UtcNow,
        };

        // Portal flow: no JWT store context → interceptor won't auto-inject StoreId.
        // SetStoreId is internal to Nexo.Domain, accessible here.
        if (storeId.HasValue)
            order.SetStoreId(storeId.Value);

        return order;
    }

    // ── Items ────────────────────────────────────────────────────────────────
    public RestDeliveryOrderItem AddItem(
        Guid tenantId,
        string productNameSnapshot,
        decimal unitPriceSnapshot,
        decimal quantity,
        Guid? productId = null,
        string? externalProductId = null,
        string? notes = null)
    {
        var item = RestDeliveryOrderItem.Create(
            tenantId, Id,
            productNameSnapshot, unitPriceSnapshot, quantity,
            productId, externalProductId, notes);
        _items.Add(item);
        return item;
    }

    // ── State machine ─────────────────────────────────────────────────────────
    public void Accept(Guid restOrderId)
    {
        EnsureStatus(DeliveryOrderStatus.Received, "aceitar");
        Status      = DeliveryOrderStatus.Accepted;
        RestOrderId = restOrderId;
        AcceptedAt  = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Reject(string? reason = null)
    {
        EnsureStatus(DeliveryOrderStatus.Received, "rejeitar");
        Status          = DeliveryOrderStatus.Rejected;
        RejectionReason = reason?.Trim();
        CancelledAt     = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetInPreparation()
    {
        EnsureStatus(DeliveryOrderStatus.Accepted, "marcar como em preparo");
        Status = DeliveryOrderStatus.InPreparation;
        SetUpdatedAt();
    }

    public void SetReadyForPickup()
    {
        if (Status is not (DeliveryOrderStatus.Accepted or DeliveryOrderStatus.InPreparation))
            throw new DomainException($"Não é possível marcar como pronto a partir de {Status}.");
        Status  = DeliveryOrderStatus.ReadyForPickup;
        ReadyAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetOutForDelivery()
    {
        EnsureStatus(DeliveryOrderStatus.ReadyForPickup, "despachar para entrega");
        if (OrderType != DeliveryOrderType.Delivery)
            throw new DomainException("OutForDelivery é exclusivo de pedidos de entrega.");
        Status       = DeliveryOrderStatus.OutForDelivery;
        DispatchedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetDelivered()
    {
        if (Status is not (DeliveryOrderStatus.ReadyForPickup or DeliveryOrderStatus.OutForDelivery))
            throw new DomainException($"Não é possível confirmar entrega a partir de {Status}.");
        Status      = DeliveryOrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status is DeliveryOrderStatus.Delivered or DeliveryOrderStatus.Rejected)
            throw new DomainException($"Não é possível cancelar um pedido com status {Status}.");
        Status      = DeliveryOrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>Called by the service retry loop when a concurrent insert caused a number collision.</summary>
    public void SetOrderNumber(int number) => OrderNumber = number;

    public void SetEstimatedMinutes(int minutes)
    {
        if (minutes <= 0)
            throw new DomainException("EstimatedMinutes deve ser maior que zero.");
        EstimatedMinutes = minutes;
        SetUpdatedAt();
    }

    public void AssignRider(string name, string? phone)
    {
        RiderName  = name.Trim();
        RiderPhone = phone?.Trim();
        SetUpdatedAt();
    }

    public void SetCustomer(Guid customerId)
    {
        CustomerId = customerId;
        SetUpdatedAt();
    }

    public void ApplyCoupon(string code, decimal discountAmount)
    {
        CouponCode     = code;
        DiscountAmount = discountAmount >= 0 ? discountAmount : 0;
        SetUpdatedAt();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    // Normaliza telefone: remove tudo que não for dígito
    public static string NormalizePhone(string phone)
        => new string(phone.Where(char.IsDigit).ToArray());

    private void EnsureStatus(DeliveryOrderStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException(
                $"Não é possível {action} um pedido com status {Status}. Esperado: {expected}.");
    }
}
