using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// Wraps IHubContext to emit restaurant events to the store group.
/// All methods are fire-and-forget after DB commit — failures are logged, never rethrown.
/// SignalR is a UX complement only; the frontend must implement polling fallback.
/// Group name: store:{tenantId}:{storeId}
/// </summary>
public class RestaurantNotificationService : IRestaurantNotificationService
{
    private readonly IHubContext<RestaurantHub> _hub;
    private readonly ICurrentTenant            _currentTenant;
    private readonly ICurrentStore             _currentStore;
    private readonly ILogger<RestaurantNotificationService> _logger;

    public RestaurantNotificationService(
        IHubContext<RestaurantHub> hub,
        ICurrentTenant currentTenant,
        ICurrentStore currentStore,
        ILogger<RestaurantNotificationService> logger)
    {
        _hub           = hub;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
        _logger        = logger;
    }

    private string GroupName => RestaurantHub.GroupFor(_currentTenant.Id, _currentStore.Id);

    public Task OrderItemStatusChangedAsync(Guid orderId, Guid itemId, string newStatus, CancellationToken ct = default)
    {
        _logger.LogDebug("SignalR → OrderItemStatusChanged order={OrderId} item={ItemId} status={Status}",
            orderId, itemId, newStatus);
        return _hub.Clients.Group(GroupName)
            .SendAsync("OrderItemStatusChanged", orderId.ToString(), itemId.ToString(), newStatus, ct);
    }

    public Task NewItemAddedAsync(Guid orderId, object itemDto, CancellationToken ct = default)
    {
        _logger.LogDebug("SignalR → NewItemAdded order={OrderId}", orderId);
        return _hub.Clients.Group(GroupName)
            .SendAsync("NewItemAdded", orderId.ToString(), itemDto, ct);
    }

    public Task OrderStatusChangedAsync(Guid orderId, string newStatus, CancellationToken ct = default)
    {
        _logger.LogDebug("SignalR → OrderStatusChanged order={OrderId} status={Status}", orderId, newStatus);
        return _hub.Clients.Group(GroupName)
            .SendAsync("OrderStatusChanged", orderId.ToString(), newStatus, ct);
    }

    public Task TableStatusChangedAsync(Guid tableId, string newStatus, CancellationToken ct = default)
    {
        _logger.LogDebug("SignalR → TableStatusChanged table={TableId} status={Status}", tableId, newStatus);
        return _hub.Clients.Group(GroupName)
            .SendAsync("TableStatusChanged", tableId.ToString(), newStatus, ct);
    }
}
