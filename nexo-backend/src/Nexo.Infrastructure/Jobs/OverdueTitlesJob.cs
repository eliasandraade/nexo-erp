using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Jobs;

/// <summary>
/// Marca como vencidos (Overdue) os títulos em aberto cujo vencimento ficou antes de hoje (Brasil).
/// A system job across every tenant: one idempotent UPDATE an hour. Listings don't depend on it —
/// they already show a pending título past due as overdue — but the stored status stays honest for
/// reports and integrations.
/// </summary>
public class OverdueTitlesJob : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OverdueTitlesJob> _logger;

    public OverdueTitlesJob(IServiceScopeFactory scopes, ILogger<OverdueTitlesJob> logger)
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
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var count = await MarkOverdueAsync(db, BusinessClock.TodayStartUtc(), stoppingToken);
                if (count > 0) _logger.LogInformation("Overdue títulos marked: {Count}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overdue títulos job failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Pending → Overdue for every título due before <paramref name="todayStartUtc"/>. Idempotent.</summary>
    public static Task<int> MarkOverdueAsync(NexoDbContext db, DateTime todayStartUtc, CancellationToken ct = default)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE nexo.financial_transactions SET status = 'Overdue', updated_at = now() WHERE status = 'Pending' AND due_date < {todayStartUtc}",
            ct);
}
