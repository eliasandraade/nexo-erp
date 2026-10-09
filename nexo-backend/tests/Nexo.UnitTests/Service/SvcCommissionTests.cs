using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcCommissionTests
{
    private static readonly Guid T    = Guid.NewGuid();
    private static readonly Guid Prof = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);

    // ── Policy: which rate applies ───────────────────────────────────────────
    [Fact]
    public void Catalog_rate_wins_over_the_professional_default()
        => SvcCommissionPolicy.ResolvePercent(30m, 40m).Should().Be(30m);

    [Fact]
    public void Professional_default_applies_when_the_catalog_has_no_rate()
        => SvcCommissionPolicy.ResolvePercent(null, 40m).Should().Be(40m);

    [Fact]
    public void No_rate_anywhere_resolves_to_null_and_earns_nothing()
    {
        var pct = SvcCommissionPolicy.ResolvePercent(null, null);
        pct.Should().BeNull();
        SvcCommissionPolicy.Earns(pct).Should().BeFalse();
    }

    [Fact]
    public void An_explicit_zero_on_the_catalog_is_not_overridden_by_the_professional_default()
    {
        var pct = SvcCommissionPolicy.ResolvePercent(0m, 40m);
        pct.Should().Be(0m);
        SvcCommissionPolicy.Earns(pct).Should().BeFalse();
    }

    [Theory]
    [InlineData(300, 10, 1, 30)]        // 10 units for 300 → 30 per unit
    [InlineData(100, 3, 1, 33.33)]      // rounded to cents
    [InlineData(100, 3, 2, 66.67)]      // AwayFromZero
    [InlineData(0, 3, 1, 0)]            // free package → no base
    [InlineData(100, 0, 1, 0)]          // degenerate package
    public void Package_value_is_prorated_evenly_over_all_units(
        decimal price, decimal totalUnits, decimal quantity, decimal expected)
        => SvcCommissionPolicy.ProratePackageValue(price, totalUnits, quantity).Should().Be(expected);

    // ── Entry ────────────────────────────────────────────────────────────────
    [Fact]
    public void Entry_snapshots_base_and_rate_and_rounds_the_amount_to_cents()
    {
        var e = SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.OrderItem, Guid.NewGuid(),
            baseAmount: 33.33m, commissionPercent: 33.33m, At, "OS · Corte");

        e.BaseAmount.Should().Be(33.33m);
        e.CommissionPercent.Should().Be(33.33m);
        e.CommissionAmount.Should().Be(11.11m);            // 11.108889 → 11.11
        e.RecognizedAt.Should().Be(At);
        e.PayoutId.Should().BeNull();
        e.IsSettled.Should().BeFalse();
        e.Notes.Should().Be("OS · Corte");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    public void Entry_rejects_a_rate_outside_the_valid_range(decimal pct)
        => ((Action)(() => SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.Appointment,
                Guid.NewGuid(), 100m, pct, At)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Entry_rejects_a_negative_base()
        => ((Action)(() => SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.Appointment,
                Guid.NewGuid(), -1m, 10m, At)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Entry_requires_professional_customer_and_source()
    {
        ((Action)(() => SvcCommissionEntry.Create(T, Guid.Empty, Cust, SvcCommissionSource.Appointment, Guid.NewGuid(), 1m, 10m, At)))
            .Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionEntry.Create(T, Prof, Guid.Empty, SvcCommissionSource.Appointment, Guid.NewGuid(), 1m, 10m, At)))
            .Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.Appointment, Guid.Empty, 1m, 10m, At)))
            .Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionEntry.Create(T, Prof, Cust, (SvcCommissionSource)99, Guid.NewGuid(), 1m, 10m, At)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reversal_is_the_exact_negative_of_the_earning_and_points_at_it()
    {
        var earning = SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.OrderItem, Guid.NewGuid(),
            33.33m, 33.33m, At, "OS · Corte");
        var reversal = SvcCommissionEntry.CreateReversalOf(earning, At.AddDays(1), "Estorno · OS · Corte");

        earning.Kind.Should().Be(SvcCommissionEntryKind.Earning);
        earning.IsActiveEarning.Should().BeTrue();
        reversal.Kind.Should().Be(SvcCommissionEntryKind.Reversal);
        reversal.IsActiveEarning.Should().BeFalse();
        reversal.ReversalOfEntryId.Should().Be(earning.Id);
        reversal.SourceId.Should().Be(earning.SourceId);
        reversal.ProfessionalId.Should().Be(earning.ProfessionalId);
        reversal.CommissionAmount.Should().Be(-earning.CommissionAmount);
        reversal.BaseAmount.Should().Be(-earning.BaseAmount);
        reversal.RecognizedAt.Should().Be(At.AddDays(1));
    }

    [Fact]
    public void A_reversal_cannot_itself_be_reversed()
    {
        var earning = SvcCommissionEntry.Create(T, Prof, Cust, SvcCommissionSource.OrderItem, Guid.NewGuid(), 10m, 10m, At);
        var reversal = SvcCommissionEntry.CreateReversalOf(earning, At, null);
        ((Action)(() => SvcCommissionEntry.CreateReversalOf(reversal, At, null))).Should().Throw<DomainException>();
    }

    // ── Payout ───────────────────────────────────────────────────────────────
    [Fact]
    public void Payout_is_born_pending_with_its_frozen_total()
    {
        var p = SvcCommissionPayout.Create(T, Prof, At.AddDays(-30), At, 150.50m, 3, " fechamento ");
        p.Status.Should().Be(SvcCommissionPayoutStatus.Pending);
        p.TotalAmount.Should().Be(150.50m);
        p.EntryCount.Should().Be(3);
        p.PaidAt.Should().BeNull();
        p.Notes.Should().Be("fechamento");
    }

    [Fact]
    public void Payout_rejects_empty_or_inverted_periods_and_non_positive_totals()
    {
        ((Action)(() => SvcCommissionPayout.Create(T, Prof, At, At.AddDays(-1), 10m, 1))).Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionPayout.Create(T, Prof, At.AddDays(-1), At, 10m, 0))).Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionPayout.Create(T, Prof, At.AddDays(-1), At, 0m, 1))).Should().Throw<DomainException>();
        ((Action)(() => SvcCommissionPayout.Create(T, Guid.Empty, At.AddDays(-1), At, 10m, 1))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Payout_can_be_paid_once()
    {
        var p = SvcCommissionPayout.Create(T, Prof, At.AddDays(-30), At, 10m, 1);
        p.MarkPaid(At);
        p.Status.Should().Be(SvcCommissionPayoutStatus.Paid);
        p.PaidAt.Should().Be(At);

        ((Action)(() => p.MarkPaid(At.AddHours(1)))).Should().Throw<DomainException>();
        p.PaidAt.Should().Be(At);
    }
}
