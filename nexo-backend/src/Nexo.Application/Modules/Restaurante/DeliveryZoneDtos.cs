namespace Nexo.Application.Modules.Restaurante;

public record DeliveryZoneDto(Guid Id, string Neighborhood, decimal Fee);

public record UpsertDeliveryZonesRequest(List<UpsertDeliveryZoneItem> Zones);

public record UpsertDeliveryZoneItem(string Neighborhood, decimal Fee);
