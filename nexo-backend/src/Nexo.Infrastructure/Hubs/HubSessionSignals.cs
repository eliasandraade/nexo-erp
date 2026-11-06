using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// Tells the instances of the API that a session may have ended: this one directly, the others through Redis pub/sub. A message
/// carries ids only and each instance asks the database (<see cref="HubSessionGuard.ReviewAsync"/>), so a late or repeated one
/// cannot close a connection that is valid now.
///
/// Best effort on purpose, and it never fails the caller: the change it reports is already committed. A message lost with Redis out
/// is covered by the sweep of each instance (<see cref="HubSessionSweepJob"/>).
/// </summary>
public sealed class HubSessionSignals
{
    private static readonly RedisChannel Channel = RedisChannel.Literal("orken:hub-sessions");
    private static readonly TimeSpan ReviewLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SubscribeLimit = TimeSpan.FromSeconds(3);

    private sealed record Message(string Instance, IReadOnlyCollection<Guid> Users, IReadOnlyCollection<Guid> Tenants, IReadOnlyCollection<string>? EndedTokens);

    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly HubSessionGuard _guard;
    private readonly IServiceProvider _services;
    private readonly ILogger<HubSessionSignals> _logger;
    private volatile bool _listening;

    public HubSessionSignals(HubSessionGuard guard, IServiceProvider services, ILogger<HubSessionSignals> logger)
    {
        _guard = guard;
        _services = services;
        _logger = logger;
    }

    /// <summary>Closes what the change ended on this instance, and only then returns. Tells the others without waiting for them.</summary>
    public async Task PublishAsync(HubSessionChange change, CancellationToken ct)
    {
        await ReviewAsync(change, ct);
        try
        {
            if (_services.GetService<IConnectionMultiplexer>() is not { } redis) return;
            var message = JsonSerializer.Serialize(new Message(_instance, change.Users, change.Tenants, change.EndedTokens));
            await redis.GetSubscriber().PublishAsync(Channel, message, CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not tell the other instances about a session change; their sweep covers it");
        }
    }

    /// <summary>Subscribes once. Without Redis there is nobody to hear from; a failure here is tried again by the next call of the job.</summary>
    public async Task StartListeningAsync(CancellationToken ct)
    {
        if (_listening) return;
        try
        {
            if (_services.GetService<IConnectionMultiplexer>() is { } redis)
                await redis.GetSubscriber().SubscribeAsync(Channel, (_, message) => OnMessage(message)).WaitAsync(SubscribeLimit, ct);
            _listening = true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not subscribe to the session changes of the other instances; trying again on the next pass");
        }
    }

    private void OnMessage(RedisValue value)
        => _ = Task.Run(async () =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<Message>(value.ToString());
                if (message is null || message.Instance == _instance) return;
                await ReviewAsync(new HubSessionChange(message.Users, message.Tenants, message.EndedTokens), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read a session change from another instance; the sweep covers it");
            }
        });

    private async Task ReviewAsync(HubSessionChange change, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(ReviewLimit);
            await _guard.ReviewAsync(change, limit.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not review the hub connections of a session change; the sweep covers it");
        }
    }
}
