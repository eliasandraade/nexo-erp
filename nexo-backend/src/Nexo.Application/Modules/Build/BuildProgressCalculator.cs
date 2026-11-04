using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>One stage as the calculator sees it: weight and planned dates already resolved from the reference plan.</summary>
public sealed record BuildProgressStageInput(
    Guid             StageId,
    string           Name,
    int              Order,
    decimal          Weight,
    int              ActualPercent,
    BuildStageStatus Status,
    DateOnly?        PlannedStart,
    DateOnly?        PlannedEnd,
    DateOnly?        ActualStart,
    DateOnly?        ActualEnd);

public sealed record BuildProgressStageResult(
    Guid             StageId,
    string           Name,
    int              Order,
    decimal          Weight,
    string           Status,
    decimal?         PlannedPercent,
    decimal          ActualPercent,
    DateOnly?        PlannedStart,
    DateOnly?        PlannedEnd,
    DateOnly?        ActualStart,
    DateOnly?        ActualEnd,
    DateOnly?        ForecastStart,
    DateOnly?        ForecastEnd,
    int?             DelayDays,
    IReadOnlyList<string> Flags);

public sealed record BuildProgressResult(
    DateOnly         Date,
    decimal?         PlannedPercent,
    decimal          ActualPercent,
    decimal?         ForecastPercent,
    DateOnly?        PlannedDelivery,
    DateOnly?        ForecastDelivery,
    IReadOnlyList<BuildProgressStageResult> Stages,
    IReadOnlyList<string> Warnings,
    string           Methodology);

/// <summary>
/// Deterministic, pure progress engine — methodology <c>weighted-stages/1</c>. Nobody types the overall %.
///
/// <list type="bullet">
/// <item><b>Planned % of a stage at date d</b>: linear over the planned calendar days, inclusive —
///   0 before the planned start, (d − start + 1) / (end − start + 1) between, 100 from the planned end on.
///   Stages without both planned dates are left out of the planned figure and flagged <c>missing_planned_dates</c>.</item>
/// <item><b>Actual % of a stage</b>: its recorded progress (0–100).</item>
/// <item><b>Overall</b> = Σ(weight·%) / Σweight — actual over every stage, planned over the dated stages.</item>
/// <item><b>Forecast end of a stage</b>: completed → actual end; in progress with a start and progress &gt; 0 →
///   today + remaining / observed daily rate (progress ÷ days since start); not started → planned window
///   shifted by the largest delay of the earlier stages (by order), and never starting before today.
///   Delay of a stage = forecast end − planned end (days).</item>
/// <item><b>Forecast delivery</b> = the latest forecast end. <b>Forecast %</b> = where the project will be on the
///   planned delivery date at this pace (today's actual when that date has passed); stages without a forecast
///   stay at their actual %.</item>
/// </list>
/// </summary>
public static class BuildProgressCalculator
{
    public const string Methodology = "weighted-stages/1";

    public const string FlagMissingPlannedDates = "missing_planned_dates";
    public const string FlagNoActualStart       = "no_actual_start";
    public const string FlagNoForecast          = "no_forecast";

