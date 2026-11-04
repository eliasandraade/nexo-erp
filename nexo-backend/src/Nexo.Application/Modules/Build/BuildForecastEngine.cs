using System.Globalization;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>One stage progress event as the forecast sees it (Wave 1 history, append-only).</summary>
public sealed record BuildForecastEvent(Guid StageId, DateOnly EffectiveDate, DateTime RecordedAt, int PreviousPercent, int Percent);

/// <summary>A change order not yet approved (Draft or SentToClient) — shown as a risk, never applied to the dates.</summary>
public sealed record BuildForecastChangeOrder(Guid Id, int Number, string Title, string Status, int ScheduleImpactDays, Guid? StageId);

/// <summary>A weather risk of a sensitive stage on a day (from the weather rules) — shown as a factor, never moves dates.</summary>
public sealed record BuildForecastWeatherRisk(DateOnly Date, Guid StageId, string StageName, string Level, string Message);

public sealed record BuildForecastInput(
    DateOnly Today,
    IReadOnlyList<BuildProgressStageInput> Stages,
    IReadOnlyList<BuildForecastEvent> Events,
    BuildWorkCalendar Calendar,
    int WindowDays = BuildForecastEngine.DefaultWindowDays,
    IReadOnlyList<BuildForecastChangeOrder>? PendingChangeOrders = null,
    IReadOnlyList<BuildForecastWeatherRisk>? WeatherRisks = null);

public sealed record BuildForecastStageResult(
    Guid StageId,
    string Name,
    int Order,
    decimal Weight,
    string Status,
    decimal ActualPercent,
    /// <summary>completed, window, overall, planned or none.</summary>
    string Method,
    /// <summary>Productivity used: % per working day.</summary>
    decimal? RatePerWorkingDay,
    decimal? PlannedRatePerWorkingDay,
    int DataPoints,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    DateOnly? ForecastStart,
    DateOnly? ForecastEnd,
    int? DelayDays,
    int InheritedDelayDays,
    decimal Confidence);

public sealed record BuildForecastFactor(string Key, string Kind, string Severity, string Title, string Evidence, Guid? StageId, int? Days);

public sealed record BuildForecastResult(
    DateOnly Date,
    string Methodology,
    int Version,
    DateOnly? ForecastDelivery,
    DateOnly? PlannedDelivery,
    int? DelayDays,
    decimal Confidence,
    IReadOnlyList<BuildForecastStageResult> Stages,
    IReadOnlyList<BuildForecastFactor> Factors);

/// <summary>
/// Deterministic delivery forecast — methodology <c>productivity/1</c> (pure: no I/O, no clock). See docs/build/forecast.md.
///
/// <list type="bullet">
/// <item><b>Productivity</b> of a started stage = Δ% per working day: (a) <i>window</i> — progress over the last
///   <see cref="BuildForecastInput.WindowDays"/> calendar days (default 21) ÷ working days of that window (since the actual start
///   when it began inside it), when at least one event in the window moved the stage; (b) <i>overall</i> — actual % ÷ working
///   days since the actual start; (c) <i>planned</i> — 100 ÷ planned working days, when there is no data.</item>
/// <item><b>Finish</b> of a started stage = today + ⌈remaining % ÷ productivity⌉ working days (Sundays and national holidays
///   skipped). Completed → its actual end.</item>
/// <item><b>Sequence</b>: a stage that has not started keeps its planned duration (working days) and starts on its planned start
///   — but never before today, and, when its planned start is not after the previous stage's (by order) forecast finish,
///   on the next working day after that finish (the delay is "inherited").</item>
/// <item><b>Delivery</b> = the latest stage finish. Delay = forecast − planned end (calendar days).</item>
/// <item><b>Confidence</b> 0..1 = Σ weight × stage confidence ÷ Σ weight over unfinished stages; stage confidence = method base
///   (window 0.9, overall 0.65, planned 0.5 for not started / 0.35 for started without data) × (0.6 + 0.4 × min(1, events ÷ 5))
///   × 1 ÷ (1 + coefficient of variation of the interval rates).</item>
/// <item><b>Factors</b> explain the result: low productivity (&lt; 80% of the planned rate), inherited delays, pending change
///   orders (risk only — not applied), weather risk of sensitive stages (risk only), stages without forecast, delivery after
///   the plan. They never move dates.</item>
/// </list>
/// </summary>
public static class BuildForecastEngine
{
    public const string Methodology = "productivity/1";
    public const int Version = 1;
    public const int DefaultWindowDays = 21;

