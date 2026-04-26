using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Snapshot imutável de um modificador aplicado a um item de delivery order.
/// ModifierId é nullable para suportar canais externos onde o mapeamento pode não existir.
/// </summary>
public class RestDeliveryOrderItemModifier : TenantEntity
{
    private RestDeliveryOrderItemModifier() { }
    private RestDeliveryOrderItemModifier(Guid tenantId) : base(tenantId) { }

    public Guid    DeliveryOrderItemId { get; private set; }
    public Guid?   ModifierId          { get; private set; }   // null se externo não mapeado
    public string? ExternalModifierId  { get; private set; }   // ID no sistema externo (iFood)
    public string  LabelSnapshot       { get; private set; } = string.Empty;
    public decimal PriceSnapshot       { get; private set; }

    public static RestDeliveryOrderItemModifier Create(
        Guid tenantId,
        Guid deliveryOrderItemId,
        string label,
        decimal price,
        Guid? modifierId = null,
        string? externalModifierId = null)
    {
        return new RestDeliveryOrderItemModifier(tenantId)
        {
            DeliveryOrderItemId = deliveryOrderItemId,
            ModifierId          = modifierId,
            ExternalModifierId  = externalModifierId,
            LabelSnapshot       = label.Trim(),
            PriceSnapshot       = price,
        };
    }
}
