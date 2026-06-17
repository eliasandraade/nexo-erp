using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcAppointmentTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static readonly Guid Prof = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End   = new(2026, 6, 18, 11, 0, 0, DateTimeKind.Utc);

    private static SvcAppointment New(Guid? subjectId = null)
        => SvcAppointment.Create(T, Cust, Prof, Item, subjectId, Start, End, 50m, "note");

    [Fact]
    public void Create_sets_fields_and_defaults_scheduled()
    {
        var a = New();
        a.CustomerId.Should().Be(Cust);
        a.ProfessionalId.Should().Be(Prof);
        a.CatalogItemId.Should().Be(Item);
        a.SubjectId.Should().BeNull();
        a.StartsAt.Should().Be(Start);
        a.EndsAt.Should().Be(End);
        a.PriceSnapshot.Should().Be(50m);
        a.Status.Should().Be(SvcAppointmentStatus.Scheduled);
        a.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_with_starts_after_ends_throws()
    {
        var act = () => SvcAppointment.Create(T, Cust, Prof, Item, null, End, Start, 50m);
        act.Should().Throw<DomainException>().WithMessage("*StartsAt*EndsAt*");
    }

    [Theory]
    [InlineData(true, false, false)]   // empty customer
    [InlineData(false, true, false)]   // empty professional
    [InlineData(false, false, true)]   // empty catalog item
    public void Create_with_empty_required_ids_throws(bool noCust, bool noProf, bool noItem)
    {
        var act = () => SvcAppointment.Create(
            T, noCust ? Guid.Empty : Cust, noProf ? Guid.Empty : Prof,
            noItem ? Guid.Empty : Item, null, Start, End, 50m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_with_negative_price_throws()
        => ((Action)(() => SvcAppointment.Create(T, Cust, Prof, Item, null, Start, End, -1m)))
            .Should().Throw<DomainException>();

    [Theory]
    [InlineData(SvcAppointmentStatus.Confirmed)]
    [InlineData(SvcAppointmentStatus.Cancelled)]
    [InlineData(SvcAppointmentStatus.NoShow)]
    public void Scheduled_allows(SvcAppointmentStatus to)
    {
        var a = New();
        a.ChangeStatus(to, "r");
        a.Status.Should().Be(to);
    }

    [Fact]
    public void Confirmed_to_in_progress_to_completed_is_allowed()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Confirmed, null);
        a.ChangeStatus(SvcAppointmentStatus.InProgress, null);
        a.ChangeStatus(SvcAppointmentStatus.Completed, null);
        a.Status.Should().Be(SvcAppointmentStatus.Completed);
        a.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Cancel_records_reason()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Cancelled, "client asked");
        a.CancellationReason.Should().Be("client asked");
    }

    [Theory]
    [InlineData(SvcAppointmentStatus.InProgress)]  // Scheduled cannot jump to InProgress
    [InlineData(SvcAppointmentStatus.Completed)]   // Scheduled cannot jump to Completed
    public void Invalid_transition_from_scheduled_throws(SvcAppointmentStatus to)
        => ((Action)(() => New().ChangeStatus(to, null)))
            .Should().Throw<DomainException>().WithMessage("*status*");

    [Fact]
    public void Transition_from_terminal_throws()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Cancelled, "x");
        ((Action)(() => a.ChangeStatus(SvcAppointmentStatus.Confirmed, null)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_updates_fields_when_not_terminal()
    {
        var a = New();
        var ns = new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc);
        var ne = new DateTime(2026, 6, 19, 9, 30, 0, DateTimeKind.Utc);
        a.Reschedule(Cust, Prof, Item, null, ns, ne, 80m, "moved");
        a.StartsAt.Should().Be(ns);
        a.EndsAt.Should().Be(ne);
        a.PriceSnapshot.Should().Be(80m);
    }

    [Fact]
    public void Reschedule_terminal_throws()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Confirmed, null);
        a.ChangeStatus(SvcAppointmentStatus.InProgress, null);
        a.ChangeStatus(SvcAppointmentStatus.Completed, null);
        var act = () => a.Reschedule(Cust, Prof, Item, null, Start, End, 50m, null);
        act.Should().Throw<DomainException>().WithMessage("*Completed*");
    }
}
