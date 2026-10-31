using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Common;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Loads a project's stages and reference plan, runs <see cref="BuildProgressCalculator"/>, keeps the daily
/// snapshots and builds the curves. Every query names the tenant explicitly. <c>bypassFilters</c> is true
/// only for callers without a current tenant — the public portal (tenant resolved from the slug) and the
/// daily snapshot job; tenant-scoped requests keep the global filter on as well.
/// </summary>
public sealed class BuildProgressEngine
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NexoDbContext _db;
    private readonly BuildForecastOptions _forecast;

    public BuildProgressEngine(NexoDbContext db, BuildForecastOptions forecast)
    {
        _db = db;
        _forecast = forecast;
    }

    /// <summary>
    /// Weighted-stages result (<see cref="BuildProgressCalculator"/>) and, when the company has BuildPredict on, the productivity
    /// forecast that replaced its forecast dates (see <see cref="ApplyForecast"/>).
    /// </summary>
    public sealed record Computation(
        BuildProject Project,
        IReadOnlyList<BuildProgressStageInput> Inputs,
        BuildProgressResult Result,
        BuildScheduleRevision? Plan)
    {
        /// <summary>Progress events of the project (oldest first) — the forecast's productivity history.</summary>
        public IReadOnlyList<BuildForecastEvent> Events { get; init; } = [];
        /// <summary>The productivity/1 forecast (without change-order/weather factors); null when BuildPredict is off.</summary>
        public BuildForecastResult? Forecast { get; init; }
        public bool PredictEnabled { get; init; }
        /// <summary>The weather sensitivity setting of each stage (null = keyword default).</summary>
        public IReadOnlyDictionary<Guid, bool?> WeatherSensitive { get; init; } = new Dictionary<Guid, bool?>();
    }

    /// <summary>Methodology reported when the productivity forecast replaced the Wave 1 forecast dates.</summary>
    public const string CombinedMethodology = BuildProgressCalculator.Methodology + "+" + BuildForecastEngine.Methodology;

    private IQueryable<T> Q<T>(Guid tenantId, bool bypassFilters) where T : TenantEntity
        => (bypassFilters ? _db.Set<T>().IgnoreQueryFilters() : _db.Set<T>()).Where(x => x.TenantId == tenantId);

    public async Task<Computation?> ComputeAsync(Guid tenantId, Guid projectId, DateOnly today, bool bypassFilters,
        CancellationToken ct)
        => (await ComputeManyAsync(tenantId, [projectId], today, bypassFilters, ct)).GetValueOrDefault(projectId);

    /// <summary>
    /// The computation of several projects of one tenant with a fixed number of queries (projects, stages, latest plans,
    /// BuildPredict flag, progress events) — used by the portfolio dashboard without N+1.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Computation>> ComputeManyAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds,
        DateOnly today, bool bypassFilters, CancellationToken ct)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, Computation>();
        var projects = await Q<BuildProject>(tenantId, bypassFilters).AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (projects.Count == 0) return new Dictionary<Guid, Computation>();
        var found = projects.Select(p => p.Id).ToList();

        var stages = await Q<BuildStage>(tenantId, bypassFilters).AsNoTracking()
            .Where(s => found.Contains(s.ProjectId)).ToListAsync(ct);
        var revisions = Q<BuildScheduleRevision>(tenantId, bypassFilters);
        var plans = await revisions.AsNoTracking()
            .Where(r => found.Contains(r.ProjectId)
                        && r.Number == revisions.Where(x => x.ProjectId == r.ProjectId).Max(x => x.Number))
            .ToListAsync(ct);
        var predict = await PredictEnabledAsync(tenantId, ct);
        var events = predict
            ? await Q<BuildStageProgressEvent>(tenantId, bypassFilters).AsNoTracking()
                .Where(e => found.Contains(e.ProjectId))
                .OrderBy(e => e.EffectiveDate).ThenBy(e => e.CreatedAt)
                .Select(e => new { e.ProjectId, e.StageId, e.EffectiveDate, e.CreatedAt, e.PreviousPercent, e.Percent })
                .ToListAsync(ct)
            : [];

        var stagesByProject = stages.GroupBy(s => s.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var planByProject = plans.GroupBy(p => p.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Number).First());
        var eventsByProject = events.GroupBy(e => e.ProjectId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<BuildForecastEvent>)g.Select(e => new BuildForecastEvent(e.StageId, e.EffectiveDate, e.CreatedAt,
                e.PreviousPercent, e.Percent)).ToList());

        var result = new Dictionary<Guid, Computation>(projects.Count);
        foreach (var project in projects)
        {
            var plan = planByProject.GetValueOrDefault(project.Id);
            var projectStages = stagesByProject.GetValueOrDefault(project.Id) ?? [];
            var frozen = plan is null
                ? new Dictionary<Guid, BuildScheduleItemDto>()
                : ParseItems(plan.ItemsJson).GroupBy(i => i.StageId).ToDictionary(g => g.Key, g => g.First());

            var inputs = projectStages.Select(s =>
            {
                var inPlan = frozen.TryGetValue(s.Id, out var item);
                return new BuildProgressStageInput(
                    s.Id, s.Name, s.Order,
                    inPlan && item!.Weight > 0m ? item.Weight : s.Weight,
                    s.ProgressPercent, s.Status,
                    inPlan ? item!.PlannedStart : s.PlannedStartDate,
                    inPlan ? item!.PlannedEnd : s.PlannedEndDate,
                    s.ActualStartDate, s.ActualEndDate);
            }).ToList();

            var baseResult = BuildProgressCalculator.Calculate(inputs, today);
            var projectEvents = eventsByProject.GetValueOrDefault(project.Id) ?? [];
            BuildForecastResult? forecast = predict
                ? BuildForecastEngine.Calculate(new BuildForecastInput(today, inputs, projectEvents, _forecast.Calendar, _forecast.WindowDays))
                : null;
            result[project.Id] = new Computation(project, inputs, forecast is null ? baseResult : ApplyForecast(baseResult, forecast), plan)
            {
                Events = projectEvents,
                Forecast = forecast,
                PredictEnabled = predict,
                WeatherSensitive = projectStages.ToDictionary(s => s.Id, s => s.WeatherSensitive),
            };
        }
        return result;
    }

    private async Task<bool> PredictEnabledAsync(Guid tenantId, CancellationToken ct)
        => await _db.BldTenantSettings.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenantId)
               .Select(s => (bool?)s.BuildPredictEnabled).FirstOrDefaultAsync(ct)
           ?? BuildTenantSettings.DefaultBuildPredict;

    /// <summary>
    /// Replaces the Wave 1 forecast dates (weighted-stages/1: observed average rate in calendar days) by the productivity forecast
    /// (productivity/1: working days, recent window, sequence) when it has a delivery: forecast start, end and delay per stage,
    /// forecast delivery and the forecast % at the planned delivery. Planned and actual figures are untouched. Without a
    /// productivity delivery the Wave 1 result stays (fallback).
    /// </summary>
    public static BuildProgressResult ApplyForecast(BuildProgressResult r, BuildForecastResult f)
    {
        if (f.ForecastDelivery is null) return r;
        var byStage = f.Stages.ToDictionary(s => s.StageId);
        var stages = r.Stages.Select(s => byStage.TryGetValue(s.StageId, out var fs) && fs.ForecastEnd is not null
            ? s with
            {
                ForecastStart = fs.ForecastStart,
                ForecastEnd = fs.ForecastEnd,
                DelayDays = fs.DelayDays,
                Flags = s.Flags.Where(x => x != BuildProgressCalculator.FlagNoForecast).ToList(),
            }
            : s).ToList();
        var merged = r with { Stages = stages, ForecastDelivery = f.ForecastDelivery, Methodology = CombinedMethodology };
        return merged with
        {
            ForecastPercent = r.PlannedDelivery is { } pd
                ? BuildProgressCalculator.ForecastPercentAt(merged, pd < r.Date ? r.Date : pd)
                : r.ForecastPercent,
        };
    }

    public static IReadOnlyList<BuildScheduleItemDto> ParseItems(string json)
        => JsonSerializer.Deserialize<List<BuildScheduleItemDto>>(json, Json) ?? [];

    public static string PlanSource(BuildScheduleRevision? plan)
        => plan is null ? "current" : plan.Kind == BuildScheduleRevisionKind.Baseline ? "baseline" : "revision";

    // ── Snapshots ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates or refreshes the snapshot of <paramref name="today"/> (past days are never touched) and saves.
    /// A concurrent creation of the same day is resolved by refreshing the row that won.
    /// </summary>
    public async Task UpsertSnapshotAsync(Computation c, DateOnly today, bool bypassFilters, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var existing = await Q<BuildProgressSnapshot>(c.Project.TenantId, bypassFilters)
                .FirstOrDefaultAsync(s => s.ProjectId == c.Project.Id && s.Date == today, ct);
            BuildProgressSnapshot? added = null;
            if (existing is null)
            {
                added = Snapshot(c, today);
                _db.BldProgressSnapshots.Add(added);
            }
            else
            {
                existing.Refresh(today, c.Result.PlannedPercent, c.Result.ActualPercent, c.Result.ForecastPercent,
                    c.Result.ForecastDelivery, StagesJson(c.Result), c.Result.Methodology);
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 0 && added is not null
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _db.Entry(added).State = EntityState.Detached;
            }
        }
    }

    public static BuildProgressSnapshot Snapshot(Computation c, DateOnly today)
        => BuildProgressSnapshot.Take(c.Project.TenantId, c.Project.Id, today, c.Result.PlannedPercent,
            c.Result.ActualPercent, c.Result.ForecastPercent, c.Result.ForecastDelivery, StagesJson(c.Result),
            c.Result.Methodology);

    private static string StagesJson(BuildProgressResult r)
        => JsonSerializer.Serialize(r.Stages.Select(s => new
        {
            stageId = s.StageId,
            name = s.Name,
            order = s.Order,
            weight = s.Weight,
            status = s.Status,
            plannedPercent = s.PlannedPercent,
            actualPercent = s.ActualPercent,
            plannedEnd = s.PlannedEnd,
            forecastEnd = s.ForecastEnd,
            delayDays = s.DelayDays,
        }), Json);

    /// <summary>
    /// Daily job: one snapshot per active project per day (Brazil). Only creates the missing ones — today's
    /// existing snapshots are kept current by the progress changes themselves. Returns how many were created.
    /// </summary>
    public async Task<int> EnsureDailySnapshotsAsync(DateOnly today, CancellationToken ct)
    {
        var active = await _db.BldProjects.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == BuildProjectStatus.Planning || p.Status == BuildProjectStatus.InProgress
                     || p.Status == BuildProjectStatus.Paused)
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync(ct);
        var done = (await _db.BldProgressSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Date == today).Select(s => s.ProjectId).ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var p in active.Where(p => !done.Contains(p.Id)))
        {
            var c = await ComputeAsync(p.TenantId, p.Id, today, bypassFilters: true, ct);
            if (c is null) continue;
            await UpsertSnapshotAsync(c, today, bypassFilters: true, ct);
            _db.ChangeTracker.Clear();
            created++;
        }
        return created;
    }

    // ── Curves ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Weekly points from the first known date to the latest of today / planned / forecast delivery.
    /// Planned: the reference plan; actual: the daily snapshots (today: the live value); forecast: from
    /// today to the forecast delivery.
    /// </summary>
    public async Task<BuildProgressCurvesDto> CurvesAsync(Computation c, DateOnly today, bool bypassFilters, CancellationToken ct)
    {
        var snapshots = await Q<BuildProgressSnapshot>(c.Project.TenantId, bypassFilters).AsNoTracking()
            .Where(s => s.ProjectId == c.Project.Id && s.Date <= today)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, s.ActualPercent })
            .ToListAsync(ct);

        var starts = c.Inputs.Select(i => i.PlannedStart).Concat(c.Inputs.Select(i => i.ActualStart))
            .Append(c.Project.StartDate)
            .Append(snapshots.Count > 0 ? snapshots[0].Date : null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        var start = starts.Count > 0 ? starts.Min() : today;
        var ends = new[] { (DateOnly?)today, c.Result.ForecastDelivery, c.Result.PlannedDelivery, c.Project.ExpectedEndDate }
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        var end = ends.Max();
        if (start > end) start = end;

        var totalDays = end.DayNumber - start.DayNumber;
        var stepDays = 7 * Math.Max(1, (int)Math.Ceiling(totalDays / 7.0 / 200.0)); // ≤ ~200 points
        var points = new List<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(stepDays)) points.Add(d);
        if (points[^1] != end) points.Add(end);

        var planned = new List<BuildProgressPointDto>();
        foreach (var d in points)
            if (BuildProgressCalculator.PlannedPercentAt(c.Inputs, d) is { } p) planned.Add(new(d, p));

        var actual = new List<BuildProgressPointDto>();
        var idx = -1;
        foreach (var d in points.Where(d => d < today))
        {
            while (idx + 1 < snapshots.Count && snapshots[idx + 1].Date <= d) idx++;
            if (idx >= 0) actual.Add(new(d, snapshots[idx].ActualPercent));
        }
        actual.Add(new(today, c.Result.ActualPercent));

        var forecast = new List<BuildProgressPointDto> { new(today, c.Result.ActualPercent) };
        var forecastEnd = c.Result.ForecastDelivery;
        if (forecastEnd is { } fe && fe > today)
        {
            foreach (var d in points.Where(d => d > today && d < fe))
                forecast.Add(new(d, BuildProgressCalculator.ForecastPercentAt(c.Result, d)));
            forecast.Add(new(fe, BuildProgressCalculator.ForecastPercentAt(c.Result, fe)));
        }

        return new BuildProgressCurvesDto(planned, actual, forecast);
    }
}
