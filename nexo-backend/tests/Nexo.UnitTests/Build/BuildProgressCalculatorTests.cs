using FluentAssertions;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>Methodology "weighted-stages/1": weights, stages without dates, forecast.</summary>
public class BuildProgressCalculatorTests
{
    private static readonly DateOnly D1 = new(2026, 1, 1);

    private static BuildProgressStageInput Stage(
        string name, int order, decimal weight, int percent,
        DateOnly? plannedStart = null, DateOnly? plannedEnd = null,
        DateOnly? actualStart = null, DateOnly? actualEnd = null,
        BuildStageStatus? status = null)
        => new(Guid.NewGuid(), name, order, weight, percent,
            status ?? (percent >= 100 ? BuildStageStatus.Completed : percent > 0 ? BuildStageStatus.InProgress : BuildStageStatus.Pending),
            plannedStart, plannedEnd, actualStart, actualEnd);

    [Fact]
    public void Overall_actual_is_the_weighted_mean_of_the_stages()
    {
        var r = BuildProgressCalculator.Calculate(
        [
            Stage("Fundação", 1, 3m, 100, actualEnd: D1),
            Stage("Alvenaria", 2, 1m, 0),
        ], D1);

        r.ActualPercent.Should().Be(75m, "(3·100 + 1·0) / 4");
        r.Methodology.Should().Be("weighted-stages/1");
    }

    [Fact]
    public void Equal_weights_are_a_plain_average()
    {
        var r = BuildProgressCalculator.Calculate([Stage("A", 1, 1m, 40), Stage("B", 2, 1m, 60)], D1);
        r.ActualPercent.Should().Be(50m);
    }

    [Fact]
    public void Planned_is_linear_between_the_planned_dates_and_ignores_stages_without_dates()
    {
        var today = D1.AddDays(4); // 5th day of a 10-day stage
        var r = BuildProgressCalculator.Calculate(
        [
            Stage("Fundação", 1, 1m, 30, D1, D1.AddDays(9), actualStart: D1),
            Stage("Acabamento", 2, 5m, 0),
        ], today);

        r.PlannedPercent.Should().Be(50m, "only the dated stage counts in the planned figure");
        var undated = r.Stages.Single(s => s.Name == "Acabamento");
        undated.PlannedPercent.Should().BeNull();
        undated.Flags.Should().Contain(BuildProgressCalculator.FlagMissingPlannedDates);
        r.Warnings.Should().ContainSingle(w => w.Contains("Acabamento"));
    }

    [Fact]
    public void Without_any_planned_dates_there_is_no_planned_figure()
    {
        var r = BuildProgressCalculator.Calculate([Stage("A", 1, 1m, 10)], D1);
        r.PlannedPercent.Should().BeNull();
        r.PlannedDelivery.Should().BeNull();
        r.ForecastPercent.Should().BeNull();
    }

