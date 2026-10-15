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
        DateTime? competenceDate = null)
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
        };
    }

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
            competenceDate: when);
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

    public void Update(decimal amount, string description, DateTime dueDate)
    {
        if (!IsOpen)
            throw new DomainException("Só um lançamento em aberto pode ser alterado.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("O lançamento precisa de uma descrição.");
        Amount      = ValidAmount(amount);
        Description = description.Trim();
        DueDate     = dueDate;
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
