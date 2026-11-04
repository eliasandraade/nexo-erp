using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Integrations.Weather;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Forecast and weather configuration (Wave 6): <c>Build:Forecast:WorkingDays</c> (default Mon–Sat),
/// <c>Build:Forecast:ExtraHolidays</c> (yyyy-MM-dd list, added to the national table), <c>Build:Forecast:WindowDays</c> (21),
/// <c>Build:Weather:SensitiveKeywords</c> (list replacing the default keywords).
/// </summary>
public sealed class BuildForecastOptions
{
    public BuildForecastOptions(IConfiguration config)
    {
        var days = BuildWorkCalendar.ParseDays(config["Build:Forecast:WorkingDays"]);
        var extra = List(config, "Build:Forecast:ExtraHolidays")
            .Select(x => DateOnly.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        Calendar = new BuildWorkCalendar(days, BrazilNationalHolidays.All().Concat(extra));
        WindowDays = Math.Clamp(config.GetValue("Build:Forecast:WindowDays", BuildForecastEngine.DefaultWindowDays), 3, 180);
        var keywords = List(config, "Build:Weather:SensitiveKeywords");
        SensitiveKeywords = keywords.Count > 0 ? keywords : BuildWeatherRules.DefaultKeywords;
        ExtraHolidays = extra.Count;
    }

    public BuildWorkCalendar Calendar { get; }
    public int WindowDays { get; }
    public IReadOnlyList<string> SensitiveKeywords { get; }
    public int ExtraHolidays { get; }

    public BuildForecastCalendarDto Describe()
        => new(Calendar.WorkingDays.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString()).ToList(),
            $"Feriados nacionais (tabela {BrazilNationalHolidays.FirstYear}–{BrazilNationalHolidays.LastYear})"
            + (ExtraHolidays > 0 ? $" + {ExtraHolidays} data(s) da configuração" : ""), WindowDays);

    /// <summary>A list from an array section (Key:0, Key:1…) or a comma-separated value.</summary>
    private static List<string> List(IConfiguration config, string key)
    {
        var section = config.GetSection(key);
        var items = section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
            items = section.Value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return items;
    }
}

/// <summary>
/// Delivery forecast (methodology productivity/1, <see cref="BuildForecastEngine"/>) and weather per activity — company side
/// (tenant-scoped) plus the daily job. Forecast routes need BuildPredict on (403 build_predict_disabled); weather needs the
/// platform weather integration on and site coordinates (otherwise 200 with a reason). Nothing here calls an LLM: the
/// explanation of the forecast is the Wave 5 risk/explain.
/// </summary>
public sealed class BuildForecastService
{
    public const string CodePredictDisabled = "build_predict_disabled";
    public const string WeatherSource = "Open-Meteo";
    public const string WeatherDisclaimer =
        "Previsão do tempo para o local da obra — é previsão, não certeza, e pode mudar ao longo do dia. Não altera datas automaticamente.";
    public const int WeatherDays = 7;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildProgressEngine _engine;
    private readonly BuildForecastOptions _options;
    private readonly BuildAuditTrail _audit;
    private readonly IWeatherProvider _weather;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly ILogger<BuildForecastService> _logger;

    public BuildForecastService(NexoDbContext db, ICurrentTenant tenant, BuildProgressEngine engine, BuildForecastOptions options,
        BuildAuditTrail audit, IWeatherProvider weather, IIntegrationFeatureFlags flags, ILogger<BuildForecastService> logger)
    {
        _db = db;
        _tenant = tenant;
        _engine = engine;
        _options = options;
        _audit = audit;
        _weather = weather;
        _flags = flags;
        _logger = logger;
    }

    private Guid Tid => _tenant.Id;

    // ── Forecast ──────────────────────────────────────────────────────────────

    /// <summary>Today's stored forecast (latest of the day) or, when none was stored today, one computed on the fly (not stored).</summary>
    public async Task<BuildForecastDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        await EnsurePredictAsync(Tid, ct);
        var today = BusinessClock.Today();
        var stored = await _db.BldForecasts.AsNoTracking()
            .Where(f => f.TenantId == Tid && f.ProjectId == projectId && f.Date == today)
            .OrderByDescending(f => f.GeneratedAt).FirstOrDefaultAsync(ct);
        var expectedEnd = await _db.BldProjects.AsNoTracking().Where(p => p.TenantId == Tid && p.Id == projectId)
            .Select(p => p.ExpectedEndDate).FirstOrDefaultAsync(ct);
        if (stored is not null) return Map(stored, expectedEnd);

