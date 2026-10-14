using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Xunit;

namespace Nexo.UnitTests.Sales;

public class SaleReturnDomainTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User   = Guid.NewGuid();

    [Fact]
    public void Cash_refunds_need_a_drawer_and_other_methods_never_have_one()
    {
        var cashNoDrawer = () => SaleReturn.Create(Tenant, Guid.NewGuid(), PaymentMethod.Cash, 10m, "defeito", User, null, null);
        var pixWithDrawer = () => SaleReturn.Create(Tenant, Guid.NewGuid(), PaymentMethod.Pix, 10m, "defeito", User, null, Guid.NewGuid());

        cashNoDrawer.Should().Throw<DomainException>();
        pixWithDrawer.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(PaymentMethod.Mixed, 10, "x")]
    [InlineData(PaymentMethod.Pix, -1, "x")]
    [InlineData(PaymentMethod.Pix, 10.001, "x")]
    [InlineData(PaymentMethod.Pix, 10, " ")]
    public void Invalid_devolucoes_are_rejected(PaymentMethod method, decimal amount, string reason)
    {
        var act = () => SaleReturn.Create(Tenant, Guid.NewGuid(), method, amount, reason, User, null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Returned_lines_need_a_positive_quantity()
    {
        var r = SaleReturn.Create(Tenant, Guid.NewGuid(), PaymentMethod.Pix, 10m, "defeito", User, null, null);

        var act = () => r.AddItem(Guid.NewGuid(), Guid.NewGuid(), 0m, 10m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_paid_sale_goes_partially_then_fully_returned_and_never_beyond_its_total()
    {
        var sale = PaidSale(total: 90m);

        sale.RegisterReturn(45m, fullyReturned: false);
        sale.Status.Should().Be(SaleStatus.PartiallyReturned);
        sale.ReturnedAmount.Should().Be(45m);

        var tooMuch = () => sale.RegisterReturn(50m, fullyReturned: true);
        tooMuch.Should().Throw<DomainException>();

        sale.RegisterReturn(45m, fullyReturned: true);
        sale.Status.Should().Be(SaleStatus.Returned);

        var again = () => sale.RegisterReturn(1m, fullyReturned: true);
        again.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_paid_sale_cannot_be_cancelled_and_a_cancellation_keeps_its_reason()
    {
        var paid = PaidSale(total: 10m);
        var cancelPaid = () => paid.Cancel(User, "x");
        cancelPaid.Should().Throw<DomainException>();

        var draft = Sale.Create(Tenant, 2, User, null, null, null);
        draft.Cancel(User, "  Cliente desistiu  ");
        draft.Status.Should().Be(SaleStatus.Cancelled);
        draft.CancellationReason.Should().Be("Cliente desistiu");
        draft.CancelledByUserId.Should().Be(User);
    }

    private static Sale PaidSale(decimal total)
    {
        var sale = Sale.Create(Tenant, 1, User, null, null, null);
        var item = SaleItem.Create(Tenant, sale.Id, Guid.NewGuid(), 1, total, 1m, 0);
        sale.RecalculateTotals([item]);
        sale.Confirm();
        sale.MarkPaid();
        return sale;
    }
}
