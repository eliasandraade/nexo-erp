using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.UnitTests.Financial;

public class FinancialRecurrenceTests
{
    private static FinancialRecurrence Rent(int day = 31, DateOnly? start = null, DateOnly? end = null)
        => FinancialRecurrence.Create(Guid.NewGuid(), TransactionType.Payable, FinancialCategory.OperatingExpense,
            "Aluguel", 1800m, day, start ?? new DateOnly(2026, 1, 15), end, Guid.NewGuid());

    [Fact]
    public void Pending_months_run_from_the_start_through_the_horizon_and_never_twice()
    {
        var r = Rent();
        r.StartMonth.Should().Be(new DateOnly(2026, 1, 1));
        var months = r.PendingMonths(new DateOnly(2026, 3, 1));
        months.Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1));

        r.MarkGenerated(months[^1]);
        r.PendingMonths(new DateOnly(2026, 3, 1)).Should().BeEmpty();
        r.PendingMonths(new DateOnly(2026, 4, 1)).Should().Equal(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void The_end_month_and_deactivation_stop_generation()
    {
        Rent(end: new DateOnly(2026, 2, 10)).PendingMonths(new DateOnly(2026, 6, 1)).Should().HaveCount(2);
        var r = Rent();
        r.Deactivate();
        r.PendingMonths(new DateOnly(2026, 6, 1)).Should().BeEmpty();
    }

    [Fact]
    public void A_due_day_past_the_months_end_falls_on_its_last_day()
    {
        var r = Rent(day: 31);
        r.DueDayIn(new DateOnly(2026, 2, 1)).Should().Be(new DateOnly(2026, 2, 28));
        r.DueDayIn(new DateOnly(2026, 4, 1)).Should().Be(new DateOnly(2026, 4, 30));
        r.DueDayIn(new DateOnly(2026, 5, 1)).Should().Be(new DateOnly(2026, 5, 31));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(32, 100)]
    [InlineData(10, 0)]
    public void Invalid_day_or_amount_is_rejected(int day, int amount)
        => FluentActions.Invoking(() => FinancialRecurrence.Create(Guid.NewGuid(), TransactionType.Payable,
                FinancialCategory.OperatingExpense, "x", amount, day, new DateOnly(2026, 1, 1), null, Guid.NewGuid()))
            .Should().Throw<DomainException>();
}
