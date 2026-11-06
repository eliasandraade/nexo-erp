using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Nexo.Infrastructure.Hubs;

// Same set as AppRoles.RestaurantOps (Nexo.Api) — floor and kitchen; Estoquista has no use for order events.
[Authorize(Roles = "Gerente,Diretoria,Vendedor,Cozinha")]
public class RestaurantHub : Hub
{
    private readonly HubSessionGuard _guard;
    private readonly ILogger<RestaurantHub> _logger;

    public RestaurantHub(HubSessionGuard guard, ILogger<RestaurantHub> logger)
    {
        _guard  = guard;
        _logger = logger;
    }

    /// <summary>
    /// Client calls this after connecting to subscribe to a store's events. Validates that the user has access to the requested
    /// store via the claims of the token that opened the connection. Those claims only count while the session is the same:
    /// <see cref="HubSessionFilter"/> confirmed it before this call, and it is confirmed again once the connection is in the group,
    /// so a change that landed in between cannot leave it there.
    /// </summary>
    public async Task JoinStore(string storeId)
    {
        var groupName = GroupOf(storeId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        try
        {
            await _guard.EnsureSessionAsync(Context);
        }
        catch
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, CancellationToken.None);
            throw;
        }
        _logger.LogInformation("Connection {ConnectionId} joined store group {Group}",
            Context.ConnectionId, groupName);
    }

    public async Task LeaveStore(string storeId)
    {
        var groupName = GroupOf(storeId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Connection {ConnectionId} left store group {Group}",
            Context.ConnectionId, groupName);
    }

    /// <summary>
    /// From the caller context, the only one that exists on every transport: the HTTP context behind ICurrentUser is gone once the
    /// first poll of a long-polling connection ends.
    /// </summary>
    private string GroupOf(string storeId)
    {
        if (!Guid.TryParse(storeId, out var storeGuid))
            throw new HubException("Invalid storeId format.");

        if (Context.User?.HasClaim("store", storeGuid.ToString()) != true
            || !Guid.TryParse(Context.User.FindFirstValue("tenantId"), out var tenantId))
            throw new HubException("Access denied: you do not have access to this store.");

        return GroupFor(tenantId, storeGuid);
    }

    public static string GroupFor(Guid tenantId, Guid storeId)
        => $"store:{tenantId}:{storeId}";
}
