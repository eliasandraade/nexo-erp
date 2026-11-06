using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// The fail-safe of the hub connections: every <see cref="Interval"/> it judges all the connections of this instance by the database,
/// so a connection whose session ended closes in at most <see cref="Interval"/> + <see cref="PassLimit"/> (30 s) even if the
/// immediate signal was lost, as long as the database answers. A failed pass closes nothing and the next one tries again. With no
/// connection open, a pass touches nothing. The job also keeps the subscription to the signals of the other instances.
/// Not in Testing: tests call <see cref="RunOnceAsync"/>.
/// </summary>
public sealed class HubSessionSweepJob : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan PassLimit = TimeSpan.FromSeconds(10);

    private readonly HubSessionGuard _guard;
    private readonly HubSessionSignals _signals;
    private readonly ILogger<HubSessionSweepJob> _logger;
    private readonly TimeSpan _interval;

    public HubSessionSweepJob(HubSessionGuard guard, HubSessionSignals signals, ILogger<HubSessionSweepJob> logger, TimeSpan? interval = null)
    {
        _guard = guard;
        _signals = signals;
        _logger = logger;
        _interval = interval ?? Interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();   // connecting to Redis must not hold the start of the host
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                await _signals.StartListeningAsync(stoppingToken);
                try
                {
                    using var pass = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    pass.CancelAfter(PassLimit);
                    await RunOnceAsync(_guard, _logger, pass.Token);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Hub connection sweep failed, nothing was closed; retrying in {Interval}", _interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the host is stopping
        }
    }

    /// <summary>One pass. Public for tests and manual runs. Throws when the database fails, having closed nothing. Returns how many closed.</summary>
    public static async Task<int> RunOnceAsync(HubSessionGuard guard, ILogger logger, CancellationToken ct)
    {
        var closed = await guard.SweepAsync(ct);
        if (closed > 0) logger.LogInformation("Hub connection sweep closed {Count} connection(s) whose session ended", closed);
        return closed;
    }
}
