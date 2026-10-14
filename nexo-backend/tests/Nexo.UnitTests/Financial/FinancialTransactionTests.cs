using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.UnitTests.Financial;

public class FinancialTransactionTests
{
    private static readonly Guid Tenant  = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid User    = Guid.NewGuid();
    private static readonly Guid Store   = Guid.NewGuid();

    private static FinancialTransaction Revenue(decimal amount = 100m)
        => FinancialTransaction.Create(Tenant, Account, TransactionType.Receivable, FinancialCategory.Revenue,
            amount, "Venda #1", DateTime.UtcNow, User, "Sale", Guid.NewGuid(), Store);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    public void Amount_must_be_positive_after_rounding_to_cents(decimal amount)
        => FluentActions.Invoking(() => Revenue(amount)).Should().Throw<DomainException>();

    [Fact]
    public void Amount_is_kept_in_cents()
        => Revenue(10.005m).Amount.Should().Be(10.01m);

    [Fact]
    public void A_settled_entry_is_not_paid_again_cancelled_or_edited()
    {
        var tx = Revenue();
        tx.MarkPaid();

        FluentActions.Invoking(() => tx.MarkPaid()).Should().Throw<DomainException>();
        FluentActions.Invoking(tx.Cancel).Should().Throw<DomainException>();
        FluentActions.Invoking(() => tx.Update(1m, "x", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_cancelled_entry_cannot_be_paid()
    {
        var tx = Revenue();
        tx.Cancel();
        FluentActions.Invoking(() => tx.MarkPaid()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Reversing_revenue_is_a_settled_revenue_deduction_in_the_same_store()
    {
        var original = Revenue(80m);
        original.MarkPaid();
        var when = DateTime.UtcNow;

        var reversal = FinancialTransaction.CreateReversal(original, Account, "Estorno", when, User, "SaleCancellation", Guid.NewGuid());

        reversal.TransactionType.Should().Be(TransactionType.Payable);
        reversal.Category.Should().Be(FinancialCategory.RevenueDeduction);
        reversal.Amount.Should().Be(80m);
        reversal.StoreId.Should().Be(Store);
        reversal.Status.Should().Be(TransactionStatus.Paid);
        reversal.PaidAt.Should().Be(when);
        reversal.CompetenceDate.Should().Be(when);
        reversal.ReversesTransactionId.Should().Be(original.Id);
    }

    [Fact]
    public void Only_settled_entries_are_reversed()
        => FluentActions.Invoking(() => FinancialTransaction.CreateReversal(
                Revenue(), Account, "Estorno", DateTime.UtcNow, User, "SaleCancellation", Guid.NewGuid()))
            .Should().Throw<DomainException>();

    [Fact]
    public void Manual_categories_follow_the_direction()
    {
        FinancialCategory.Revenue.IsAllowedForManualEntry(TransactionType.Receivable).Should().BeTrue();
        FinancialCategory.Revenue.IsAllowedForManualEntry(TransactionType.Payable).Should().BeFalse();
        FinancialCategory.Tax.IsAllowedForManualEntry(TransactionType.Payable).Should().BeTrue();
        FinancialCategory.RevenueDeduction.IsAllowedForManualEntry(TransactionType.Payable).Should().BeFalse();
    }
}
