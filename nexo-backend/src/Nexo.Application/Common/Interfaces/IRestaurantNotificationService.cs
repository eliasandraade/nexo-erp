namespace Nexo.Application.Common.Interfaces;

public interface IRestaurantNotificationService
{
    Task OrderItemStatusChangedAsync(Guid orderId, Guid itemId, string newStatus, CancellationToken ct = default);
    Task NewItemAddedAsync(Guid orderId, object itemDto, CancellationToken ct = default);
    Task OrderStatusChangedAsync(Guid orderId, string newStatus, CancellationToken ct = default);
    Task TableStatusChangedAsync(Guid tableId, string newStatus, CancellationToken ct = default);
}
