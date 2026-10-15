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
    public void A_partial_settlement_splits_the_titulo_keeping_its_facts()
    {
        var tx = Revenue(100m);
        var (remainder, abated) = tx.Settle(40m, 0m, DateTime.UtcNow);

        abated.Should().BeNull();
        tx.Amount.Should().Be(40m);
        tx.Status.Should().Be(TransactionStatus.Paid);
        remainder.Should().NotBeNull();
        remainder!.Amount.Should().Be(60m);
        remainder.Status.Should().Be(TransactionStatus.Pending);
        remainder.ParentTransactionId.Should().Be(tx.Id);
        remainder.CompetenceDate.Should().Be(tx.CompetenceDate);
        remainder.DueDate.Should().Be(tx.DueDate);
        remainder.StoreId.Should().Be(Store);
        remainder.ReferenceId.Should().Be(tx.ReferenceId);

        // settling the remainder in two steps keeps pointing at the original título
        remainder.Settle(10m, 0m, DateTime.UtcNow).Remainder!.ParentTransactionId.Should().Be(tx.Id);
    }

    [Fact]
    public void A_discount_is_an_abated_piece_never_money()
    {
        var tx = Revenue(100m);
        var (remainder, abated) = tx.Settle(80m, 10m, DateTime.UtcNow);

        tx.Amount.Should().Be(80m);                          // money: counts on caixa
        tx.Status.Should().Be(TransactionStatus.Paid);
        abated!.Amount.Should().Be(10m);                     // desconto: competência only
        abated.Status.Should().Be(TransactionStatus.Abated);
        remainder!.Amount.Should().Be(10m);

        var onlyDiscount = Revenue(50m);
        onlyDiscount.Settle(0m, 50m, DateTime.UtcNow).Should().Be(new SettleOutcome(null, null));
        onlyDiscount.Status.Should().Be(TransactionStatus.Abated);
        FluentActions.Invoking(() => onlyDiscount.MarkPaid()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_settlement_cannot_cover_more_than_the_balance_or_nothing()
    {
        FluentActions.Invoking(() => Revenue(100m).Settle(100m, 0.01m, DateTime.UtcNow)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => Revenue(100m).Settle(0m, 0m, DateTime.UtcNow)).Should().Throw<DomainException>();
        Revenue(100m).Settle(100m, 0m, DateTime.UtcNow).Remainder.Should().BeNull();   // full baixa
    }

    [Fact]
    public void Interest_received_is_paid_financial_income_and_a_discount_granted_is_abated_financial_expense()
    {
        var tx = Revenue();
        var interest = tx.CreateSettlementAdjustment(Account, TransactionType.Receivable, 2m, "Juros", DateTime.UtcNow, User, moneyMoved: true);
        interest.Category.Should().Be(FinancialCategory.FinancialIncome);
        interest.Status.Should().Be(TransactionStatus.Paid);
        var discount = tx.CreateSettlementAdjustment(Account, TransactionType.Payable, 5m, "Desconto", DateTime.UtcNow, User, moneyMoved: false);
        discount.Category.Should().Be(FinancialCategory.FinancialExpense);
        discount.Status.Should().Be(TransactionStatus.Abated);
        discount.ParentTransactionId.Should().Be(tx.Id);
        discount.ReferenceType.Should().Be(FinancialTransaction.SettlementReference);
    }

    [Fact]
    public void Renegotiating_an_overdue_titulo_to_a_future_date_reopens_it()
    {
        var tx = Revenue();
        tx.MarkOverdue();
        tx.Update(100m, "Renegociado", DateTime.UtcNow.AddDays(10));
        tx.Status.Should().Be(TransactionStatus.Pending);
    }

    [Fact]
    public void Manual_categories_follow_the_direction()
    {
        FinancialCategory.Revenue.IsAllowedForManualEntry(TransactionType.Receivable).Should().BeTrue();
        FinancialCategory.Revenue.IsAllowedForManualEntry(TransactionType.Payable).Should().BeFalse();
        FinancialCategory.Tax.IsAllowedForManualEntry(TransactionType.Payable).Should().BeTrue();
        FinancialCategory.RevenueDeduction.IsAllowedForManualEntry(TransactionType.Payable).Should().BeFalse();
    }
}
