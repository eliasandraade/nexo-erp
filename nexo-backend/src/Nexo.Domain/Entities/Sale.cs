using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Aggregate root for a sales transaction.
///
/// State machine (enforced in domain — never rely on service layer alone):
///   Draft → Confirmed → Paid      (terminal)
///   Draft → Cancelled             (no stock/cash/financial side-effects)
///   Confirmed → Cancelled         (reverses stock + cash/financial)
///   Paid → (nothing)              (create a return sale instead)
///
/// Rules:
///   - Items can only be added/removed while Status == Draft
///   - Confirmation is atomic (transaction wraps stock + payment record creation)
///   - SaleItem.UnitPrice and SaleItem.CostPrice are snapshots at time of sale
/// </summary>
public class Sale : StoreEntity
{
    private Sale() { }
    private Sale(Guid tenantId) : base(tenantId) { }

    public int Number { get; private set; }                         // sequential per tenant
    public SaleStatus Status { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid SoldByUserId { get; private set; }
    public Guid? CashSessionId { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal SurchargesAmount { get; private set; }
    public decimal Total { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    /// <summary>Sum refunded by devoluções (SaleReturn) so far.</summary>
    public decimal ReturnedAmount { get; private set; }
    /// <summary>"CPF/CNPJ na nota": digits only, validated by the service; null when not asked for.</summary>
    public string? ConsumerDocument { get; private set; }

    // Navigation
    public Customer? Customer { get; private set; }
    public User? SoldBy { get; private set; }
    public CashSession? CashSession { get; private set; }
    public ICollection<SaleItem> Items { get; private set; } = [];
    public ICollection<SalePayment> Payments { get; private set; } = [];

    // ── Factory ───────────────────────────────────────────────────────────────

    public static Sale Create(
        Guid tenantId,
        int number,
        Guid soldByUserId,
        Guid? customerId = null,
        Guid? cashSessionId = null,
        string? notes = null)
    {
        return new Sale(tenantId)
        {
            Number        = number,
            Status        = SaleStatus.Draft,
            CustomerId    = customerId,
            SoldByUserId  = soldByUserId,
            CashSessionId = cashSessionId,
            Notes         = notes?.Trim(),
        };
    }

    // ── Totals ────────────────────────────────────────────────────────────────

    public void RecalculateTotals(
        IEnumerable<SaleItem> items,
        decimal discountAmount = 0,
        decimal taxAmount = 0,
        decimal surchargesAmount = 0)
    {
        EnsureCanBeModified();
        if (discountAmount < 0 || taxAmount < 0 || surchargesAmount < 0)
            throw new DomainException("Desconto, impostos e acréscimos não podem ser negativos.");

        // Money is kept in cents: a 10% discount on 33,33 must not leave a 29,997 total that no
        // Pix/card payment can ever match.
        Subtotal         = Cents(items.Sum(i => i.Total));
        DiscountAmount   = Cents(discountAmount);
        TaxAmount        = Cents(taxAmount);
        SurchargesAmount = Cents(surchargesAmount);
        Total            = Subtotal - DiscountAmount + TaxAmount + SurchargesAmount;
        SetUpdatedAt();
    }

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ── State transitions ─────────────────────────────────────────────────────

    /// <summary>The consumer's CPF/CNPJ for the nota, set while the sale is still being closed.</summary>
    public void SetConsumerDocument(string? digits)
    {
        if (Status != SaleStatus.Draft)
            throw new DomainException("O CPF/CNPJ na nota só pode ser informado antes de finalizar a venda.");
        ConsumerDocument = string.IsNullOrEmpty(digits) ? null : digits;
    }

    /// <summary>
    /// Draft → Confirmed.
    /// Called after stock has been deducted and payment records have been persisted.
    /// </summary>
    public void Confirm()
    {
        if (Status != SaleStatus.Draft)
            throw new DomainException($"Only Draft sales can be confirmed. Current: {Status}.");

        Status      = SaleStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Confirmed → Paid.
    /// Called immediately when all payments are Cash, or later when all credit transactions settle.
    /// </summary>
    public void MarkPaid()
    {
        if (Status != SaleStatus.Confirmed)
            throw new DomainException($"Only Confirmed sales can be marked Paid. Current: {Status}.");

        Status = SaleStatus.Paid;
        PaidAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Draft or Confirmed → Cancelled.
    /// Caller must reverse stock/cash/financial before calling this.
    /// </summary>
    public void Cancel(Guid? cancelledByUserId = null, string? reason = null)
    {
        if (Status is SaleStatus.Paid or SaleStatus.PartiallyReturned or SaleStatus.Returned)
            throw new DomainException("Venda paga não é cancelada: registre uma devolução.");

        if (Status == SaleStatus.Cancelled)
            throw new DomainException("Sale is already cancelled.");

        if (reason is { Length: > 500 })
            throw new DomainException("O motivo do cancelamento tem no máximo 500 caracteres.");

        CancelledByUserId  = cancelledByUserId;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Status      = SaleStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void UpdateNotes(string? notes)
    {
        EnsureCanBeModified();
        Notes = notes?.Trim();
        SetUpdatedAt();
    }

    public void LinkCashSession(Guid cashSessionId)
    {
        EnsureCanBeModified();
        CashSessionId = cashSessionId;
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    /// <summary>Paid / partially returned sales accept a devolução.</summary>
    public bool CanBeReturned => Status is SaleStatus.Paid or SaleStatus.PartiallyReturned;

    /// <summary>Records a devolução of <paramref name="amount"/>; fully returned when nothing is left.</summary>
    public void RegisterReturn(decimal amount, bool fullyReturned)
    {
        if (!CanBeReturned)
            throw new DomainException($"A venda #{Number} não aceita devolução no status {Status}.");
        if (amount < 0 || ReturnedAmount + amount > Total)
            throw new DomainException("O valor devolvido ultrapassa o total da venda.");

        ReturnedAmount += amount;
        Status = fullyReturned ? SaleStatus.Returned : SaleStatus.PartiallyReturned;
        SetUpdatedAt();
    }

    public bool IsInDraft   => Status == SaleStatus.Draft;
    public bool IsCancelled => Status == SaleStatus.Cancelled;

    /// <summary>True if ConfirmAsync was already called (stock was deducted).</summary>
    public bool WasConfirmed => ConfirmedAt.HasValue;

    private void EnsureCanBeModified()
    {
        if (Status != SaleStatus.Draft)
            throw new DomainException($"Sale #{Number} cannot be modified in status '{Status}'.");
    }
}