    public const string MethodCompleted = "completed";
    public const string MethodWindow = "window";
    public const string MethodOverall = "overall";
    public const string MethodPlanned = "planned";
    public const string MethodNone = "none";

    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static BuildForecastResult Calculate(BuildForecastInput input)
    {
        var today = input.Today;
        var cal = input.Calendar;
        var window = Math.Clamp(input.WindowDays, 3, 180);
        var ordered = input.Stages.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        var eventsByStage = input.Events.GroupBy(e => e.StageId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EffectiveDate).ThenBy(e => e.RecordedAt).ToList());
        var results = new List<BuildForecastStageResult>(ordered.Count);
        var factors = new List<BuildForecastFactor>();
        DateOnly? previousFinish = null;
        string? previousName = null;

        foreach (var s in ordered)
        {
            var events = eventsByStage.GetValueOrDefault(s.StageId) ?? [];
            var actual = Math.Clamp(s.ActualPercent, 0, 100);
            var hasPlan = s.PlannedStart.HasValue && s.PlannedEnd.HasValue;
            var plannedDays = hasPlan ? Math.Max(1, cal.WorkingDaysBetween(Min(s.PlannedStart!.Value, s.PlannedEnd!.Value),
                Max(s.PlannedStart.Value, s.PlannedEnd.Value))) : (int?)null;
            decimal? plannedRate = plannedDays is { } pd ? Round4(100m / pd) : null;

            string method;
            decimal? rate = null;
            var dataPoints = 0;
            DateOnly? fStart = null, fEnd = null;
            var inherited = 0;
            decimal confidence;

            if (s.Status == BuildStageStatus.Completed || actual >= 100)
            {
                method = MethodCompleted;
                fStart = s.ActualStart ?? s.PlannedStart;
                fEnd = s.ActualEnd ?? s.PlannedEnd;
                actual = 100;
                confidence = 1m;
                dataPoints = events.Count;
            }
            else
            {
                var started = actual > 0;
                method = MethodNone;
                if (started)
                {
                    (rate, dataPoints) = WindowRate(s, events, actual, today, window, cal);
                    if (rate is not null) method = MethodWindow;
                    else if (StartOf(s, events, today) is { } start)
                    {
                        rate = Round4((decimal)actual / Math.Max(1, cal.WorkingDaysBetween(start, today)));
                        dataPoints = events.Count;
                        method = MethodOverall;
                    }
                }
                if (rate is null && plannedRate is not null)
                {
                    rate = plannedRate;
                    method = MethodPlanned;
                }

                if (rate is { } r && r > 0m && (started || s.Status == BuildStageStatus.InProgress))
                {
                    var need = (int)Math.Ceiling((100m - actual) / r);
                    fStart = s.ActualStart ?? StartOf(s, events, today) ?? today;
                    fEnd = cal.AddWorkingDays(today, Math.Max(1, need));
                }
                else if (rate is { } r2 && r2 > 0m)
                {
                    // not started: planned duration from the planned start (or the predecessor's finish), never in the past
                    var anchor = Max(s.PlannedStart ?? today, today);
                    if (previousFinish is { } pf && (s.PlannedStart is null || s.PlannedStart.Value <= pf) && anchor <= pf)
                    {
                        var pushed = pf.AddDays(1);
                        inherited = Math.Max(0, pushed.DayNumber - Max(s.PlannedStart ?? today, today).DayNumber);
                        anchor = pushed;
                    }
                    var duration = plannedDays ?? (int)Math.Ceiling(100m / r2);
                    fStart = cal.NextWorkingDay(anchor);
                    fEnd = cal.AddWorkingDays(fStart.Value, Math.Max(0, duration - 1));
                }

                confidence = StageConfidence(method, started, dataPoints, RateVariation(events, cal));
            }

            int? delay = fEnd is { } fe && s.PlannedEnd is { } pe ? fe.DayNumber - pe.DayNumber : null;
            results.Add(new BuildForecastStageResult(s.StageId, s.Name, s.Order, s.Weight,
                (method == MethodCompleted ? BuildStageStatus.Completed : s.Status).ToString(), actual, method, rate is null ? null : Round2(rate.Value),
                plannedRate is null ? null : Round2(plannedRate.Value), dataPoints, s.PlannedStart, s.PlannedEnd, fStart, fEnd, delay,
                inherited, Round2(confidence)));

            // ── factors of this stage ──
            if (method is MethodWindow or MethodOverall && rate is { } observed && plannedRate is { } planned && observed < planned * 0.8m)
                factors.Add(new BuildForecastFactor($"low_productivity:{s.StageId}", "low_productivity",
                    delay is > 15 ? High : delay is > 0 ? Medium : Low, $"Produtividade baixa: {s.Name}",
                    string.Create(PtBr, $"Ritmo de {observed:0.##}% por dia útil contra {planned:0.##}% planejado ({actual}% concluída)."),
                    s.StageId, delay));
            if (inherited > 0)
                factors.Add(new BuildForecastFactor($"inherited_delay:{s.StageId}", "inherited_delay",
                    inherited > 30 ? High : inherited > 7 ? Medium : Low, $"Atraso herdado: {s.Name}",
                    $"Só pode começar depois do término previsto de {previousName} ({inherited} dia(s) após o início planejado).",
                    s.StageId, inherited));
            if (method == MethodPlanned && actual > 0)
                factors.Add(new BuildForecastFactor($"no_productivity_data:{s.StageId}", "no_productivity_data", Low,
                    $"Sem histórico de avanço: {s.Name}", "Etapa iniciada sem eventos de avanço: a previsão usa o ritmo planejado.",
                    s.StageId, null));
            if (method == MethodNone)
                factors.Add(new BuildForecastFactor($"no_forecast:{s.StageId}", "no_forecast", Low, $"Etapa sem previsão: {s.Name}",
                    "Sem datas planejadas nem avanço registrado: cadastre as datas da etapa para entrar na previsão.", s.StageId, null));

            if (fEnd is { } finish)
            {
                previousFinish = finish;
                previousName = s.Name;
            }
        }

