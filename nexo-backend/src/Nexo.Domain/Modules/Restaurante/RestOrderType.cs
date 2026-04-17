namespace Nexo.Domain.Modules.Restaurante;

public enum RestOrderType
{
    DineIn,    // mesa obrigatória
    Counter,   // balcão — sem mesa
    Takeaway,  // retirada — sem mesa
    Delivery,  // entrega — sem mesa (v2)
}
