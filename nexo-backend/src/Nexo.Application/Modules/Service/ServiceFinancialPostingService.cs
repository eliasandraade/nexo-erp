using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Bridges Service money events into the tenant's financeiro (<see cref="FinancialTransaction"/> —
/// contas a pagar e a receber). Follows the pattern a credit sale already uses in SaleService:
/// resolve the tenant's default account for the type, create a transaction carrying
/// ReferenceType/ReferenceId back to the originating record, and settle it.
///
/// Two rules this class enforces:
///   - a reversal is a COUNTER-ENTRY, never a delete — the original stays visible, matching how
///     SaleCancellation reverses a sale;
///   - Service money is recorded as already settled, because SvcPayment only ever records money
///     that was actually received (and a commission payout only money actually handed over).
///
/// It does NOT call SaveChanges: the repositories share the scoped NexoDbContext, so the caller's
/// unit of work commits the payment and its transaction together.
/// </summary>
public class ServiceFinancialPostingService
{
    public const string PaymentReference       = "SvcPayment";
    public const string PaymentVoidReference   = "SvcPaymentVoid";

    private readonly IFinancialRepository _financial;
    private readonly ICurrentUser        _currentUser;
    private readonly ICurrentTenant      _currentTenant;

    public ServiceFinancialPostingService(
        IFinancialRepository financial, ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        _financial     = financial;
        _currentUser   = currentUser;
        _currentTenant = currentTenant;
    }

    /// <summary>Records money received for a Service order or customer package as settled revenue.</summary>
    public Task PostPaymentAsync(SvcPayment payment, string description, CancellationToken ct = default)
        => PostSettledAsync(
            FinancialAccountType.Receivable, TransactionType.Receivable,
            payment.Amount, payment.PaidAt, description, PaymentReference, payment.Id, ct);

    /// <summary>
    /// Counter-entry for a voided payment: a settled payable of the same amount, so the net effect
    /// on the period is zero while both sides remain auditable.
    /// </summary>
    public Task ReversePaymentAsync(SvcPayment payment, string description, CancellationToken ct = default)
        => PostSettledAsync(
            FinancialAccountType.Payable, TransactionType.Payable,
            payment.Amount, payment.VoidedAt ?? DateTime.UtcNow, description,
            PaymentVoidReference, payment.Id, ct);

    /// <summary>Records an expense already paid (used by commission payouts).</summary>
    public Task PostSettledExpenseAsync(
        decimal amount, DateTime paidAt, string description,
        string referenceType, Guid referenceId, CancellationToken ct = default)
        => PostSettledAsync(
            FinancialAccountType.Payable, TransactionType.Payable,
            amount, paidAt, description, referenceType, referenceId, ct);

    private async Task PostSettledAsync(
        FinancialAccountType accountType, TransactionType transactionType,
        decimal amount, DateTime when, string description,
        string referenceType, Guid referenceId, CancellationToken ct)
    {
        var account = await _financial.GetDefaultByTypeAsync(accountType, ct)
            ?? throw new DomainException(
                $"Tenant has no active '{Describe(accountType)}' account. " +
                "It is created automatically for new tenants — an older tenant may need a backfill.");

        var transaction = FinancialTransaction.Create(
            tenantId:           _currentTenant.Id,
            financialAccountId: account.Id,
            transactionType:    transactionType,
            amount:             amount,
            description:        description,
            dueDate:            when,
            createdByUserId:    _currentUser.UserId,
            referenceType:      referenceType,
            referenceId:        referenceId);

        transaction.MarkPaid(when);
        await _financial.AddTransactionAsync(transaction, ct);
    }

    private static string Describe(FinancialAccountType type) => type switch
    {
        FinancialAccountType.Receivable => "Contas a Receber",
        FinancialAccountType.Payable    => "Contas a Pagar",
        _                               => type.ToString(),
    };
}
