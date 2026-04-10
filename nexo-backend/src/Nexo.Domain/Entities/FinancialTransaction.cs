using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>Contas a pagar e a receber.</summary>
public class FinancialTransaction : TenantEntity
{
    private FinancialTransaction() { }
    private FinancialTransaction(Guid tenantId) : base(tenantId) { }

    public Guid FinancialAccountId { get; private set; }
    public TransactionType TransactionType { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTime DueDate { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public TransactionStatus Status { get; private set; }
    public string? ReferenceType { get; private set; }             // "Sale"
    public Guid? ReferenceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    // Navigation
    public FinancialAccount? FinancialAccount { get; private set; }

    public static FinancialTransaction Create(
        Guid tenantId,
        Guid financialAccountId,
        TransactionType transactionType,
        decimal amount,
        string description,
        DateTime dueDate,
        Guid createdByUserId,
        string? referenceType = null,
        Guid? referenceId = null)
    {
        return new FinancialTransaction(tenantId)
        {
            FinancialAccountId = financialAccountId,
            TransactionType    = transactionType,
            Amount             = amount,
            Description        = description.Trim(),
            DueDate            = dueDate,
            Status             = TransactionStatus.Pending,
            CreatedByUserId    = createdByUserId,
            ReferenceType      = referenceType,
            ReferenceId        = referenceId,
        };
    }

    public void MarkPaid(DateTime? paidAt = null)
    {
        Status = TransactionStatus.Paid;
        PaidAt = paidAt ?? DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel()
    {
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
        Amount      = amount;
        Description = description.Trim();
        DueDate     = dueDate;
        SetUpdatedAt();
    }
}
