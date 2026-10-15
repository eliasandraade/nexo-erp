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

    [Fact]
    public void Reversal_restores_units_up_to_the_total_and_reopens_a_used_up_package()
    {
        var cp = New();
        var b = SvcCustomerPackageItem.Create(T, cp.Id, Guid.NewGuid(), "Banho", 2m);
        b.Consume(2m);
        cp.MarkConsumed();

        b.Restore(1m);
        cp.ReopenAfterReversal();
        b.RemainingQuantity.Should().Be(1m);
        cp.Status.Should().Be(SvcCustomerPackageStatus.Active);
        ((Action)(() => b.Restore(2m))).Should().Throw<DomainException>();   // would exceed the total
    }

    [Fact]
    public void Usage_is_reversed_once_and_needs_a_reason()
    {
        var u = SvcPackageUsage.Create(T, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, null, null, null);
        ((Action)(() => u.Reverse(" ", Guid.NewGuid(), DateTime.UtcNow))).Should().Throw<DomainException>();
        u.Reverse("Serviço errado", Guid.NewGuid(), DateTime.UtcNow);
        u.IsReversed.Should().BeTrue();
        ((Action)(() => u.Reverse("de novo", Guid.NewGuid(), DateTime.UtcNow))).Should().Throw<ConflictException>();
    }
}
