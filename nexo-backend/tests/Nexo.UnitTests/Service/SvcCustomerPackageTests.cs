using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcCustomerPackageTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc);

    private static SvcCustomerPackage New(DateTime? expires = null)
        => SvcCustomerPackage.Create(T, "PKG-1", Guid.NewGuid(), Guid.NewGuid(), null,
            Start, expires, 500m, null);

    [Fact]
    public void Create_defaults_active()
    {
        var cp = New(Start.AddDays(30));
        cp.Status.Should().Be(SvcCustomerPackageStatus.Active);
        cp.PriceSnapshot.Should().Be(500m);
        cp.ExpiresAt.Should().Be(Start.AddDays(30));
        cp.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_expires_before_start_throws()
        => ((Action)(() => New(Start.AddDays(-1)))).Should().Throw<DomainException>();

    [Fact]
    public void Cancel_sets_status_and_blocks_when_terminal()
    {
        var cp = New();
        cp.Cancel();
        cp.Status.Should().Be(SvcCustomerPackageStatus.Cancelled);
        cp.IsTerminal.Should().BeTrue();
        ((Action)cp.Cancel).Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkConsumed_only_from_active()
    {
        var cp = New();
        cp.MarkConsumed();
        cp.Status.Should().Be(SvcCustomerPackageStatus.Consumed);
        ((Action)cp.MarkConsumed).Should().Throw<DomainException>();
    }

    [Fact]
    public void IsExpiredAt_true_after_expiry()
    {
        var cp = New(Start.AddDays(1));
        cp.IsExpiredAt(Start.AddDays(2)).Should().BeTrue();
        cp.IsExpiredAt(Start).Should().BeFalse();
    }

    [Fact]
    public void Balance_item_consume_reduces_remaining_and_guards()
    {
        var b = SvcCustomerPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "Banho", 4m);
        b.RemainingQuantity.Should().Be(4m);
        b.Consume(1m);
        b.RemainingQuantity.Should().Be(3m);
        ((Action)(() => b.Consume(5m))).Should().Throw<DomainException>();   // insufficient
        ((Action)(() => b.Consume(0m))).Should().Throw<DomainException>();   // non-positive
    }

    [Fact]
    public void Usage_create_guards()
    {
        var u = SvcPackageUsage.Create(T, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, null, null, "n");
        u.Quantity.Should().Be(2m);
        ((Action)(() => SvcPackageUsage.Create(T, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, null, null, null)))
            .Should().Throw<DomainException>();
    }
}
