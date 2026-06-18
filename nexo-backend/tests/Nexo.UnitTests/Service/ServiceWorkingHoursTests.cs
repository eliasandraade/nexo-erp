using System;
using FluentAssertions;
using Nexo.Application.Modules.Service.Public;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>
/// The working-hours parser must be lenient: a malformed column yields an EMPTY schedule (no
/// availability) instead of throwing, so the portal honestly shows "indisponível".
/// </summary>
public class ServiceWorkingHoursTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Blank_or_malformed_is_empty(string? json)
        => ServiceWorkingHours.Parse(json).IsEmpty.Should().BeTrue();

    [Fact]
    public void Parses_valid_windows_per_day()
    {
        var hours = ServiceWorkingHours.Parse(
            "[{\"weekday\":1,\"windows\":[{\"start\":\"09:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"18:00\"}]}]");

        hours.IsEmpty.Should().BeFalse();
        var monday = hours.ForDay(DayOfWeek.Monday);
        monday.Should().HaveCount(2);
        monday[0].Start.Should().Be(new TimeOnly(9, 0));
        monday[0].End.Should().Be(new TimeOnly(12, 0));
        hours.ForDay(DayOfWeek.Tuesday).Should().BeEmpty();
    }

    [Fact]
    public void Drops_invalid_windows_but_keeps_valid_ones()
    {
        var hours = ServiceWorkingHours.Parse(
            "[{\"weekday\":3,\"windows\":[" +
            "{\"start\":\"bad\",\"end\":\"12:00\"}," +   // unparseable start
            "{\"start\":\"15:00\",\"end\":\"14:00\"}," + // end before start
            "{\"start\":\"08:00\",\"end\":\"09:30\"}]}]");

        var wed = hours.ForDay(DayOfWeek.Wednesday);
        wed.Should().ContainSingle();
        wed[0].Start.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public void Ignores_out_of_range_weekday()
        => ServiceWorkingHours.Parse(
            "[{\"weekday\":9,\"windows\":[{\"start\":\"09:00\",\"end\":\"12:00\"}]}]")
            .IsEmpty.Should().BeTrue();
}
