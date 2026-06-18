using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nexo.Application.Modules.Service.Public;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>
/// Pure-logic coverage for the public booking availability engine. A fixed <c>now</c> and a custom
/// fixed-offset timezone make every assertion deterministic and free of the host's tz database.
/// </summary>
public class AvailabilityCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo Minus3 =
        TimeZoneInfo.CreateCustomTimeZone("T-3", TimeSpan.FromHours(-3), "UTC-3 test", "UTC-3 test");

    // A Monday 08:00Z baseline.
    private static readonly DateTime Now = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

    private static ServiceWorkingHours HoursForDay(DayOfWeek day, string start, string end)
        => ServiceWorkingHours.Parse(
            $"[{{\"weekday\":{(int)day},\"windows\":[{{\"start\":\"{start}\",\"end\":\"{end}\"}}]}}]");

    private static ServiceWorkingHours AllWeek(string start, string end)
    {
        var days = Enumerable.Range(0, 7).Select(d =>
            $"{{\"weekday\":{d},\"windows\":[{{\"start\":\"{start}\",\"end\":\"{end}\"}}]}}");
        return ServiceWorkingHours.Parse("[" + string.Join(",", days) + "]");
    }

    [Fact]
    public void No_working_hours_yields_no_slots()
    {
        var slots = AvailabilityCalculator.GenerateSlots(
            ServiceWorkingHours.Empty, Utc, Now,
            minLeadMinutes: 0, bookingDaysAhead: 7, slotIntervalMinutes: 30, durationMinutes: 60,
            busy: []);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Generates_grid_slots_within_window_sized_to_duration()
    {
        var date = Now.Date.AddDays(1);                       // tomorrow
        var hours = HoursForDay(date.DayOfWeek, "09:00", "12:00");

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Utc, Now,
            minLeadMinutes: 0, bookingDaysAhead: 3, slotIntervalMinutes: 60, durationMinutes: 60,
            busy: []);

        // 09:00, 10:00, 11:00 (11:00 + 60 = 12:00 fits; 12:00 would overflow the window).
        slots.Should().HaveCount(3);
        slots.Should().BeInAscendingOrder();
        slots[0].Should().Be(date.AddHours(9));
        slots[^1].Should().Be(date.AddHours(11));
        slots.Should().AllSatisfy(s => s.Kind.Should().Be(DateTimeKind.Utc));
    }

    [Fact]
    public void Longer_duration_drops_slots_that_no_longer_fit()
    {
        var date = Now.Date.AddDays(1);
        var hours = HoursForDay(date.DayOfWeek, "09:00", "12:00");

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Utc, Now,
            minLeadMinutes: 0, bookingDaysAhead: 3, slotIntervalMinutes: 60, durationMinutes: 120,
            busy: []);

        // Only 09:00 (→11:00) and 10:00 (→12:00) fit a 120-min service.
        slots.Should().HaveCount(2);
        slots[^1].Should().Be(date.AddHours(10));
    }

    [Fact]
    public void Existing_appointment_removes_overlapping_slots()
    {
        var date = Now.Date.AddDays(1);
        var hours = HoursForDay(date.DayOfWeek, "09:00", "12:00");
        var busy = new List<BusyInterval> { new(date.AddHours(10), date.AddHours(11)) };

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Utc, Now,
            minLeadMinutes: 0, bookingDaysAhead: 3, slotIntervalMinutes: 60, durationMinutes: 60,
            busy: busy);

        // 10:00 is taken; 09:00 and 11:00 remain.
        slots.Should().Equal(date.AddHours(9), date.AddHours(11));
    }

    [Fact]
    public void Minimum_lead_excludes_slots_too_soon()
    {
        // now inside today's window so lead actually bites.
        var now = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        var hours = HoursForDay(now.DayOfWeek, "09:00", "12:00");

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Utc, now,
            minLeadMinutes: 60, bookingDaysAhead: 3, slotIntervalMinutes: 60, durationMinutes: 60,
            busy: []);

        // earliest = 10:30 → only 11:00 qualifies today; the weekday doesn't recur within 3 days.
        slots.Should().ContainSingle().Which.Should().Be(now.Date.AddHours(11));
    }

    [Fact]
    public void Days_ahead_horizon_caps_the_range()
    {
        // Working every day; a tight horizon should bound how far slots extend.
        var hours = AllWeek("09:00", "10:00");

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Utc, Now,
            minLeadMinutes: 0, bookingDaysAhead: 2, slotIntervalMinutes: 60, durationMinutes: 60,
            busy: []);

        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(s => s <= Now.AddDays(2));
    }

    [Fact]
    public void Honours_timezone_when_mapping_local_hours_to_utc()
    {
        var date = Now.Date.AddDays(1);
        var hours = HoursForDay(date.DayOfWeek, "09:00", "10:00"); // local UTC-3

        var slots = AvailabilityCalculator.GenerateSlots(
            hours, Minus3, Now,
            minLeadMinutes: 0, bookingDaysAhead: 3, slotIntervalMinutes: 60, durationMinutes: 60,
            busy: []);

        // 09:00 local (UTC-3) == 12:00 UTC.
        slots.Should().ContainSingle().Which.Should().Be(date.AddHours(12));
    }

    [Fact]
    public void IsWithinWorkingHours_accepts_inside_and_rejects_outside()
    {
        var date = Now.Date.AddDays(1);
        var hours = HoursForDay(date.DayOfWeek, "09:00", "12:00");

        AvailabilityCalculator.IsWithinWorkingHours(hours, Utc, date.AddHours(10), date.AddHours(11))
            .Should().BeTrue();
        AvailabilityCalculator.IsWithinWorkingHours(hours, Utc, date.AddHours(13), date.AddHours(14))
            .Should().BeFalse();
        // Spills past the window end.
        AvailabilityCalculator.IsWithinWorkingHours(hours, Utc, date.AddHours(11).AddMinutes(30), date.AddHours(12).AddMinutes(30))
            .Should().BeFalse();
    }

    [Fact]
    public void IsWithinWorkingHours_is_false_without_hours()
    {
        AvailabilityCalculator.IsWithinWorkingHours(
            ServiceWorkingHours.Empty, Utc, Now.AddHours(1), Now.AddHours(2)).Should().BeFalse();
    }
}
