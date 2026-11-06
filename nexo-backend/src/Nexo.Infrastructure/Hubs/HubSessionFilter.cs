using Microsoft.AspNetCore.SignalR;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// Every connection of every hub goes through <see cref="HubSessionGuard"/>: it registers when it opens and it is checked on every
/// hub call, so a new hub or a new hub method cannot forget it.
/// </summary>
public sealed class HubSessionFilter : IHubFilter
{
    private readonly HubSessionGuard _guard;

    public HubSessionFilter(HubSessionGuard guard) => _guard = guard;

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        _guard.Register(context.Context);
        try
        {
            await _guard.CloseIfEndedAsync(context.Context);
            await next(context);
        }
        catch
        {
            // After a failure here the framework does not call OnDisconnectedAsync.
            _guard.Forget(context.Context.ConnectionId);
            throw;
        }
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await _guard.EnsureSessionAsync(invocationContext.Context);
        return await next(invocationContext);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        _guard.Forget(context.Context.ConnectionId);
        return next(context, exception);
    }
}
