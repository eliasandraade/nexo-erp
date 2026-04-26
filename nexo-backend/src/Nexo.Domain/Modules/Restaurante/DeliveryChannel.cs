namespace Nexo.Domain.Modules.Restaurante;

public enum DeliveryChannel
{
    Portal,      // portal próprio do restaurante
    IFood,       // iFood (manual no MVP; webhook estruturado)
    Rappi,       // Rappi
    Anotaai,     // AnotaAí
    WhatsApp,    // WhatsApp (entrada manual)
    PhoneCall,   // ligação telefônica (entrada manual)
    InPerson,    // presencial no balcão de delivery
    Other,       // outros canais
}
