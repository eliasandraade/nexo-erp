using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Lançamento financeiro — o livro único do Orken (contas a pagar e a receber, quitadas ou não).
/// Toda movimentação de dinheiro dos módulos chega aqui: vendas (qualquer forma de pagamento),
/// devoluções e cancelamentos, compras, Service. O caixa físico (<see cref="CashMovement"/>) continua
/// sendo o controle da gaveta, não o livro.
///
/// Os dois regimes leem o mesmo lançamento:
///   competência → <see cref="CompetenceDate"/> (quando o fato aconteceu), exceto os cancelados;
///   caixa       → <see cref="PaidAt"/> (quando o dinheiro entrou ou saiu), só os quitados.
///
/// Estados: Pending/Overdue → Paid | Cancelled. Quitado não se cancela nem se edita: corrige-se com
/// um estorno (<see cref="CreateReversal"/>), e o original continua visível.
/// </summary>
/// <summary>Result of a baixa: the open remainder and the abated (desconto) piece, when any.</summary>
public record SettleOutcome(FinancialTransaction? Remainder, FinancialTransaction? Abated);

public class FinancialTransaction : TenantEntity
{
    private FinancialTransaction() { }
    private FinancialTransaction(Guid tenantId) : base(tenantId) { }

    public Guid FinancialAccountId { get; private set; }
    public TransactionType TransactionType { get; private set; }
    public FinancialCategory Category { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTime CompetenceDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public TransactionStatus Status { get; private set; }
    /// <summary>Loja do fato (venda, compra, atendimento); null para lançamentos da empresa toda.</summary>
    public Guid? StoreId { get; private set; }
    public string? ReferenceType { get; private set; }             // "Sale", "Purchase", "SvcPayment"…
    public Guid? ReferenceId { get; private set; }
    /// <summary>Set on a counter-entry: the lançamento it reverses.</summary>
    public Guid? ReversesTransactionId { get; private set; }
    /// <summary>Set on the remainder of a partial settlement (or on its juros/desconto): the título it came from.</summary>
    public Guid? ParentTransactionId { get; private set; }
    /// <summary>Cliente do título (a receber), quando houver.</summary>
    public Guid? CustomerId { get; private set; }
    /// <summary>Fornecedor do título (a pagar), quando houver.</summary>
    public Guid? SupplierId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    // Navigation
    public FinancialAccount? FinancialAccount { get; private set; }

    public bool IsOpen => Status is TransactionStatus.Pending or TransactionStatus.Overdue;

    public static FinancialTransaction Create(
        Guid tenantId,
        Guid financialAccountId,
        TransactionType transactionType,
        FinancialCategory category,
        decimal amount,
        string description,
        DateTime dueDate,
        Guid createdByUserId,
        string? referenceType = null,
        Guid? referenceId = null,
        Guid? storeId = null,
        DateTime? competenceDate = null,
        Guid? customerId = null,
        Guid? supplierId = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("O lançamento precisa de uma descrição.");

        return new FinancialTransaction(tenantId)
        {
            FinancialAccountId = financialAccountId,
            TransactionType    = transactionType,
            Category           = category,
            Amount             = ValidAmount(amount),
            Description        = description.Trim(),
            CompetenceDate     = competenceDate ?? DateTime.UtcNow,
            DueDate            = dueDate,
            Status             = TransactionStatus.Pending,
            CreatedByUserId    = createdByUserId,
            ReferenceType      = referenceType,
            ReferenceId        = referenceId,
            StoreId            = storeId is { } s && s != Guid.Empty ? s : null,
            CustomerId         = customerId is { } c && c != Guid.Empty ? c : null,
            SupplierId         = supplierId is { } sp && sp != Guid.Empty ? sp : null,
        };
    }

    /// <summary>
    /// Baixa de um título em aberto: <paramref name="money"/> really moved, <paramref name="discount"/>
    /// extinguishes part of it without money. A baixa below the balance splits the título — the open
    /// remainder keeps due date, competência, category, store and customer. The money part is Paid
    /// (counts on caixa); the desconto part is Abated (competência only), so receitas recebidas never
    /// include a discount and undoing the origin reverses exactly the money.
    /// </summary>
    public SettleOutcome Settle(decimal money, decimal discount, DateTime paidAt)
    {
        if (!IsOpen)
            throw new DomainException(Status == TransactionStatus.Paid
                ? "Este lançamento já está quitado."
                : "Este lançamento não está em aberto.");
        money    = Math.Round(money, 2, MidpointRounding.AwayFromZero);
        discount = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        if (money < 0 || discount < 0)
            throw new DomainException("Valores da baixa não podem ser negativos.");
        var covered = money + discount;
        if (covered <= 0)
            throw new DomainException("A baixa precisa cobrir algum valor.");
        if (covered > Amount)
            throw new DomainException($"A baixa ({covered:F2}) passa do saldo do título ({Amount:F2}).");

        var remainder = covered < Amount ? Child(Amount - covered) : null;
        FinancialTransaction? abated = null;
        if (money > 0)
        {
            if (discount > 0)
            {
                abated = Child(discount);
                abated.Abate(paidAt);
            }
            Amount = money;
            MarkPaid(paidAt);
        }
        else
        {
            Amount = discount;
            Abate(paidAt);
        }
        return new SettleOutcome(remainder, abated);
    }

    /// <summary>A piece of this título (same facts), keeping the link to the original.</summary>
    private FinancialTransaction Child(decimal amount) => new(TenantId)
    {
        FinancialAccountId  = FinancialAccountId,
        TransactionType     = TransactionType,
        Category            = Category,
        Amount              = amount,
        Description         = Description,
        CompetenceDate      = CompetenceDate,
        DueDate             = DueDate,
        Status              = Status,             // an overdue remainder stays overdue
        CreatedByUserId     = CreatedByUserId,
        ReferenceType       = ReferenceType,
        ReferenceId         = ReferenceId,
        StoreId             = StoreId,
        CustomerId          = CustomerId,
        SupplierId          = SupplierId,
        ParentTransactionId = ParentTransactionId ?? Id,
    };

    private void Abate(DateTime when)
    {
        Status = TransactionStatus.Abated;
        PaidAt = when;
        SetUpdatedAt();
    }

    /// <summary>
    /// Juros/multa or desconto of a baixa, as its own lançamento: received interest is financial income,
    /// a discount granted is a financial expense (mirrored for a payable). Juros moved money (Paid); a
    /// desconto did not (Abated — competência only, matching the abated part of the título).
    /// </summary>
    public FinancialTransaction CreateSettlementAdjustment(
        Guid financialAccountId, TransactionType direction, decimal amount, string description,
        DateTime paidAt, Guid createdByUserId, bool moneyMoved)
    {
        var adjustment = Create(
            TenantId, financialAccountId, direction,
            direction == TransactionType.Receivable ? FinancialCategory.FinancialIncome : FinancialCategory.FinancialExpense,
            amount, description, paidAt, createdByUserId,
            referenceType: SettlementReference, referenceId: Id,
            storeId: StoreId, competenceDate: paidAt, customerId: CustomerId, supplierId: SupplierId);
        adjustment.ParentTransactionId = ParentTransactionId ?? Id;
        if (moneyMoved) adjustment.MarkPaid(paidAt);
        else adjustment.Abate(paidAt);
        return adjustment;
    }

    /// <summary>ReferenceType of the juros/multa/desconto lançamentos of a baixa.</summary>
    public const string SettlementReference = "Settlement";

    /// <summary>
    /// Counter-entry for a settled lançamento: opposite direction, same amount and store, already
    /// settled at <paramref name="when"/>. Reversing revenue is a revenue deduction (not an expense),
    /// so a DRE never shows an estorno as despesa.
    /// </summary>
    public static FinancialTransaction CreateReversal(
        FinancialTransaction original,
        Guid financialAccountId,
        string description,
        DateTime when,
        Guid createdByUserId,
        string referenceType,
        Guid referenceId)
    {
        if (original.Status != TransactionStatus.Paid)
            throw new DomainException("Só um lançamento quitado é estornado; um em aberto é cancelado.");

        var reversal = Create(
            original.TenantId,
            financialAccountId,
            original.TransactionType == TransactionType.Receivable ? TransactionType.Payable : TransactionType.Receivable,
            original.Category == FinancialCategory.Revenue ? FinancialCategory.RevenueDeduction : original.Category,
            original.Amount,
            description,
            when,
            createdByUserId,
            referenceType,
            referenceId,
            original.StoreId,
            competenceDate: when,
            customerId: original.CustomerId,
            supplierId: original.SupplierId);
        reversal.ReversesTransactionId = original.Id;
        reversal.MarkPaid(when);
        return reversal;
    }

    public void MarkPaid(DateTime? paidAt = null)
    {
        if (!IsOpen)
            throw new DomainException(Status == TransactionStatus.Paid
                ? "Este lançamento já está quitado."
                : "Um lançamento cancelado não pode ser quitado.");
        Status = TransactionStatus.Paid;
        PaidAt = paidAt ?? DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (!IsOpen)
            throw new DomainException(Status == TransactionStatus.Paid
                ? "Um lançamento quitado não se cancela: lance um estorno."
                : "Este lançamento já está cancelado.");
        Status = TransactionStatus.Cancelled;
        SetUpdatedAt();
    }

    public void MarkOverdue()
    {
        if (Status == TransactionStatus.Pending)
        {
            Status = TransactionStatus.Overdue;
            SetUpdatedAt();
        }
    }

    public void Update(decimal amount, string description, DateTime dueDate, DateTime? todayStartUtc = null)
    {
        if (!IsOpen)
            throw new DomainException("Só um lançamento em aberto pode ser alterado.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("O lançamento precisa de uma descrição.");
        Amount      = ValidAmount(amount);
        Description = description.Trim();
        DueDate     = dueDate;
        // Renegotiated to a date still ahead: no longer overdue (the job re-flags it if it lapses).
        if (Status == TransactionStatus.Overdue && dueDate >= (todayStartUtc ?? DateTime.UtcNow.Date))
            Status = TransactionStatus.Pending;
        SetUpdatedAt();
    }

    private static decimal ValidAmount(decimal amount)
    {
        var cents = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (cents <= 0)
            throw new DomainException("O valor do lançamento precisa ser maior que zero.");
        return cents;
    }
}