    [Fact]
    public void No_stages_means_zero_actual_and_a_warning()
    {
        var r = BuildProgressCalculator.Calculate([], D1);
        r.ActualPercent.Should().Be(0m);
        r.PlannedPercent.Should().BeNull();
        r.ForecastDelivery.Should().BeNull();
        r.Warnings.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 10)]
    [InlineData(4, 50)]
    [InlineData(9, 100)]
    [InlineData(30, 100)]
    public void Stage_planned_percent_is_inclusive_by_calendar_day(int dayOffset, decimal expected)
        => BuildProgressCalculator.StagePlannedPercent(D1, D1.AddDays(9), D1.AddDays(dayOffset)).Should().Be(expected);

    [Fact]
    public void In_progress_stage_is_forecast_from_its_observed_rate()
    {
        var today = D1.AddDays(9); // 10 days elapsed, 50% done → 5%/day → 10 more days
        var r = BuildProgressCalculator.Calculate(
            [Stage("Estrutura", 1, 1m, 50, D1, D1.AddDays(9), actualStart: D1)], today);

        var s = r.Stages.Single();
        s.ForecastEnd.Should().Be(today.AddDays(10));
        s.DelayDays.Should().Be(10);
        r.ForecastDelivery.Should().Be(today.AddDays(10));
        BuildProgressCalculator.ForecastPercentAt(r, today.AddDays(10)).Should().Be(100m);
        BuildProgressCalculator.ForecastPercentAt(r, today.AddDays(5)).Should().Be(75m);
        BuildProgressCalculator.ForecastPercentAt(r, today).Should().Be(r.ActualPercent, "today the forecast is the actual");
    }

    [Fact]
    public void Not_started_stages_are_shifted_by_the_largest_delay_of_the_earlier_ones()
    {
        var today = D1.AddDays(9);
        var r = BuildProgressCalculator.Calculate(
        [
            Stage("Estrutura", 1, 1m, 50, D1, D1.AddDays(9), actualStart: D1),        // forecast +10 days
            Stage("Cobertura", 2, 1m, 0, D1.AddDays(10), D1.AddDays(19)),           // planned 11th..20th
        ], today);

        var next = r.Stages.Single(s => s.Name == "Cobertura");
        next.ForecastStart.Should().Be(D1.AddDays(20));
        next.ForecastEnd.Should().Be(D1.AddDays(29));
        next.DelayDays.Should().Be(10);
        r.ForecastDelivery.Should().Be(D1.AddDays(29));
        r.PlannedDelivery.Should().Be(D1.AddDays(19));
    }

    [Fact]
    public void A_stage_that_should_have_started_starts_today_at_the_earliest()
    {
        var today = D1.AddDays(9);
        var r = BuildProgressCalculator.Calculate([Stage("Pintura", 1, 1m, 0, D1, D1.AddDays(4))], today);
        var s = r.Stages.Single();
        s.ForecastStart.Should().Be(today);
        s.ForecastEnd.Should().Be(today.AddDays(4));
        s.DelayDays.Should().Be(9);
    }

    [Fact]
    public void Completed_stage_forecast_is_its_actual_end_and_counts_as_100()
    {
        var r = BuildProgressCalculator.Calculate(
            [Stage("Fundação", 1, 1m, 100, D1, D1.AddDays(9), D1, D1.AddDays(12))], D1.AddDays(20));
        var s = r.Stages.Single();
        s.ActualPercent.Should().Be(100m);
        s.ForecastEnd.Should().Be(D1.AddDays(12));
        s.DelayDays.Should().Be(3);
        r.ForecastDelivery.Should().Be(D1.AddDays(12));
    }

    [Fact]
    public void Forecast_percent_is_where_the_project_will_be_on_the_planned_delivery()
    {
        var today = D1.AddDays(9);
        var r = BuildProgressCalculator.Calculate(
            [Stage("Estrutura", 1, 1m, 50, D1, D1.AddDays(14), actualStart: D1)], today);
        // planned delivery = day 15 (5 days ahead); forecast end = today + 10 → halfway of the remaining 50%
        r.ForecastPercent.Should().Be(75m);
    }

    [Fact]
    public void Stage_without_dates_nor_progress_has_no_forecast_and_is_flagged()
    {
        var r = BuildProgressCalculator.Calculate([Stage("Extra", 1, 1m, 0)], D1);
        r.Stages.Single().Flags.Should().Contain(BuildProgressCalculator.FlagNoForecast);
        r.Warnings.Should().Contain(w => w.StartsWith("Previsão parcial"));
    }

    [Fact]
    public void Planned_percent_at_a_date_uses_only_dated_stages()
    {
        var stages = new[]
        {
            Stage("A", 1, 1m, 0, D1, D1.AddDays(9)),
            Stage("B", 2, 3m, 0, D1.AddDays(10), D1.AddDays(19)),
            Stage("C", 3, 10m, 0),
        };
        BuildProgressCalculator.PlannedPercentAt(stages, D1.AddDays(9)).Should().Be(25m, "A done (w1), B not started (w3)");
        BuildProgressCalculator.PlannedPercentAt(stages, D1.AddDays(19)).Should().Be(100m);
    }
}
