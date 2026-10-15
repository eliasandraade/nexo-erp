using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcFinanceServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);
    private static readonly DateTime Noon = BusinessClock.NoonUtc(Day);
    private static readonly Guid Ana = Guid.NewGuid(), Bia = Guid.NewGuid(), Order = Guid.NewGuid(), Pkg = Guid.NewGuid();

    private static SvcFinanceRaw Raw(
        IReadOnlyList<SvcFinancePaymentRow>? payments = null, IReadOnlyList<SvcFinanceOrderItemRow>? items = null,
        IReadOnlyList<SvcFinanceCommissionRow>? commissions = null, IReadOnlyList<SvcFinanceUsageRow>? usages = null)
        => new(payments ?? [], items ?? [], new Dictionary<Guid, Guid?> { [Order] = Ana }, usages ?? [], commissions ?? [],
            new Dictionary<SvcAppointmentStatus, int> { [SvcAppointmentStatus.Completed] = 1, [SvcAppointmentStatus.NoShow] = 1, [SvcAppointmentStatus.Scheduled] = 1 }, 1, 300m,
            new Dictionary<Guid, string> { [Ana] = "Ana", [Bia] = "Bia" });

    private static SvcFinancePaymentRow Paid(decimal amount, Guid? order = null, Guid? pkg = null, SvcPaymentMethod m = SvcPaymentMethod.Pix)
        => new(Guid.NewGuid(), order, pkg, amount, m, SvcPaymentStatus.Paid, Noon, null);

    [Fact]
    public void An_order_payment_is_split_by_the_items_professionals_to_the_cent()
    {
        var s = SvcFinanceService.Build(Day, Day, Raw(
            payments: [Paid(100m, order: Order)],
            items: [new(Order, Ana, 50m), new(Order, Bia, 25m), new(Order, Bia, 25m)]));

        s.Received.Should().Be(100m);
        s.ByProfessional.Single(p => p.ProfessionalId == Ana).ReceivedFromOrders.Should().Be(50m);
        s.ByProfessional.Single(p => p.ProfessionalId == Bia).ReceivedFromOrders.Should().Be(50m);

        var odd = SvcFinanceService.Build(Day, Day, Raw(
            payments: [Paid(10m, order: Order)], items: [new(Order, Ana, 1m), new(Order, Bia, 1m), new(Order, Bia, 1m)]));
        odd.ByProfessional.Sum(p => p.ReceivedFromOrders).Should().Be(10m, "rounding never loses or invents a cent");
    }

    [Fact]
    public void Money_without_a_professional_lands_in_its_own_row()
    {
        var bare = Guid.NewGuid();
        var s = SvcFinanceService.Build(Day, Day, Raw(
            payments: [Paid(30m, order: bare), Paid(20m, order: Order)],
            items: [new(Order, null, 20m)],
            usages: [new(null, 15m)]));

        var none = s.ByProfessional.Single(p => p.ProfessionalId == null);
        none.Name.Should().Be("Sem profissional");
        none.ReceivedFromOrders.Should().Be(50m, "an order with no professional anywhere, and an item without one");
        none.PackageServices.Should().Be(15m);
    }

    [Fact]
    public void An_item_the_package_paid_for_gets_none_of_the_comanda_money()
    {
        var s = SvcFinanceService.Build(Day, Day, Raw(
            payments: [Paid(40m, order: Order)],
            items: [new(Order, Ana, 60m, CoveredByPackage: true), new(Order, Bia, 40m)]));

        s.ByProfessional.Should().ContainSingle(p => p.ProfessionalId == Bia).Which.ReceivedFromOrders.Should().Be(40m);
        s.ByProfessional.Should().NotContain(p => p.ProfessionalId == Ana);
    }

    [Fact]
    public void Allocation_always_adds_up_and_never_goes_negative()
    {
        SvcFinanceService.Allocate(10m, [1m, 1m, 1m]).Should().Equal(3.34m, 3.33m, 3.33m);
        var tiny = SvcFinanceService.Allocate(0.05m, [100m, 100m, 0.01m]);
        tiny.Sum().Should().Be(0.05m);
        tiny.Should().OnlyContain(x => x >= 0m);
    }

    [Fact]
    public void Totals_by_source_method_and_ticket()
    {
        var s = SvcFinanceService.Build(Day, Day, Raw(payments:
        [
            Paid(60m, order: Order), Paid(40m, order: Order, m: SvcPaymentMethod.Cash), Paid(300m, pkg: Pkg),
            new(Guid.NewGuid(), Order, null, 20m, SvcPaymentMethod.Pix, SvcPaymentStatus.Voided, Noon, Noon),
        ]));

        s.Received.Should().Be(400m);
        s.ReceivedFromOrders.Should().Be(100m);
        s.ReceivedFromPackages.Should().Be(300m);
        s.AverageTicket.Should().Be(200m, "two things paid: one comanda, one package");
        s.Voided.Should().Be(20m);
        s.ByMethod.Single(m => m.Method == SvcPaymentMethod.Pix).Amount.Should().Be(360m);
        s.ByDay.Should().ContainSingle().Which.Day.Should().Be(Day);
        s.Appointments.Should().Be(new SvcFinanceAppointmentsDto(3, 1, 0, 1, 1));
    }

    [Fact]
    public void A_reversed_earning_counts_only_when_it_had_been_paid_out()
    {
        var payout = Guid.NewGuid();
        var s = SvcFinanceService.Build(Day, Day, Raw(commissions:
        [
            new(Ana, SvcCommissionEntryKind.Earning, 10m, null, null),          // open: counts
            new(Ana, SvcCommissionEntryKind.Earning, 7m, Noon, null),           // reversed before payout: never counted
            new(Bia, SvcCommissionEntryKind.Earning, 5m, Noon, payout),         // paid, then reversed: counts…
            new(Bia, SvcCommissionEntryKind.Reversal, -5m, null, null),         // …and its negative entry discounts it
        ], usages: [new(Ana, 40m)]));

        s.ByProfessional.Single(p => p.ProfessionalId == Ana).Commission.Should().Be(10m);
        s.ByProfessional.Single(p => p.ProfessionalId == Ana).PackageServices.Should().Be(40m);
        s.ByProfessional.Should().NotContain(p => p.ProfessionalId == Bia, "nothing left for Bia in the period");
        s.CommissionTotal.Should().Be(10m);
    }
}
