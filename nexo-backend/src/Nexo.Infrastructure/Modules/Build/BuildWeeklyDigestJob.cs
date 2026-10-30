using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Optional weekly client digest (<c>Build:Ai:WeeklyDigestJob:Enabled</c>, default false; never in Testing). On Mondays (Brazil)
/// it creates one Pending WeeklyDigest suggestion per project with a published portal, in companies with the Build AI on,
/// unless the project already has one from this week. Each project runs in its own scope with the tenant set; the usage
/// limits of the AI runtime apply (over the limit → the deterministic text). It never publishes: a person reviews, accepts
/// (draft update) and publishes.
/// </summary>
public sealed class BuildWeeklyDigestJob : BackgroundService
{
    public const string ActorName = "Resumo semanal automático";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<BuildWeeklyDigestJob> _logger;

    public BuildWeeklyDigestJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<BuildWeeklyDigestJob> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Build:Ai:WeeklyDigestJob:Enabled", false)) return;
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                if (BusinessClock.Today().DayOfWeek == DayOfWeek.Monday)
                {
                    var created = await RunOnceAsync(_scopes, _logger, stoppingToken);
                    if (created > 0) _logger.LogInformation("Build weekly digests suggested: {Count}", created);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Build weekly digest job failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One pass (idempotent within the week). Public for tests and manual runs.</summary>
    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, ILogger logger, CancellationToken ct)
    {
        List<(Guid TenantId, Guid ProjectId)> targets;
        var weekStart = BusinessClock.TodayStartUtc().AddDays(-6);
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var aiTenants = await db.BldTenantSettings.AsNoTracking().Where(s => s.BuildAiEnabled).Select(s => s.TenantId).ToListAsync(ct);
            targets = await db.BldProjectPortals.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.IsPublished && aiTenants.Contains(p.TenantId))
                .Where(p => !db.BldAiSuggestions.IgnoreQueryFilters().Any(s => s.TenantId == p.TenantId && s.ProjectId == p.ProjectId
                    && s.Capability == BuildAiCapability.WeeklyDigest && s.CreatedAt >= weekStart))
                .Select(p => new ValueTuple<Guid, Guid>(p.TenantId, p.ProjectId)).ToListAsync(ct);
        }

        var created = 0;
        foreach (var (tenantId, projectId) in targets)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Set(tenantId, "weekly-digest", ["build"]);
                var ai = scope.ServiceProvider.GetRequiredService<BuildAiService>();
                ai.SystemActorName = ActorName;
                await ai.WeeklyDigestAsync(projectId, ct);
                created++;
            }
            catch (Exception ex) when (ex is AiProviderCallException or Nexo.Domain.Exceptions.DomainException)
            {
                logger.LogWarning("Weekly digest skipped for a project: {Reason}", ex.Message);
            }
        }
        return created;
    }
}