    public static BuildProgressResult Calculate(IReadOnlyList<BuildProgressStageInput> stages, DateOnly today)
    {
        var ordered  = stages.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        var results  = new List<BuildProgressStageResult>(ordered.Count);
        var warnings = new List<string>();
        var maxDelay = 0;

        foreach (var s in ordered)
        {
            var flags     = new List<string>();
            var hasPlan   = s.PlannedStart.HasValue && s.PlannedEnd.HasValue;
            if (!hasPlan) flags.Add(FlagMissingPlannedDates);
            var planned   = hasPlan ? StagePlannedPercent(s.PlannedStart!.Value, s.PlannedEnd!.Value, today) : (decimal?)null;
            var actual    = (decimal)Math.Clamp(s.ActualPercent, 0, 100);
            var completed = IsCompleted(s);

            DateOnly? fStart = null, fEnd = null;
            if (completed)
            {
                fStart = s.ActualStart ?? s.PlannedStart;
                fEnd   = s.ActualEnd ?? s.PlannedEnd;
                actual = 100m;
            }
            else if (s.ActualPercent > 0 && StartForRate(s, today) is { } start)
            {
                var elapsed   = Math.Max(1, today.DayNumber - start.DayNumber + 1);
                var rate      = (decimal)s.ActualPercent / elapsed;              // % per day
                var remaining = (int)Math.Ceiling((100m - s.ActualPercent) / rate);
                fStart = start;
                fEnd   = today.AddDays(Math.Max(1, remaining));
                if (s.ActualStart is null) flags.Add(FlagNoActualStart);
            }
            else if (hasPlan)
            {
                if (s.ActualPercent > 0) flags.Add(FlagNoActualStart);
                var duration = s.PlannedEnd!.Value.DayNumber - s.PlannedStart!.Value.DayNumber;
                var shifted  = s.PlannedStart.Value.AddDays(maxDelay);
                if (shifted < today) shifted = today; // should have started already: starts today at the earliest
                fStart = shifted;
                fEnd   = shifted.AddDays(Math.Max(0, duration));
            }
            else
            {
                flags.Add(FlagNoForecast);
            }

            int? delay = fEnd.HasValue && s.PlannedEnd.HasValue ? fEnd.Value.DayNumber - s.PlannedEnd.Value.DayNumber : null;
            if (delay is > 0) maxDelay = Math.Max(maxDelay, delay.Value);

            results.Add(new BuildProgressStageResult(
                s.StageId, s.Name, s.Order, s.Weight, (completed ? BuildStageStatus.Completed : s.Status).ToString(),
                planned is null ? null : Round(planned.Value), Round(actual),
                s.PlannedStart, s.PlannedEnd, s.ActualStart, s.ActualEnd, fStart, fEnd, delay, flags));
        }

        var missing = results.Where(r => r.Flags.Contains(FlagMissingPlannedDates)).Select(r => r.Name).ToList();
        if (results.Count == 0) warnings.Add("A obra ainda não tem etapas: cadastre as etapas para medir o avanço.");
        else if (missing.Count > 0)
            warnings.Add($"Etapas sem datas previstas ficam fora do planejado: {string.Join(", ", missing)}.");
        if (results.Any(r => !IsCompletedStatus(r) && r.ForecastEnd is null))
            warnings.Add("Previsão parcial: há etapas sem datas nem avanço para estimar o término.");

        var plannedDelivery = results.Where(r => r.PlannedEnd.HasValue).Select(r => r.PlannedEnd).Max();
        DateOnly? forecastDelivery = results.Count == 0 ? null : results.Where(r => r.ForecastEnd.HasValue).Select(r => r.ForecastEnd).Max();

        var partial = new BuildProgressResult(today, null, 0m, null, plannedDelivery, forecastDelivery, results, warnings, Methodology);
        var plannedOverall = PlannedPercentAt(ordered, today);
        var actualOverall  = Weighted(results.Select(r => (r.Weight, r.ActualPercent)));
        decimal? forecastPct = plannedDelivery is null ? null
            : ForecastPercentAt(partial, plannedDelivery.Value < today ? today : plannedDelivery.Value);

        return partial with
        {
            PlannedPercent  = plannedOverall,
            ActualPercent   = actualOverall,
            ForecastPercent = forecastPct,
        };
    }

    /// <summary>Overall planned % at <paramref name="date"/> over the stages with both planned dates (null when none).</summary>
    public static decimal? PlannedPercentAt(IEnumerable<BuildProgressStageInput> stages, DateOnly date)
    {
        var dated = stages.Where(s => s.PlannedStart.HasValue && s.PlannedEnd.HasValue).ToList();
        if (dated.Count == 0) return null;
        return Weighted(dated.Select(s => (s.Weight, StagePlannedPercent(s.PlannedStart!.Value, s.PlannedEnd!.Value, date))));
    }

    /// <summary>
    /// Overall forecast % at <paramref name="date"/> (on or after the calculation date): each stage moves
    /// linearly from today's actual % to 100% at its forecast end; stages without a forecast keep their actual %.
    /// </summary>
    public static decimal ForecastPercentAt(BuildProgressResult result, DateOnly date)
    {
        var today = result.Date;
        return Weighted(result.Stages.Select(s => (s.Weight, StageForecastPercent(s, today, date))));
    }

    public static decimal StagePlannedPercent(DateOnly start, DateOnly end, DateOnly date)
    {
        if (end < start) (start, end) = (end, start);
        if (date < start) return 0m;
        if (date >= end) return 100m;
        var elapsed  = date.DayNumber - start.DayNumber + 1;
        var duration = end.DayNumber - start.DayNumber + 1;
        return Round(100m * elapsed / duration);
    }

    private static decimal StageForecastPercent(BuildProgressStageResult s, DateOnly today, DateOnly date)
    {
        if (IsCompletedStatus(s) || s.ActualPercent >= 100m) return 100m;
        if (date <= today || s.ForecastEnd is null) return s.ActualPercent;
        var end = s.ForecastEnd.Value;
        if (date >= end) return 100m;
        // anchor at today's actual % (or at the shifted start for a stage that has not begun yet)
        var anchor = s.ForecastStart is { } fs && fs > today ? fs : today;
        if (date < anchor) return s.ActualPercent;
        var span = end.DayNumber - anchor.DayNumber;
        if (span <= 0) return 100m;
        return Round(s.ActualPercent + (100m - s.ActualPercent) * (date.DayNumber - anchor.DayNumber) / span);
    }

    private static DateOnly? StartForRate(BuildProgressStageInput s, DateOnly today)
        => s.ActualStart ?? (s.PlannedStart is { } ps && ps <= today ? ps : null);

    private static bool IsCompleted(BuildProgressStageInput s)
        => s.Status == BuildStageStatus.Completed || s.ActualPercent >= 100;

    private static bool IsCompletedStatus(BuildProgressStageResult s)
        => s.Status == nameof(BuildStageStatus.Completed);

    private static decimal Weighted(IEnumerable<(decimal Weight, decimal Percent)> items)
    {
        decimal sumW = 0m, sumWp = 0m;
        foreach (var (w, p) in items)
        {
            if (w <= 0m) continue;
            sumW  += w;
            sumWp += w * p;
        }
        return sumW == 0m ? 0m : Round(sumWp / sumW);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
