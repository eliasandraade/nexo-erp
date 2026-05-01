namespace Nexo.Application.Modules.Restaurante;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateDeliveryOrderRequest(
    string                         Channel,        // DeliveryChannel enum string
    string                         OrderType,      // "Delivery" | "Takeaway"
    string                         CustomerName,
    string                         CustomerPhone,
    string?                        CustomerEmail   = null,
    Guid?                          CustomerId      = null,
    string?                        DeliveryAddressJson = null,
    decimal                        DeliveryFee     = 0,
    int?                           EstimatedMinutes = null,
    string?                        Notes           = null,
    string?                        ExternalOrderId = null,
    string?                        ExternalEventType = null,
    string?                        RawPayload      = null,
    List<CreateDeliveryOrderItemRequest>? Items    = null);

public record CreateDeliveryOrderItemRequest(
    Guid?   ProductId,
    string  ProductName,
    decimal UnitPrice,
    decimal Quantity,
    string? Notes = null,
    List<CreateDeliveryOrderItemModifierRequest>? Modifiers = null);

public record CreateDeliveryOrderItemModifierRequest(
    string  Label,
    decimal Price,
    Guid?   ModifierId = null);

public record AcceptDeliveryOrderRequest(
    int? EstimatedMinutes = null);

public record RejectDeliveryOrderRequest(string? Reason = null);

public record UpdateDeliveryStatusRequest(
    string  Status,        // "OutForDelivery" | "Delivered"
    string? RiderName  = null,
    string? RiderPhone = null);

public record AssignRiderRequest(string Name, string? Phone = null);

// ── Manual order (operator-facing, authenticated) ────────────────────────────
// Products resolved from catalog — name+price snapshot taken from DB, not from client.
// Channel must be a manual one: PhoneCall | InPerson | WhatsApp | Other.

public record CreateManualOrderRequest(
    string  OrderType,              // "Delivery" | "Takeaway"
    string  CustomerName,
    string  CustomerPhone,
    string? CustomerEmail        = null,
    Guid?   CustomerId           = null,
    string? DeliveryAddressJson  = null,
    decimal DeliveryFee          = 0,
    int?    EstimatedMinutes     = null,
    string? Notes                = null,
    string  Channel              = "PhoneCall",
    List<CreateManualOrderItemRequest>? Items = null);

public record CreateManualOrderItemRequest(
    Guid    ProductId,
    decimal Quantity,
    string? Notes     = null,
    List<CreateManualOrderItemModifierRequest>? Modifiers = null);

public record CreateManualOrderItemModifierRequest(Guid ModifierId);

// ── Portal order (public-facing, unauthenticated) ────────────────────────────
// Store resolved from PublicSlug. Products validated as IsMenuVisible=true.
// Prices taken from catalog — never trusted from client.

public record CreatePortalOrderRequest(
    string  PublicSlug,
    string  OrderType,
    string  CustomerName,
    string  CustomerPhone,
    string? CustomerEmail        = null,
    string? DeliveryAddressJson  = null,
    int?    EstimatedMinutes     = null,
    string? Notes                = null,
    Guid?   DeliveryZoneId       = null,
    string? CouponCode           = null,
    List<CreatePortalOrderItemRequest>? Items = null);

public record CreatePortalOrderItemRequest(
    Guid    ProductId,
    decimal Quantity,
    string? Notes     = null,
    List<CreatePortalOrderItemModifierRequest>? Modifiers = null);

public record CreatePortalOrderItemModifierRequest(Guid ModifierId);

// ── Responses ───────────────────────────────────────────────────────────────

public record DeliveryOrderItemModifierDto(
    Guid?   ModifierId,
    string  Label,
    decimal Price);

public record DeliveryOrderItemDto(
    Guid    Id,
    Guid?   ProductId,
    string  ProductName,
    decimal UnitPrice,
    decimal Quantity,
    decimal LineTotal,
    string? Notes,
    IReadOnlyList<DeliveryOrderItemModifierDto> Modifiers);

public record DeliveryOrderDto(
    Guid     Id,
    int      OrderNumber,
    string   TrackingToken,
    string   Channel,
    string   OrderType,
    string   Status,
    string?  RejectionReason,
    // cliente
    string   CustomerName,
    string   CustomerPhone,
    string?  CustomerEmail,
    Guid?    CustomerId,
    string?  DeliveryAddressJson,
    // financeiro
    decimal  DeliveryFee,
    decimal  ItemsSubtotal,
    decimal  DiscountAmount,
    decimal  Total,
    string?  CouponCode,
    // logística
    int?     EstimatedMinutes,
    string?  RiderName,
    string?  RiderPhone,
    // vínculos
    Guid?    RestOrderId,
    string?  ExternalOrderId,
    // observações
    string?  Notes,
    // timestamps
    DateTime  ReceivedAt,
    DateTime? AcceptedAt,
    DateTime? ReadyAt,
    DateTime? DispatchedAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt,
    IReadOnlyList<DeliveryOrderItemDto> Items);

/// <summary>Payload mínimo para o cliente rastrear o pedido (endpoint público).</summary>
public record DeliveryOrderTrackingDto(
    int      OrderNumber,
    string   Status,
    string   StatusLabel,
    int?     EstimatedMinutes,
    string   OrderType);
