using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Item de uma comanda.
/// Guarda snapshot do preço no momento da adição.
/// Status: Pending → Preparing → Ready → Delivered | Cancelled
/// </summary>
public class RestOrderItem : TenantEntity
{
    private RestOrderItem() { }
    private RestOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid               OrderId   { get; private set; }
    public Guid               ProductId { get; private set; }
    public decimal            Quantity  { get; private set; }
    public decimal            UnitPrice { get; private set; }  // snapshot ao AddItem
    public decimal            Total     { get; private set; }  // updated by ApplyModifier
    public string?            Notes     { get; private set; }
    public RestOrderItemStatus Status   { get; private set; }

    public DateTime? SentToKitchenAt { get; private set; }
    public DateTime? PreparedAt      { get; private set; }
    public DateTime? DeliveredAt     { get; private set; }
    public DateTime? CancelledAt     { get; private set; }

    // Navigation
    public RestOrder?                        Order   { get; private set; }
    public Nexo.Domain.Entities.Product?     Product { get; private set; }

    private readonly List<RestOrderItemModifier> _modifiers = [];
    public IReadOnlyList<RestOrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RestOrderItem Create(
        Guid tenantId, Guid orderId, Guid productId,
        decimal quantity, decimal unitPrice, string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("Order item quantity must be greater than zero.");
        if (unitPrice < 0)
            throw new DomainException("Unit price cannot be negative.");

        return new RestOrderItem(tenantId)
        {
            OrderId   = orderId,
            ProductId = productId,
            Quantity  = quantity,
            UnitPrice = unitPrice,
            Total     = quantity * unitPrice,
            Notes     = notes?.Trim(),
            Status    = RestOrderItemStatus.Pending,
        };
    }

    // ── Modifier application ──────────────────────────────────────────────────

    /// <summary>
    /// Applies a modifier snapshot to this item.
    /// Updates Total: += priceAdjustment * Quantity.
    /// Called by OrderService.AddItemAsync after item is created.
    /// </summary>
    public RestOrderItemModifier ApplyModifier(
        Guid tenantId, Guid modifierId, string labelSnapshot, decimal priceAdjustment)
    {
        var modifier = RestOrderItemModifier.Create(
            tenantId, Id, modifierId, labelSnapshot, priceAdjustment);
        _modifiers.Add(modifier);
        Total += priceAdjustment * Quantity;
        SetUpdatedAt();
        return modifier;
    }

    // ── Kitchen flow ──────────────────────────────────────────────────────────

    public void SetPreparing()
    {
        if (Status != RestOrderItemStatus.Pending)
            throw new DomainException($"Item can only go to Preparing from Pending (current: {Status}).");
        Status           = RestOrderItemStatus.Preparing;
        SentToKitchenAt  = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetReady()
    {
        if (Status != RestOrderItemStatus.Preparing)
            throw new DomainException($"Item can only go to Ready from Preparing (current: {Status}).");
        Status     = RestOrderItemStatus.Ready;
        PreparedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetDelivered()
    {
        if (Status != RestOrderItemStatus.Ready)
            throw new DomainException($"Item can only be Delivered from Ready (current: {Status}).");
        Status      = RestOrderItemStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == RestOrderItemStatus.Delivered)
            throw new DomainException("Cannot cancel an already delivered item.");
        if (Status == RestOrderItemStatus.Cancelled)
            throw new DomainException("Item is already cancelled.");
        Status      = RestOrderItemStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public bool IsActive => Status != RestOrderItemStatus.Cancelled;
}
