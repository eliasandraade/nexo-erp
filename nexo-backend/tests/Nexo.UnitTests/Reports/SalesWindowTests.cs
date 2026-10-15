using FluentAssertions;
using Nexo.Application.Features.Reports;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Reports;
using Xunit;

namespace Nexo.UnitTests.Reports;

public class SalesWindowTests
{
    [Fact]
    public void Month_to_date_follows_the_brazil_calendar()
    {
        // 01/11 01:30 UTC is still 31/10 22:30 in Brazil — October, not November
        var w = SalesWindow.MonthToDate(new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc));
        w.From.Should().Be(new DateOnly(2026, 10, 1));
        w.To.Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void Days_start_at_midnight_in_brazil()
    {
        var w = new SalesWindow(new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 31));
        w.FromUtc.Should().Be(new DateTime(2026, 3, 31, 3, 0, 0, DateTimeKind.Utc));
        w.ToUtcExclusive.Should().Be(new DateTime(2026, 4, 1, 3, 0, 0, DateTimeKind.Utc));
        w.Days.Should().Be(1);
    }

    [Theory]
    [InlineData("2026-02-30", null)]
    [InlineData("31/03/2026", null)]
    [InlineData("2026-03-31", "2026-03-01")]
    [InlineData("2025-01-01", "2026-03-31")]
    public void Bad_periods_are_refused_with_a_message(string? from, string? to)
    {
        SalesWindow.TryParse(from, to, out var error).Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Missing_ends_default_to_the_month_so_far()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        SalesWindow.TryParse(null, null, out _, now).Should().Be(new SalesWindow(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)));
        SalesWindow.TryParse("2026-10-10", null, out _, now)!.To.Should().Be(new DateOnly(2026, 10, 15));
        SalesWindow.TryParse("2026-01-01", "2026-12-31", out var error, now).Should().NotBeNull(error);
    }

    [Fact]
    public void The_spreadsheet_never_runs_a_name_as_a_formula_and_quotes_separators()
    {
        var row = SalesCsv.Row(new SaleExportRow(7, new DateTime(2026, 4, 1, 2, 30, 0, DateTimeKind.Utc), SaleStatus.Paid,
            "=HYPERLINK(\"x\")", "Ana; Bia", 100m, 10m, 90m, 0m, [PaymentMethod.Pix, PaymentMethod.Cash, PaymentMethod.Pix]));

        row[1].Should().Be("31/03/2026 23:30");
        row[3].Should().Be("'=HYPERLINK(\"x\")");
        row[^1].Should().Be("Pix + Dinheiro");
        SalesCsv.Line(row).Should().Be("7;31/03/2026 23:30;Paga;\"'=HYPERLINK(\"\"x\"\")\";\"Ana; Bia\";100,00;10,00;90,00;0,00;90,00;Pix + Dinheiro");
    }
}
