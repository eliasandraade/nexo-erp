using FluentAssertions;
using Nexo.Application.Features.Sales;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Xunit;

namespace Nexo.UnitTests.Sales;

public class SalePaymentPlanTests
{
    private static PaymentInput Cash(decimal amount) => new("Cash", "Cash", amount);
    private static PaymentInput Pix(decimal amount)  => new("Pix", "Cash", amount);
    private static PaymentInput Card(decimal amount) => new("Debit", "Cash", amount);

    [Fact]
    public void Exact_cash_payment_has_no_change_and_moves_the_drawer()
    {
        var plan = SalePaymentPlan.Build([Cash(42m)], 42m);

        plan.Should().ContainSingle();
        plan[0].Applied.Should().Be(42m);
        plan[0].Change.Should().Be(0m);
        plan[0].MovesCashDrawer.Should().BeTrue();
    }

    [Fact]
    public void Cash_handed_over_above_the_total_becomes_change()
    {
        var plan = SalePaymentPlan.Build([Cash(50m)], 42.30m);

        plan[0].Applied.Should().Be(42.30m);
        plan[0].Change.Should().Be(7.70m);
        plan[0].Tendered.Should().Be(50m);
    }

    [Theory]
    [InlineData("Pix")]
    [InlineData("Debit")]
    [InlineData("Credit")]
    [InlineData("Transfer")]
    public void Non_cash_payments_never_move_the_drawer(string method)
    {
        var plan = SalePaymentPlan.Build([new PaymentInput(method, "Cash", 30m)], 30m);

        plan[0].MovesCashDrawer.Should().BeFalse();
        plan[0].IsSettledNonCash.Should().BeTrue();
    }

    [Fact]
    public void Pix_or_card_above_the_total_is_rejected()
    {
        var pix  = () => SalePaymentPlan.Build([Pix(50m)], 42m);
        var card = () => SalePaymentPlan.Build([Card(50m)], 42m);

        pix.Should().Throw<DomainException>().WithMessage("*troco*");
        card.Should().Throw<DomainException>().WithMessage("*troco*");
    }

    [Fact]
    public void Mixed_cash_and_pix_takes_the_change_from_the_cash_part_only()
    {
        // total 100: 60 by Pix, 50 cash handed over → 40 applied in cash, 10 troco
        var plan = SalePaymentPlan.Build([Pix(60m), Cash(50m)], 100m);

        plan.Single(p => p.MovesCashDrawer).Applied.Should().Be(40m);
        plan.Single(p => p.MovesCashDrawer).Change.Should().Be(10m);
        plan.Single(p => !p.MovesCashDrawer).Applied.Should().Be(60m);
        plan.Sum(p => p.Applied).Should().Be(100m);
    }

    [Fact]
    public void Change_that_would_swallow_the_whole_cash_part_is_rejected()
    {
        // total 50 by Pix, plus 10 cash handed over and handed back — a typing error
        var act = () => SalePaymentPlan.Build([Pix(50m), Cash(10m)], 50m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Insufficient_payment_is_rejected()
    {
        var act = () => SalePaymentPlan.Build([Cash(40m)], 42m);

        act.Should().Throw<DomainException>().WithMessage("*insuficiente*");
    }

    [Theory]
    [InlineData("Bitcoin", "Cash", 10)]
    [InlineData("Mixed", "Cash", 10)]
    [InlineData("Cash", "Later", 10)]
    [InlineData("Cash", "Cash", 0)]
    [InlineData("Cash", "Cash", -5)]
    public void Invalid_lines_are_rejected_as_domain_errors(string method, string type, decimal amount)
    {
        var act = () => SalePaymentPlan.Build([new PaymentInput(method, type, amount)], 10m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Amounts_with_more_than_two_decimals_are_rejected()
    {
        var act = () => SalePaymentPlan.Build([Cash(10.005m)], 10m);

        act.Should().Throw<DomainException>().WithMessage("*duas casas*");
    }

    [Fact]
    public void A_prazo_needs_a_due_date_and_is_not_settled()
    {
        var missing = () => SalePaymentPlan.Build([new PaymentInput("Credit", "Credit", 10m)], 10m);
        missing.Should().Throw<DomainException>().WithMessage("*vencimento*");

        var plan = SalePaymentPlan.Build(
            [new PaymentInput("Credit", "Credit", 10m, DateTime.UtcNow.AddDays(30))], 10m);
        plan[0].MovesCashDrawer.Should().BeFalse();
        plan[0].IsSettledNonCash.Should().BeFalse();
        plan[0].Type.Should().Be(PaymentType.Credit);
    }

    [Fact]
    public void Empty_payment_list_is_rejected()
    {
        var act = () => SalePaymentPlan.Build([], 10m);

        act.Should().Throw<DomainException>();
    }
}
