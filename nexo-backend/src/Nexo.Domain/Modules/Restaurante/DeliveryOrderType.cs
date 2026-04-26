namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Tipo do pedido de delivery. Distinto de RestOrderType (que inclui DineIn e Counter).
/// Mapeado para RestOrderType em AcceptAsync.
/// </summary>
public enum DeliveryOrderType
{
    Delivery,  // entrega no endereço
    Takeaway,  // retirada no local
}
