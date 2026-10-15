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
    public const string CommissionPayoutReference = "SvcCommissionPayout";

    /// <summary>
    /// ReferenceTypes owned by the Service module. Their lançamentos mirror Service records
    /// (payment, void, payout) and are corrected only at the origin — the financial module must
    /// not create, edit, pay or cancel them, or the two sides silently disagree.
    /// </summary>
    public static bool IsServiceManaged(string? referenceType)
        => referenceType is not null && referenceType.StartsWith("Svc", StringComparison.OrdinalIgnoreCase);

    private readonly IFinancialRepository _financial;
    private readonly ICurrentUser        _currentUser;
    private readonly ICurrentTenant      _currentTenant;
    private readonly ICurrentStore       _currentStore;

    public ServiceFinancialPostingService(
        IFinancialRepository financial, ICurrentUser currentUser, ICurrentTenant currentTenant,
        ICurrentStore currentStore)
    {
        _financial     = financial;
        _currentUser   = currentUser;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
    }

    /// <summary>Records money received for a Service order or customer package as settled revenue.</summary>
    public Task PostPaymentAsync(SvcPayment payment, string description, CancellationToken ct = default)
        => PostSettledAsync(
            FinancialAccountType.Receivable, TransactionType.Receivable, FinancialCategory.Revenue,
            payment.Amount, payment.PaidAt, description, PaymentReference, payment.Id, StoreOf(payment.StoreId), ct,
            payment.CustomerId);

    /// <summary>
    /// Counter-entry for a voided payment: a settled outflow of the same amount classified as a
    /// revenue deduction (not an expense), so the period nets to zero and a DRE stays right while
    /// both sides remain auditable.
    /// </summary>
    public async Task ReversePaymentAsync(SvcPayment payment, string description, CancellationToken ct = default)
    {
        var when = payment.VoidedAt ?? DateTime.UtcNow;
        var original = (await _financial.GetTransactionsByReferenceAsync(PaymentReference, payment.Id, ct))
            .FirstOrDefault(t => t.Status == TransactionStatus.Paid);
        if (original is not null)
        {
            await _financial.AddTransactionAsync(FinancialTransaction.CreateReversal(
                original, await AccountIdAsync(FinancialAccountType.Payable, ct), description, when,
                _currentUser.UserId, PaymentVoidReference, payment.Id), ct);
            return;
        }
        // Payment recorded before the financeiro mirror existed: still post the deduction.
        await PostSettledAsync(
            FinancialAccountType.Payable, TransactionType.Payable, FinancialCategory.RevenueDeduction,
            payment.Amount, when, description, PaymentVoidReference, payment.Id, StoreOf(payment.StoreId), ct,
            payment.CustomerId);
    }

    /// <summary>Records a commission payout already handed over.</summary>
    public Task PostCommissionPayoutAsync(
        decimal amount, DateTime paidAt, string description, Guid payoutId, Guid storeId, CancellationToken ct = default)
        => amount <= 0 ? Task.CompletedTask : PostSettledAsync(   // nothing handed over, nothing to record
            FinancialAccountType.Payable, TransactionType.Payable, FinancialCategory.Commission,
            amount, paidAt, description, CommissionPayoutReference, payoutId, StoreOf(storeId), ct);

    private async Task PostSettledAsync(
        FinancialAccountType accountType, TransactionType transactionType, FinancialCategory category,
        decimal amount, DateTime when, string description,
        string referenceType, Guid referenceId, Guid? storeId, CancellationToken ct, Guid? customerId = null)
    {
        var transaction = FinancialTransaction.Create(
            tenantId:           _currentTenant.Id,
            financialAccountId: await AccountIdAsync(accountType, ct),
            transactionType:    transactionType,
            category:           category,
            amount:             amount,
            description:        description,
            dueDate:            when,
            createdByUserId:    _currentUser.UserId,
            referenceType:      referenceType,
            referenceId:        referenceId,
            storeId:            storeId,
            competenceDate:     when,
            customerId:         customerId);

        transaction.MarkPaid(when);
        await _financial.AddTransactionAsync(transaction, ct);
    }

    private async Task<Guid> AccountIdAsync(FinancialAccountType accountType, CancellationToken ct)
        => (await _financial.GetDefaultByTypeAsync(accountType, ct)
            ?? throw new DomainException(
                $"Tenant has no active '{Describe(accountType)}' account. " +
                "It is created automatically for new tenants — an older tenant may need a backfill.")).Id;

    /// <summary>A record not saved yet has no StoreId — it is being created in the request's store.</summary>
    private Guid? StoreOf(Guid storeId)
        => storeId != Guid.Empty ? storeId : _currentStore.IsResolved ? _currentStore.Id : null;

    private static string Describe(FinancialAccountType type) => type switch
    {
        FinancialAccountType.Receivable => "Contas a Receber",
        FinancialAccountType.Payable    => "Contas a Pagar",
        _                               => type.ToString(),
    };
}
