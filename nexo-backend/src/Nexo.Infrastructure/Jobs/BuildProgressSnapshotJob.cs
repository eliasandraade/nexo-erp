using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Infrastructure.Jobs;

/// <summary>
/// One progress snapshot per active Build project per day (Brazil), so the curves and "como estava a obra
/// em X" have a point even on days nobody touched the project. A system job across every tenant, hourly and
/// idempotent: it only creates the day's missing snapshots (changes during the day refresh today's own). Wave 6: it also stores
/// the day's forecast (Scheduled) of every active project whose company has BuildPredict on — deterministic, no LLM.
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
                var today = BusinessClock.Today();
                var created = await engine.EnsureDailySnapshotsAsync(today, stoppingToken);
                if (created > 0) _logger.LogInformation("Build progress snapshots created: {Count}", created);
                // Wave 6: one deterministic forecast (productivity/1) per active project of the companies with BuildPredict on
                var forecasts = await scope.ServiceProvider.GetRequiredService<BuildForecastService>()
                    .EnsureDailyForecastsAsync(today, stoppingToken);
                if (forecasts > 0) _logger.LogInformation("Build forecasts stored: {Count}", forecasts);
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
