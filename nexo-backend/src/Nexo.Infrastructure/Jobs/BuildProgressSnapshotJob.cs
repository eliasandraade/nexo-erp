using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Infrastructure.Jobs;

/// <summary>
/// One progress snapshot per active Build project per day (Brazil), so the curves and "como estava a obra
/// em X" have a point even on days nobody touched the project. A system job across every tenant, hourly and
/// idempotent: it only creates the day's missing snapshots (changes during the day refresh today's own).
/// </summary>
public class BuildProgressSnapshotJob : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BuildProgressSnapshotJob> _logger;

    public BuildProgressSnapshotJob(IServiceScopeFactory scopes, ILogger<BuildProgressSnapshotJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRun, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<BuildProgressEngine>();
                var created = await engine.EnsureDailySnapshotsAsync(BusinessClock.Today(), stoppingToken);
                if (created > 0) _logger.LogInformation("Build progress snapshots created: {Count}", created);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Build progress snapshot job failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