        var (c, f) = await ComputeFullAsync(Tid, projectId, today, bypass: false, ct);
        return Map(projectId, f, c.Project.ExpectedEndDate, "OnTheFly", stored: false, id: null, DateTime.UtcNow, null);
    }

    /// <summary>Recomputes and stores (source Manual). History is kept.</summary>
    public async Task<BuildForecastDto> RecomputeAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        await EnsurePredictAsync(Tid, ct);
        var today = BusinessClock.Today();
        var (c, f) = await ComputeFullAsync(Tid, projectId, today, bypass: false, ct);
        var row = Entity(Tid, projectId, f, BuildForecastSource.Manual, _audit.UserId, _audit.UserName);
        _db.BldForecasts.Add(row);
        _audit.Record(AuditActions.BuildForecastRecomputed, "Forecast", row.Id,
            f.ForecastDelivery is { } d ? $"Previsão recalculada: entrega em {d:dd/MM/yyyy}." : "Previsão recalculada: sem data de entrega.",
            new { projectId, f.Methodology, f.Confidence });
        await _db.SaveChangesAsync(ct);
        return Map(row, c.Project.ExpectedEndDate);
    }

    public async Task<IReadOnlyList<BuildForecastHistoryItemDto>> HistoryAsync(Guid projectId, int? take, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var n = Math.Clamp(take ?? 60, 1, 365);
        var rows = await _db.BldForecasts.AsNoTracking()
            .Where(f => f.TenantId == Tid && f.ProjectId == projectId)
            .OrderByDescending(f => f.GeneratedAt).Take(n)
            .Select(f => new { f.Id, f.Date, f.GeneratedAt, f.Source, f.Methodology, f.Version, f.ForecastDelivery, f.PlannedDelivery, f.Confidence, f.CreatedByName })
            .ToListAsync(ct);
        return rows.Select(f => new BuildForecastHistoryItemDto(f.Id, f.Date, BuildScope.Utc(f.GeneratedAt), f.Source.ToString(), f.Methodology,
            f.Version, f.ForecastDelivery, f.PlannedDelivery,
            f.ForecastDelivery is { } fd && f.PlannedDelivery is { } pd ? fd.DayNumber - pd.DayNumber : null, f.Confidence, f.CreatedByName)).ToList();
    }

    /// <summary>
    /// Daily job (system, every tenant): one Scheduled forecast per active project of the companies with BuildPredict on that
    /// has none for today. Deterministic; weather factor included when available. Returns how many were stored.
    /// </summary>
    public async Task<int> EnsureDailyForecastsAsync(DateOnly today, CancellationToken ct)
    {
        var predictOff = await _db.BldTenantSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => !s.BuildPredictEnabled).Select(s => s.TenantId).ToListAsync(ct);
        var active = await _db.BldProjects.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (p.Status == BuildProjectStatus.Planning || p.Status == BuildProjectStatus.InProgress
                         || p.Status == BuildProjectStatus.Paused) && !predictOff.Contains(p.TenantId))
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync(ct);
        var done = (await _db.BldForecasts.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Date == today).Select(f => f.ProjectId).ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var p in active.Where(p => !done.Contains(p.Id)))
        {
            try
            {
                var (_, f) = await ComputeFullAsync(p.TenantId, p.Id, today, bypass: true, ct);
                _db.BldForecasts.Add(Entity(p.TenantId, p.Id, f, BuildForecastSource.Scheduled, null, null));
                await _db.SaveChangesAsync(ct);
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Build forecast of project {ProjectId} failed; skipped until the next run.", p.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return created;
    }

    /// <summary>The productivity forecast with every factor: pending change orders (risk only) and weather risk (risk only).</summary>
    private async Task<(BuildProgressEngine.Computation C, BuildForecastResult F)> ComputeFullAsync(Guid tenantId, Guid projectId,
        DateOnly today, bool bypass, CancellationToken ct)
    {
        var c = await _engine.ComputeAsync(tenantId, projectId, today, bypass, ct) ?? throw new NotFoundException("BuildProject", projectId);
        var orders = bypass ? _db.BldChangeOrders.IgnoreQueryFilters() : _db.BldChangeOrders;
        var pending = await orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.ScheduleImpactDays > 0
                        && (o.Status == BuildChangeOrderStatus.Draft || o.Status == BuildChangeOrderStatus.SentToClient))
            .Select(o => new { o.Id, o.Number, o.Title, o.Status, o.ScheduleImpactDays, o.StageId })
            .ToListAsync(ct);

        var (days, _) = await DailyAsync(c.Project, ct);
        var weatherRisks = new List<BuildForecastWeatherRisk>();
        if (days is not null)
        {
            var stages = WeatherStages(c, today);
            foreach (var day in days.Where(d => d.Date >= today))
                foreach (var (stage, condition, message) in BuildWeatherRules.Risks(day, stages))
                    weatherRisks.Add(new BuildForecastWeatherRisk(day.Date, stage.StageId, stage.Name, condition.Level, message));
        }

        var f = BuildForecastEngine.Calculate(new BuildForecastInput(today, c.Inputs, c.Events, _options.Calendar, _options.WindowDays,
            pending.Select(o => new BuildForecastChangeOrder(o.Id, o.Number, o.Title, o.Status.ToString(), o.ScheduleImpactDays, o.StageId)).ToList(),
            weatherRisks));
        return (c, f);
    }

    private static BuildForecast Entity(Guid tenantId, Guid projectId, BuildForecastResult f, BuildForecastSource source, Guid? userId,
        string? userName)
        => BuildForecast.Store(tenantId, projectId, f.Date, f.Methodology, f.Version, f.ForecastDelivery, f.PlannedDelivery, f.Confidence,
            JsonSerializer.Serialize(f.Factors, Json), JsonSerializer.Serialize(f.Stages, Json), source, userId, userName);

    private BuildForecastDto Map(BuildForecast row, DateOnly? expectedEnd)
    {
        var stages = JsonSerializer.Deserialize<List<BuildForecastStageResult>>(row.StagesJson, Json) ?? [];
        var factors = JsonSerializer.Deserialize<List<BuildForecastFactor>>(row.FactorsJson, Json) ?? [];
        var f = new BuildForecastResult(row.Date, row.Methodology, row.Version, row.ForecastDelivery, row.PlannedDelivery,
            row.ForecastDelivery is { } fd && row.PlannedDelivery is { } pd ? fd.DayNumber - pd.DayNumber : null, row.Confidence, stages, factors);
        return Map(row.ProjectId, f, expectedEnd, row.Source.ToString(), stored: true, row.Id, row.GeneratedAt, row.CreatedByName);
    }

    private BuildForecastDto Map(Guid projectId, BuildForecastResult f, DateOnly? expectedEnd, string source, bool stored, Guid? id,
        DateTime generatedAt, string? createdBy)
        => new(id, projectId, f.Date, BuildScope.Utc(generatedAt), source, stored, f.Methodology, f.Version, f.ForecastDelivery,
            f.PlannedDelivery, f.DelayDays, expectedEnd,
            f.ForecastDelivery is { } fd && expectedEnd is { } ee ? fd.DayNumber - ee.DayNumber : null,
            f.Confidence, f.Stages, f.Factors, _options.Describe(), createdBy);

    // ── Weather per activity ──────────────────────────────────────────────────

    /// <summary>Today and the next 7 days at the site, with the risk of each day for the sensitive stages in the field.</summary>
    public async Task<BuildWeatherDto> WeatherAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(Tid, projectId, today, bypassFilters: false, ct) ?? throw new NotFoundException("BuildProject", projectId);
        var stages = WeatherStages(c, today);
        var stageDtos = stages.Select(s =>
        {
            var explicitSetting = c.WeatherSensitive.GetValueOrDefault(s.StageId);
            var (_, basis, keyword) = BuildWeatherRules.Sensitivity(s.Name, explicitSetting, _options.SensitiveKeywords);
            return new BuildWeatherStageDto(s.StageId, s.Name, s.Sensitive, basis, keyword, explicitSetting, s.ActiveFrom, s.ActiveTo);
        }).ToList();

        var (days, reason) = await DailyAsync(c.Project, ct);
        if (days is null)
            return new BuildWeatherDto(projectId, false, reason, ReasonMessage(reason), WeatherSource, WeatherDisclaimer, null, [], stageDtos);

        var mapped = days.Where(d => d.Date >= today).OrderBy(d => d.Date).Take(WeatherDays + 1).Select(d =>
        {
            var conditions = BuildWeatherRules.Conditions(d);
            var risks = BuildWeatherRules.Risks(d, stages)
                .Select(r => new BuildWeatherRiskDto(r.Stage.StageId, r.Stage.Name, r.Condition.Kind, r.Condition.Level, r.Message)).ToList();
            return new BuildWeatherDayDto(d.Date, d.TemperatureMax, d.TemperatureMin, d.PrecipitationMm, d.PrecipitationProbability,
                d.WindSpeedMaxKmh, d.Description, BuildWeatherRules.Level(conditions), risks);
        }).ToList();
        var todayDto = mapped.FirstOrDefault(d => d.Date == today);
        return new BuildWeatherDto(projectId, true, null, null, WeatherSource, WeatherDisclaimer, todayDto,
            mapped.Where(d => d.Date > today).ToList(), stageDtos);
    }

    /// <summary>Sets (true/false) or clears (null → keyword default) the weather sensitivity of a stage. Audited.</summary>
    public async Task<BuildWeatherStageDto> SetWeatherSensitivityAsync(Guid stageId, SetBuildStageWeatherSensitivityRequest r, CancellationToken ct)
    {
        var stage = await _db.BldStages.FirstOrDefaultAsync(s => s.TenantId == Tid && s.Id == stageId, ct)
                    ?? throw new NotFoundException("BuildStage", stageId);
        var before = stage.WeatherSensitive;
        stage.SetWeatherSensitive(r.WeatherSensitive);
        _audit.Record(AuditActions.BuildStageWeatherSensitivity, "Stage", stage.Id,
            r.WeatherSensitive switch
            {
                true => $"Etapa {stage.Name} marcada como sensível ao clima.",
                false => $"Etapa {stage.Name} marcada como não sensível ao clima.",
                null => $"Etapa {stage.Name} volta à regra por palavras-chave para o clima.",
            }, new { projectId = stage.ProjectId, before, after = stage.WeatherSensitive });
        await _db.SaveChangesAsync(ct);
        var (sensitive, basis, keyword) = BuildWeatherRules.Sensitivity(stage.Name, stage.WeatherSensitive, _options.SensitiveKeywords);
        return new BuildWeatherStageDto(stage.Id, stage.Name, sensitive, basis, keyword, stage.WeatherSensitive, null, null);
    }

    /// <summary>The stages with their sensitivity and the days they are (forecast to be) in the field.</summary>
    private List<BuildWeatherStage> WeatherStages(BuildProgressEngine.Computation c, DateOnly today)
        => c.Result.Stages.Select(s =>
        {
            var (sensitive, _, _) = BuildWeatherRules.Sensitivity(s.Name, c.WeatherSensitive.GetValueOrDefault(s.StageId), _options.SensitiveKeywords);
            DateOnly? from = null, to = null;
            var completed = s.Status == nameof(BuildStageStatus.Completed) || s.ActualPercent >= 100m;
            if (!completed && (s.ActualPercent > 0m || s.Status == nameof(BuildStageStatus.InProgress)))
            {
                from = today;
                to = s.ForecastEnd ?? s.PlannedEnd;
                if (to < today) to = null;   // late stage still in the field: open-ended
            }
            else if (!completed)
            {
                from = s.ForecastStart ?? s.PlannedStart;
                to = s.ForecastEnd ?? s.PlannedEnd;
            }
            return new BuildWeatherStage(s.StageId, s.Name, sensitive, from, to);
        }).ToList();

    /// <summary>Daily forecast at the site, or the reason it is not available (weather off, no coordinates, provider down).</summary>
    private async Task<(IReadOnlyList<BuildWeatherDay>? Days, string? Reason)> DailyAsync(BuildProject project, CancellationToken ct)
    {
        if (!_flags.WeatherEnabled) return (null, "weather_disabled");
        if (!project.HasSite) return (null, "no_coordinates");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var days = await _weather.GetDailyForecastAsync((double)project.Latitude!.Value, (double)project.Longitude!.Value, WeatherDays + 1,
                timeout.Token);
            if (days.Count == 0) return (null, "provider_unavailable");
            return (days.Select(d => new BuildWeatherDay(d.Date, d.TemperatureMax, d.TemperatureMin, d.PrecipitationMm,
                d.PrecipitationProbabilityMax, d.WindSpeedMaxKmh, d.Description)).ToList(), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Weather forecast for a Build project unavailable.");
            return (null, "provider_unavailable");
        }
    }

    private static string? ReasonMessage(string? reason) => reason switch
    {
        "weather_disabled" => "A previsão do tempo não está ligada nesta instalação.",
        "no_coordinates" => "Informe a localização (latitude e longitude) da obra para ver a previsão do tempo.",
        "provider_unavailable" => "A previsão do tempo está indisponível no momento. Tente mais tarde.",
        _ => null,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task EnsureProjectAsync(Guid projectId, CancellationToken ct) => BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);

    private async Task EnsurePredictAsync(Guid tenantId, CancellationToken ct)
    {
        var on = await _db.BldTenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId)
                     .Select(s => (bool?)s.BuildPredictEnabled).FirstOrDefaultAsync(ct)
                 ?? BuildTenantSettings.DefaultBuildPredict;
        if (!on)
            throw new ForbiddenException("As previsões do Orken Build estão desligadas para esta empresa. Fale com o suporte Orken.",
                CodePredictDisabled);
    }
}
