using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Hubs;

[Authorize]
public class RestaurantHub : Hub
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RestaurantHub> _logger;

    public RestaurantHub(ICurrentUser currentUser, ILogger<RestaurantHub> logger)
    {
        _currentUser = currentUser;
        _logger      = logger;
    }

    /// <summary>
    /// Client calls this after connecting to subscribe to a store's events.
    /// Validates that the authenticated user has access to the requested store via JWT claims.
    /// </summary>
    public async Task JoinStore(string storeId)
    {
        if (!Guid.TryParse(storeId, out var storeGuid))
            throw new HubException("Invalid storeId format.");

        if (!_currentUser.StoreIds.Contains(storeGuid))
            throw new HubException("Access denied: you do not have access to this store.");

        var groupName = GroupFor(_currentUser.TenantId, storeGuid);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Connection {ConnectionId} joined store group {Group}",
            Context.ConnectionId, groupName);
    }

    public async Task LeaveStore(string storeId)
    {
        if (!Guid.TryParse(storeId, out var storeGuid))
            throw new HubException("Invalid storeId format.");

        var groupName = GroupFor(_currentUser.TenantId, storeGuid);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Connection {ConnectionId} left store group {Group}",
            Context.ConnectionId, groupName);
    }

    internal static string GroupFor(Guid tenantId, Guid storeId)
        => $"store:{tenantId}:{storeId}";
}