        var plannedDelivery = results.Where(r => r.PlannedEnd.HasValue).Select(r => r.PlannedEnd).Max();
        var delivery = results.Where(r => r.ForecastEnd.HasValue).Select(r => r.ForecastEnd).Max();
        int? deliveryDelay = delivery is { } d && plannedDelivery is { } p ? d.DayNumber - p.DayNumber : null;
        if (deliveryDelay is > 0)
            factors.Insert(0, new BuildForecastFactor("delivery_after_plan", "delivery_after_plan", deliveryDelay > 30 ? High : Medium,
                "Entrega prevista depois do planejado",
                string.Create(PtBr, $"Previsão {delivery:dd/MM/yyyy} contra {plannedDelivery:dd/MM/yyyy} planejado ({deliveryDelay} dia(s))."),
                null, deliveryDelay));

        foreach (var co in (input.PendingChangeOrders ?? []).Where(c => c.ScheduleImpactDays > 0).OrderBy(c => c.Number))
        {
            var stageName = co.StageId is { } sid ? ordered.FirstOrDefault(x => x.StageId == sid)?.Name : null;
            factors.Add(new BuildForecastFactor($"pending_change_order:{co.Id}", "pending_change_order",
                co.ScheduleImpactDays > 7 ? Medium : Low, $"Aditivo pendente nº {co.Number}: {co.Title}",
                $"Se aprovado, acrescenta {co.ScheduleImpactDays} dia(s){(stageName is null ? "" : $" à etapa {stageName}")}. "
                + "Ainda sem decisão: não aplicado à previsão.", co.StageId, co.ScheduleImpactDays));
        }

        foreach (var g in (input.WeatherRisks ?? []).GroupBy(w => w.StageId).OrderBy(g => g.Min(w => w.Date)))
        {
            var days = g.Select(w => w.Date).Distinct().OrderBy(x => x).ToList();
            factors.Add(new BuildForecastFactor($"weather_risk:{g.Key}", "weather_risk",
                g.Any(w => w.Level == High) ? Medium : Low, $"Risco climático: {g.First().StageName}",
                "Previsão do tempo desfavorável em " + string.Join(", ", days.Select(x => x.ToString("dd/MM", PtBr)))
                + ". É previsão, não certeza: as datas não foram alteradas.", g.Key, days.Count));
        }

