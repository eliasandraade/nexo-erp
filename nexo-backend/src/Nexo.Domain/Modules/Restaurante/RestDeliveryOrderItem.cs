using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Item de um pedido externo.
/// ProductId é nullable para suportar canais externos onde o produto ainda não foi mapeado.
/// Snapshots (nome, preço) são sempre obrigatórios — são a fonte de verdade histórica.
/// </summary>
public class RestDeliveryOrderItem : TenantEntity
{
    private RestDeliveryOrderItem() { }
    private RestDeliveryOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid    DeliveryOrderId      { get; private set; }
    public Guid?   ProductId            { get; private set; }   // null = produto externo não mapeado
    public string? ExternalProductId    { get; private set; }   // ID no sistema externo (iFood)
    public string  ProductNameSnapshot  { get; private set; } = string.Empty;
    public decimal UnitPriceSnapshot    { get; private set; }
    public decimal Quantity             { get; private set; }
    public string? Notes                { get; private set; }

    public decimal LineTotal => UnitPriceSnapshot * Quantity;

    private readonly List<RestDeliveryOrderItemModifier> _modifiers = [];
    public IReadOnlyList<RestDeliveryOrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    public static RestDeliveryOrderItem Create(
        Guid tenantId,
        Guid deliveryOrderId,
        string productNameSnapshot,
        decimal unitPriceSnapshot,
        decimal quantity,
        Guid? productId = null,
        string? externalProductId = null,
        string? notes = null)
    {
        return new RestDeliveryOrderItem(tenantId)
        {
            DeliveryOrderId     = deliveryOrderId,
            ProductId           = productId,
            ExternalProductId   = externalProductId,
            ProductNameSnapshot = productNameSnapshot.Trim(),
            UnitPriceSnapshot   = unitPriceSnapshot,
            Quantity            = quantity,
            Notes               = notes?.Trim(),
        };
    }

    public RestDeliveryOrderItemModifier AddModifier(
        Guid tenantId, string label, decimal price,
        Guid? modifierId = null, string? externalModifierId = null)
    {
        var modifier = RestDeliveryOrderItemModifier.Create(
            tenantId, Id, label, price, modifierId, externalModifierId);
        _modifiers.Add(modifier);
        return modifier;
    }
}
