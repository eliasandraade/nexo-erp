using FluentAssertions;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>Wave 6: working-day calendar, productivity forecast (productivity/1) and weather rules.</summary>
public class BuildForecastEngineTests
{
    private static readonly BuildWorkCalendar Cal = BuildWorkCalendar.Default;
    /// <summary>Friday 13/11/2026.</summary>
    private static readonly DateOnly T = new(2026, 11, 13);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static BuildProgressStageInput Stage(string name, int order, int percent, DateOnly? plannedStart = null, DateOnly? plannedEnd = null,
        DateOnly? actualStart = null, decimal weight = 1m, Guid? id = null, BuildStageStatus? status = null)
        => new(id ?? Guid.NewGuid(), name, order, weight, percent,
            status ?? (percent >= 100 ? BuildStageStatus.Completed : percent > 0 ? BuildStageStatus.InProgress : BuildStageStatus.Pending),
            plannedStart, plannedEnd, actualStart, percent >= 100 ? actualStart : null);

    private static BuildForecastEvent Ev(Guid stage, DateOnly date, int from, int to) => new(stage, date, date.ToDateTime(TimeOnly.MinValue), from, to);

    private static BuildForecastResult Run(IReadOnlyList<BuildProgressStageInput> stages, IReadOnlyList<BuildForecastEvent>? events = null,
        IReadOnlyList<BuildForecastChangeOrder>? orders = null, IReadOnlyList<BuildForecastWeatherRisk>? weather = null)
        => BuildForecastEngine.Calculate(new BuildForecastInput(T, stages, events ?? [], Cal, 21, orders, weather));

    // ── Calendar ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sundays_and_national_holidays_are_not_working_days_and_saturday_is()
    {
        Cal.IsWorkingDay(D(11, 8)).Should().BeFalse("Sunday");
        Cal.IsWorkingDay(D(11, 7)).Should().BeTrue("Saturday is worked by default");
        Cal.IsWorkingDay(D(11, 2)).Should().BeFalse("Finados");
        Cal.IsWorkingDay(D(11, 20)).Should().BeFalse("Consciência Negra");
        Cal.IsWorkingDay(D(4, 3)).Should().BeFalse("Sexta-feira Santa 2026");
        Cal.IsWorkingDay(new DateOnly(2027, 3, 26)).Should().BeFalse("Sexta-feira Santa 2027");
        Cal.IsWorkingDay(new DateOnly(2027, 12, 25)).Should().BeFalse("Natal");
        BrazilNationalHolidays.For(2026).Should().HaveCount(10);
    }

    [Fact]
    public void Working_days_are_counted_inclusive_and_added_skipping_sundays_and_holidays()
    {
        Cal.WorkingDaysBetween(D(11, 9), D(11, 15)).Should().Be(6, "Monday to Saturday");
        Cal.WorkingDaysBetween(D(11, 16), D(11, 22)).Should().Be(5, "the 20th is a holiday");
        Cal.WorkingDaysBetween(D(11, 22), D(11, 16)).Should().Be(0);
        Cal.AddWorkingDays(D(11, 19), 1).Should().Be(D(11, 21), "skips the holiday of the 20th");
        Cal.AddWorkingDays(D(11, 14), 1).Should().Be(D(11, 16), "skips Sunday");
        Cal.AddWorkingDays(D(11, 14), 0).Should().Be(D(11, 14));
        Cal.NextWorkingDay(D(11, 15)).Should().Be(D(11, 16));
    }

