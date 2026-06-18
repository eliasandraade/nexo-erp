using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcPaymentTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Pkg = Guid.NewGuid();
    private static readonly DateTime Paid = new(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateForOrder_sets_fields_paid_and_target()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, "ext", "n");
        p.CustomerId.Should().Be(Cust);
        p.OrderId.Should().Be(Order);
        p.CustomerPackageId.Should().BeNull();
        p.Amount.Should().Be(100m);
        p.Method.Should().Be(SvcPaymentMethod.Pix);
        p.Status.Should().Be(SvcPaymentStatus.Paid);
        p.PaidAt.Should().Be(Paid);
    }

    [Fact]
    public void CreateForCustomerPackage_sets_package_target()
    {
        var p = SvcPayment.CreateForCustomerPackage(T, Cust, Pkg, 50m, SvcPaymentMethod.Cash, Paid, null, null);
        p.CustomerPackageId.Should().Be(Pkg);
        p.OrderId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_with_non_positive_amount_throws(decimal amount)
        => ((Action)(() => SvcPayment.CreateForOrder(T, Cust, Order, amount, SvcPaymentMethod.Cash, Paid, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_empty_customer_throws()
        => ((Action)(() => SvcPayment.CreateForOrder(T, Guid.Empty, Order, 10m, SvcPaymentMethod.Cash, Paid, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Void_marks_voided_and_records_reason_and_time()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, null, null);
        p.Void("wrong entry");
        p.Status.Should().Be(SvcPaymentStatus.Voided);
        p.VoidReason.Should().Be("wrong entry");
        p.VoidedAt.Should().NotBeNull();
    }

    [Fact]
    public void Void_twice_throws()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, null, null);
        p.Void(null);
        ((Action)(() => p.Void(null))).Should().Throw<DomainException>();
    }
}