        var open = results.Where(r => r.Method != MethodCompleted && r.Weight > 0m).ToList();
        decimal overall = results.Count == 0 || delivery is null ? 0m
            : open.Count == 0 ? 1m
            : open.Sum(r => r.Weight * r.Confidence) / open.Sum(r => r.Weight);

        return new BuildForecastResult(today, Methodology, Version, delivery, plannedDelivery, deliveryDelay,
            Round2(Math.Clamp(overall, 0m, 1m)), results, factors);
    }

    /// <summary>
    /// Window productivity: progress since the start of the window (or the actual start when later) ÷ its working days, when an
    /// event inside the window moved the stage. Returns the rate and the number of events in the window.
    /// </summary>
    public static (decimal? Rate, int DataPoints) WindowRate(BuildProgressStageInput s, IReadOnlyList<BuildForecastEvent> events, int actual,
        DateOnly today, int windowDays, BuildWorkCalendar cal)
    {
        var windowStart = today.AddDays(-windowDays);   // exclusive
        var inWindow = events.Where(e => e.EffectiveDate > windowStart && e.EffectiveDate <= today).ToList();
        if (!inWindow.Any(e => e.Percent != e.PreviousPercent)) return (null, inWindow.Count);

        var before = events.Where(e => e.EffectiveDate <= windowStart).Select(e => (int?)e.Percent).LastOrDefault()
                     ?? inWindow[0].PreviousPercent;
        var from = windowStart.AddDays(1);
        var start = s.ActualStart ?? events.FirstOrDefault(e => e.Percent > 0)?.EffectiveDate;
        if (start is { } st && st > from) from = st;
        var delta = actual - before;
        if (delta <= 0) return (null, inWindow.Count);
        var days = Math.Max(1, cal.WorkingDaysBetween(from, today));
        return (Round4((decimal)delta / days), inWindow.Count);
    }

    /// <summary>Coefficient of variation of the rates between consecutive events (null with fewer than two intervals).</summary>
    public static decimal? RateVariation(IReadOnlyList<BuildForecastEvent> events, BuildWorkCalendar cal)
    {
        var rates = new List<decimal>();
        for (var i = 1; i < events.Count; i++)
        {
            var a = events[i - 1];
            var b = events[i];
            if (b.EffectiveDate <= a.EffectiveDate) continue;
            var days = Math.Max(1, cal.WorkingDaysBetween(a.EffectiveDate.AddDays(1), b.EffectiveDate));
            rates.Add((decimal)(b.Percent - a.Percent) / days);
        }
        if (rates.Count < 2) return null;
        var mean = rates.Average();
        if (mean <= 0m) return 1m;
        var variance = rates.Sum(r => (r - mean) * (r - mean)) / rates.Count;
        return Round4((decimal)Math.Sqrt((double)variance) / mean);
    }

    public static decimal StageConfidence(string method, bool started, int dataPoints, decimal? variation)
    {
        var baseValue = method switch
        {
            MethodCompleted => 1m,
            MethodWindow => 0.9m,
            MethodOverall => 0.65m,
            MethodPlanned => started ? 0.35m : 0.5m,
            _ => 0m,
        };
        if (method is MethodCompleted or MethodNone) return baseValue;
        var volume = method is MethodWindow or MethodOverall ? Math.Min(1m, dataPoints / 5m) : 0.5m;
        var spread = variation is { } v ? 1m / (1m + Math.Max(0m, v)) : 1m;
        return Math.Clamp(baseValue * (0.6m + 0.4m * volume) * spread, 0m, 1m);
    }

    private static DateOnly? StartOf(BuildProgressStageInput s, IReadOnlyList<BuildForecastEvent> events, DateOnly today)
        => s.ActualStart
           ?? events.FirstOrDefault(e => e.Percent > 0)?.EffectiveDate
           ?? (s.PlannedStart is { } ps && ps <= today ? ps : null);

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
