namespace Nexo.Domain.Modules.Restaurante;

public enum RestOrderStatus
{
    Open,
    InPreparation,
    Ready,
    Closed,     // conta gerada (Sale em Draft) — mesa ainda Occupied
    Paid,       // pagamento confirmado — mesa liberada (Available)
    Cancelled,
}
