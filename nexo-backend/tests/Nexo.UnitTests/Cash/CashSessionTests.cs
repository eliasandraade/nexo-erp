using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Xunit;

namespace Nexo.UnitTests.Cash;

public class CashSessionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User   = Guid.NewGuid();

    private static CashMovement Move(CashSession s, CashMovementType type, decimal amount)
        => CashMovement.Create(Tenant, s.Id, type, amount, type.ToString(), User);

    [Fact]
    public void Expected_balance_is_opening_plus_inflows_minus_outflows()
    {
        var s = CashSession.Open(Tenant, User, 100m);
        var movements = new[]
        {
            Move(s, CashMovementType.SaleReceipt, 20m),
            Move(s, CashMovementType.Deposit, 50m),
            Move(s, CashMovementType.Withdrawal, 30m),
            Move(s, CashMovementType.SaleRefund, 5m),
            Move(s, CashMovementType.Closing, 999m),   // informational, never counted
        };

        CashSession.ExpectedBalanceFrom(s.OpeningBalance, movements).Should().Be(135m);
    }

    [Theory]
    [InlineData(135, 0)]
    [InlineData(130, -5)]     // falta
    [InlineData(140.50, 5.50)] // sobra
    public void Closing_freezes_expected_counted_and_difference(decimal counted, decimal difference)
    {
        var s = CashSession.Open(Tenant, User, 100m);
        var movements = new[] { Move(s, CashMovementType.SaleReceipt, 35m) };

        s.Close(User, counted, movements, "conferido");

        s.Status.Should().Be(CashSessionStatus.Closed);
        s.ExpectedBalance.Should().Be(135m);
        s.ClosingBalance.Should().Be(counted);
        s.Difference.Should().Be(difference);
        s.ClosingNotes.Should().Be("conferido");
    }

    [Fact]
    public void Movements_of_another_session_never_count()
    {
        var s = CashSession.Open(Tenant, User, 10m);
        var other = CashSession.Open(Tenant, User, 0m);

        s.Close(User, 10m, [Move(other, CashMovementType.SaleReceipt, 500m)]);

        s.Difference.Should().Be(0m);
    }

    [Fact]
    public void A_closed_session_cannot_be_closed_again()
    {
        var s = CashSession.Open(Tenant, User, 0m);
        s.Close(User, 0m, []);

        var act = () => s.Close(User, 0m, []);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.001)]
    public void Opening_and_counted_balances_must_be_positive_cents(decimal value)
    {
        var open = () => CashSession.Open(Tenant, User, value);
        open.Should().Throw<DomainException>();

        var s = CashSession.Open(Tenant, User, 0m);
        var close = () => s.Close(User, value, []);
        close.Should().Throw<DomainException>();
    }
}
