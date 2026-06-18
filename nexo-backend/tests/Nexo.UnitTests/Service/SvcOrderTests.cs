using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcOrderTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static SvcOrder New() => SvcOrder.Create(T, "OS-20260618-AAA111", Cust, null, null, null, "note");

    [Fact]
    public void Create_defaults_draft_zero_total()
    {
        var o = New();
        o.Code.Should().Be("OS-20260618-AAA111");
        o.CustomerId.Should().Be(Cust);
        o.Status.Should().Be(SvcOrderStatus.Draft);
        o.TotalAmount.Should().Be(0m);
        o.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_with_empty_customer_throws()
        => ((Action)(() => SvcOrder.Create(T, "OS-x", Guid.Empty, null, null, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_blank_code_throws()
        => ((Action)(() => SvcOrder.Create(T, "  ", Cust, null, null, null, null)))
            .Should().Throw<DomainException>();

    [Theory]
    [InlineData(SvcOrderStatus.Open)]
    [InlineData(SvcOrderStatus.Cancelled)]
    public void Draft_allows(SvcOrderStatus to)
    {
        var o = New();
        o.ChangeStatus(to, "r");
        o.Status.Should().Be(to);
    }

    [Fact]
    public void Open_to_inprogress_to_completed_allowed()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Open, null);
        o.ChangeStatus(SvcOrderStatus.InProgress, null);
        o.ChangeStatus(SvcOrderStatus.Completed, null);
        o.Status.Should().Be(SvcOrderStatus.Completed);
        o.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(SvcOrderStatus.InProgress)]   // Draft cannot jump to InProgress
    [InlineData(SvcOrderStatus.Completed)]    // Draft cannot jump to Completed
    public void Invalid_transition_from_draft_throws(SvcOrderStatus to)
        => ((Action)(() => New().ChangeStatus(to, null)))
            .Should().Throw<DomainException>().WithMessage("*status*");

    [Fact]
    public void Cancel_records_reason()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Cancelled, "client gave up");
        o.CancellationReason.Should().Be("client gave up");
    }

    [Fact]
    public void RecalculateTotal_sums_items()
    {
        var o = New();
        var i1 = SvcOrderItem.Create(T, o.Id, Guid.NewGuid(), null, "A", null, 2m, 10m, null);   // 20
        var i2 = SvcOrderItem.Create(T, o.Id, Guid.NewGuid(), null, "B", null, 1m, 5m, 50m);      // 5
        o.RecalculateTotal(new[] { i1, i2 });
        o.TotalAmount.Should().Be(25m);
    }

    [Fact]
    public void UpdateDetails_throws_when_terminal()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Cancelled, "x");
        ((Action)(() => o.UpdateDetails(null, null, "new note")))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void EnsureEditable_throws_when_terminal()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Open, null);
        o.ChangeStatus(SvcOrderStatus.InProgress, null);
        o.ChangeStatus(SvcOrderStatus.Completed, null);
        ((Action)o.EnsureEditable).Should().Throw<DomainException>().WithMessage("*Completed*");
    }
}
