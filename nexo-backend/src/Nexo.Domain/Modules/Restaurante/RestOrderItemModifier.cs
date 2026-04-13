using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Snapshot of a modifier applied to an order item.
/// Snapshots prevent historical data corruption when modifier prices change after ordering.
/// </summary>
public class RestOrderItemModifier : TenantEntity
{
    private RestOrderItemModifier() { }
    private RestOrderItemModifier(Guid tenantId) : base(tenantId) { }

    public Guid    OrderItemId    { get; private set; }
    public Guid    ModifierId     { get; private set; }
    public string  LabelSnapshot  { get; private set; } = string.Empty;
    public decimal PriceSnapshot  { get; private set; }

    public static RestOrderItemModifier Create(
        Guid tenantId, Guid orderItemId, Guid modifierId,
        string labelSnapshot, decimal priceSnapshot)
        => new RestOrderItemModifier(tenantId)
        {
            OrderItemId   = orderItemId,
            ModifierId    = modifierId,
            LabelSnapshot = labelSnapshot,
            PriceSnapshot = priceSnapshot,
        };
}
