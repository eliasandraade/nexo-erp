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
    public decimal Total { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

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

    public void RecalculateTotals(IEnumerable<SaleItem> items, decimal discountAmount = 0, decimal taxAmount = 0)
    {
        EnsureCanBeModified();
        Subtotal       = items.Sum(i => i.Total);
        DiscountAmount = discountAmount;
        TaxAmount      = taxAmount;
        Total          = Subtotal - DiscountAmount + TaxAmount;
        SetUpdatedAt();
    }

    // ── State transitions ─────────────────────────────────────────────────────

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
    public void Cancel()
    {
        if (Status == SaleStatus.Paid)
            throw new DomainException("Paid sales cannot be cancelled. Create a return sale instead.");

        if (Status == SaleStatus.Cancelled)
            throw new DomainException("Sale is already cancelled.");

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
