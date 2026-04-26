namespace Nexo.Domain.Modules.Restaurante;

public enum DeliveryOrderStatus
{
    Received,        // chegou; aguarda triagem do operador
    Accepted,        // aceito; RestOrder criado
    InPreparation,   // cozinha preparando
    ReadyForPickup,  // pronto — Takeaway: cliente vem; Delivery: aguarda motoboy
    OutForDelivery,  // saiu para entrega [Delivery only]
    Delivered,       // entregue ou retirado com sucesso
    Rejected,        // recusado pelo operador (RestOrder nunca criado)
    Cancelled,       // cancelado após aceite (RestOrder.CancelAsync chamado)
}