    [Fact]
    public void Working_days_are_configurable()
    {
        BuildWorkCalendar.ParseDays("seg,ter,qua,qui,sex").Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday);
        BuildWorkCalendar.ParseDays("Mon, Sat").Should().Equal(DayOfWeek.Monday, DayOfWeek.Saturday);
        BuildWorkCalendar.ParseDays("").Should().Equal(BuildWorkCalendar.DefaultWorkingDays);
        BuildWorkCalendar.ParseDays("xyz").Should().Equal(BuildWorkCalendar.DefaultWorkingDays);
        var weekdays = new BuildWorkCalendar(BuildWorkCalendar.ParseDays("1,2,3,4,5"), BrazilNationalHolidays.All());
        weekdays.IsWorkingDay(D(11, 7)).Should().BeFalse("Saturday off in a Mon–Fri calendar");
        new BuildWorkCalendar([], null).WorkingDays.Should().HaveCount(6, "an empty calendar falls back to Mon–Sat");
    }

    // ── Productivity ──────────────────────────────────────────────────────────

    [Fact]
    public void Productivity_comes_from_the_recent_window_in_working_days()
    {
        var id = Guid.NewGuid();
        var stage = Stage("Alvenaria", 1, 50, D(10, 1), D(12, 31), D(10, 1), id: id);
        var events = new[] { Ev(id, D(10, 5), 0, 10), Ev(id, D(10, 20), 10, 20), Ev(id, D(10, 27), 20, 30), Ev(id, D(11, 10), 30, 50) };

        var r = Run([stage], events);
        var s = r.Stages.Single();

        s.Method.Should().Be("window");
        var windowDays = Cal.WorkingDaysBetween(D(10, 24), T);
        windowDays.Should().Be(17);
        s.RatePerWorkingDay.Should().Be(Math.Round(30m / 17m, 2), "30 points (20% → 50%) over the 17 working days of the window");
        s.DataPoints.Should().Be(2);
        s.ForecastEnd.Should().Be(Cal.AddWorkingDays(T, (int)Math.Ceiling(50m / Math.Round(30m / 17m, 4))));
        Cal.IsWorkingDay(s.ForecastEnd!.Value).Should().BeTrue();
        r.ForecastDelivery.Should().Be(s.ForecastEnd);
        r.Methodology.Should().Be("productivity/1");
    }

    [Fact]
    public void Without_recent_events_productivity_is_the_overall_rate_since_the_actual_start()
    {
        var id = Guid.NewGuid();
        var r = Run([Stage("Fundação", 1, 40, D(9, 1), D(12, 1), D(9, 1), id: id)], [Ev(id, D(9, 20), 0, 40)]);
        var s = r.Stages.Single();
        s.Method.Should().Be("overall");
        s.RatePerWorkingDay.Should().Be(Math.Round(40m / Cal.WorkingDaysBetween(D(9, 1), T), 2));
    }

    [Fact]
    public void Without_any_data_the_planned_rate_is_used_and_flagged()
    {
        var notStarted = Run([Stage("Cobertura", 1, 0, D(12, 1), D(12, 12))]).Stages.Single();
        notStarted.Method.Should().Be("planned");
        notStarted.ForecastStart.Should().Be(D(12, 1));
        notStarted.ForecastEnd.Should().Be(D(12, 12), "same working days as planned, nothing inherited");
        notStarted.DelayDays.Should().Be(0);

        var startedWithoutHistory = Run([Stage("Elétrica", 1, 20, D(12, 1), D(12, 12))]);
        startedWithoutHistory.Stages.Single().Method.Should().Be("planned");
        startedWithoutHistory.Factors.Should().Contain(f => f.Kind == "no_productivity_data");

        var nothing = Run([Stage("Sem datas", 1, 0)]);
        nothing.Stages.Single().Method.Should().Be("none");
        nothing.ForecastDelivery.Should().BeNull();
        nothing.Confidence.Should().Be(0m);
        nothing.Factors.Should().Contain(f => f.Kind == "no_forecast");
    }

    [Fact]
    public void A_stage_that_has_not_started_waits_for_the_previous_stage_and_inherits_its_delay()
    {
        var a = Guid.NewGuid();
        // A: 20% after 20 working days of a 40-day plan → slow; B planned to start right after A's planned end
        var stages = new[]
        {
            Stage("Estrutura", 1, 20, D(10, 15), D(11, 20), D(10, 15), id: a),
            Stage("Alvenaria", 2, 0, D(11, 21), D(12, 5)),
            Stage("Pintura", 3, 0, new DateOnly(2027, 6, 1), new DateOnly(2027, 6, 10)),
        };
        var r = Run(stages, [Ev(a, D(10, 30), 0, 10), Ev(a, D(11, 12), 10, 20)]);
        var est = r.Stages[0];
        var alv = r.Stages[1];
        var pin = r.Stages[2];

        est.ForecastEnd.Should().BeAfter(D(11, 21));
        alv.ForecastStart.Should().Be(Cal.NextWorkingDay(est.ForecastEnd!.Value.AddDays(1)));
        alv.InheritedDelayDays.Should().BeGreaterThan(0);
        var plannedWorkingDays = Cal.WorkingDaysBetween(D(11, 21), D(12, 5));
        Cal.WorkingDaysBetween(alv.ForecastStart!.Value, alv.ForecastEnd!.Value).Should().Be(plannedWorkingDays, "keeps its planned duration");
        r.Factors.Should().Contain(f => f.Key == $"inherited_delay:{alv.StageId}");
        r.Factors.Should().Contain(f => f.Key == $"low_productivity:{a}");

        pin.ForecastStart.Should().Be(new DateOnly(2027, 6, 1), "planned start after the predecessor's finish: unchanged");
        pin.InheritedDelayDays.Should().Be(0);
        r.ForecastDelivery.Should().Be(new[] { est.ForecastEnd, alv.ForecastEnd, pin.ForecastEnd }.Max());
        r.DelayDays.Should().Be(r.ForecastDelivery!.Value.DayNumber - new DateOnly(2027, 6, 10).DayNumber);
    }

    [Fact]
    public void A_late_not_started_stage_never_starts_in_the_past()
    {
        var s = Run([Stage("Fundação", 1, 0, D(10, 1), D(10, 10))]).Stages.Single();
        s.ForecastStart.Should().Be(Cal.NextWorkingDay(T));
        s.DelayDays.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Completed_stages_keep_their_actual_end_and_full_confidence()
    {
        var r = Run([Stage("Fundação", 1, 100, D(9, 1), D(9, 30), D(9, 1))]);
        r.Stages.Single().Method.Should().Be("completed");
        r.Stages.Single().ForecastEnd.Should().Be(D(9, 1));
        r.Confidence.Should().Be(1m);
    }

    [Fact]
    public void Confidence_stays_between_0_and_1_and_grows_with_data_and_consistency()
    {
        var steady = Guid.NewGuid();
        var erratic = Guid.NewGuid();
        var steadyEvents = Enumerable.Range(0, 6).Select(i => Ev(steady, D(10, 26).AddDays(i * 3), i * 6, (i + 1) * 6)).ToList();
        var erraticEvents = new[] { Ev(erratic, D(10, 26), 0, 2), Ev(erratic, D(10, 29), 2, 30), Ev(erratic, D(11, 5), 30, 31), Ev(erratic, D(11, 11), 31, 36) };

        var a = Run([Stage("A", 1, 36, D(10, 26), D(12, 20), D(10, 26), id: steady)], steadyEvents);
        var b = Run([Stage("B", 1, 36, D(10, 26), D(12, 20), D(10, 26), id: erratic)], erraticEvents);
        var c = Run([Stage("C", 1, 0, D(12, 1), D(12, 20))]);

        foreach (var r in new[] { a, b, c }) r.Confidence.Should().BeInRange(0m, 1m);
        a.Confidence.Should().BeGreaterThan(b.Confidence, "consistent rates are more reliable");
        a.Confidence.Should().BeGreaterThan(c.Confidence, "observed productivity beats the plan alone");
        Run([]).Confidence.Should().Be(0m);
        BuildForecastEngine.StageConfidence("window", true, 100, 0m).Should().BeLessThanOrEqualTo(1m);
        BuildForecastEngine.StageConfidence("window", true, 0, 50m).Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Pending_change_orders_and_weather_are_factors_that_never_move_the_dates()
    {
        var id = Guid.NewGuid();
        var stages = new[] { Stage("Concretagem", 1, 30, D(10, 20), D(12, 1), D(10, 20), id: id) };
        var events = new[] { Ev(id, D(11, 2), 0, 15), Ev(id, D(11, 11), 15, 30) };
        var plain = Run(stages, events);
        var withRisks = Run(stages, events,
            [new BuildForecastChangeOrder(Guid.NewGuid(), 3, "Laje extra", "SentToClient", 10, id)],
            [new BuildForecastWeatherRisk(D(11, 16), id, "Concretagem", "high", "Chuva forte prevista")]);

        withRisks.ForecastDelivery.Should().Be(plain.ForecastDelivery);
        withRisks.Stages.Single().ForecastEnd.Should().Be(plain.Stages.Single().ForecastEnd);
        withRisks.Factors.Should().Contain(f => f.Kind == "pending_change_order" && f.Days == 10 && f.Evidence.Contains("não aplicado"));
        withRisks.Factors.Should().Contain(f => f.Kind == "weather_risk" && f.StageId == id && f.Evidence.Contains("não certeza"));
    }

    // ── Weather rules ─────────────────────────────────────────────────────────

    private static BuildWeatherDay Day(double? mm, int? prob, double? wind = null) => new(T, 30, 20, mm, prob, wind, null);

    [Theory]
    [InlineData(12.0, 80, "heavy_rain", "high")]
    [InlineData(10.0, 70, "heavy_rain", "high")]
    [InlineData(12.0, 50, "rain", "medium")]
    [InlineData(3.0, 65, "rain", "medium")]
    [InlineData(9.9, 69, "rain", "medium")]
    public void Rain_thresholds(double mm, int prob, string kind, string level)
    {
        var c = BuildWeatherRules.Conditions(Day(mm, prob)).Single();
        c.Kind.Should().Be(kind);
        c.Level.Should().Be(level);
    }

    [Fact]
    public void Light_rain_or_low_probability_is_no_risk_and_wind_has_its_own_thresholds()
    {
        BuildWeatherRules.Conditions(Day(3, 40)).Should().BeEmpty();
        BuildWeatherRules.Conditions(Day(1, 90)).Should().BeEmpty();
        BuildWeatherRules.Conditions(Day(0, null, 45)).Single().Should().Be(new BuildWeatherCondition("strong_wind", "medium", "Vento forte previsto (até 45 km/h)"));
        BuildWeatherRules.Conditions(Day(0, null, 55)).Single().Level.Should().Be("high");
        BuildWeatherRules.Level(BuildWeatherRules.Conditions(Day(12, 80, 45))).Should().Be("high");
        BuildWeatherRules.Level([]).Should().Be("none");
    }

    [Theory]
    [InlineData("Concretagem da laje", true)]
    [InlineData("FUNDACAO", true)]
    [InlineData("Fundação e baldrame", true)]
    [InlineData("Pintura externa", true)]
    [InlineData("Impermeabilização do terraço", true)]
    [InlineData("Telhado", true)]
    [InlineData("Pintura interna", false)]
    [InlineData("Instalações elétricas", false)]
    public void Sensitivity_by_keyword(string name, bool sensitive)
        => BuildWeatherRules.Sensitivity(name, null).Sensitive.Should().Be(sensitive);

    [Fact]
    public void Explicit_sensitivity_wins_over_keywords_and_keywords_are_configurable()
    {
        BuildWeatherRules.Sensitivity("Concretagem", false).Should().Be((false, "explicit", (string?)null));
        BuildWeatherRules.Sensitivity("Elétrica", true).Sensitive.Should().BeTrue();
        BuildWeatherRules.Sensitivity("Paisagismo", null, ["paisagismo"]).Should().Be((true, "keyword", "paisagismo"));
        BuildWeatherRules.Sensitivity("Concretagem", null, ["paisagismo"]).Sensitive.Should().BeFalse();
    }

    [Fact]
    public void Risks_are_raised_only_for_sensitive_stages_in_the_field_and_say_it_is_a_forecast()
    {
        var concrete = new BuildWeatherStage(Guid.NewGuid(), "Concretagem da laje", true, T, T.AddDays(5));
        var later = new BuildWeatherStage(Guid.NewGuid(), "Cobertura", true, T.AddDays(10), T.AddDays(20));
        var indoor = new BuildWeatherStage(Guid.NewGuid(), "Pintura interna", false, T, T.AddDays(5));
        var risks = BuildWeatherRules.Risks(Day(15, 85), [concrete, later, indoor]);
        risks.Should().ContainSingle();
        risks[0].Stage.Should().Be(concrete);
        risks[0].Message.Should().StartWith("Chuva forte prevista (15 mm, probabilidade 85%) — etapa Concretagem da laje").And.Contain("Previsão");
        BuildWeatherRules.Risks(Day(0, 0), [concrete]).Should().BeEmpty();
    }
}
