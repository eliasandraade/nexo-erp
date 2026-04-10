using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Nota de compra / entrada de mercadoria.
/// Estado: Draft → Confirmed → Cancelled
///
/// Confirmed: gera StockMovement(PurchaseEntry) e atualiza custo do produto.
/// Cancelled: reverte estoque se já confirmada.
/// </summary>
public class RetPurchase : TenantEntity
{
    private RetPurchase() { }
    private RetPurchase(Guid tenantId) : base(tenantId) { }

    public int PurchaseNumber { get; private set; }
    public RetPurchaseStatus Status { get; private set; }

    public Guid SupplierId { get; private set; }
    public Guid? UserId { get; private set; }           // quem lançou

    public decimal TotalAmount { get; private set; }
    public string? Notes { get; private set; }
    public string? InvoiceNumber { get; private set; }  // número NF/documento fiscal

    public DateTime? ReceivedAt { get; private set; }   // data de recebimento físico
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    // Navigation
    private readonly List<RetPurchaseItem> _items = [];
    public IReadOnlyList<RetPurchaseItem> Items => _items.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RetPurchase Create(
        Guid tenantId,
        Guid supplierId,
        Guid userId,
        int purchaseNumber,
        string? invoiceNumber = null,
        DateTime? receivedAt = null,
        string? notes = null)
    {
        return new RetPurchase(tenantId)
        {
            PurchaseNumber = purchaseNumber,
            Status         = RetPurchaseStatus.Draft,
            SupplierId     = supplierId,
            UserId         = userId,
            TotalAmount    = 0m,
            InvoiceNumber  = invoiceNumber?.Trim(),
            ReceivedAt     = receivedAt ?? DateTime.UtcNow,
            Notes          = notes?.Trim(),
        };
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    public RetPurchaseItem AddItem(
        Guid tenantId,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        string? notes = null)
    {
        EnsureIsDraft();

        var item = RetPurchaseItem.Create(
            tenantId, Id, productId, quantity, unitCost, notes);

        _items.Add(item);
        RecalculateTotal();
        return item;
    }

    public void RemoveItem(Guid itemId)
    {
        EnsureIsDraft();

        var item = _items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("RetPurchaseItem", itemId);

        _items.Remove(item);
        RecalculateTotal();
    }

    // ── State machine ─────────────────────────────────────────────────────────

    /// <summary>
    /// Marca como Confirmed. Side-effects (stock + cost) são responsabilidade do PurchaseService.
    /// </summary>
    public void Confirm()
    {
        if (Status != RetPurchaseStatus.Draft)
            throw new DomainException($"Cannot confirm a purchase with status {Status}.");

        if (!_items.Any())
            throw new DomainException("Cannot confirm a purchase with no items.");

        Status      = RetPurchaseStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Cancela a compra. Se já confirmada, o PurchaseService é responsável
    /// por reverter o estoque antes de chamar este método.
    /// </summary>
    public void Cancel()
    {
        if (Status == RetPurchaseStatus.Cancelled)
            throw new DomainException("Purchase is already cancelled.");

        Status      = RetPurchaseStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    public bool WasConfirmed => ConfirmedAt.HasValue;
    public bool IsDraft      => Status == RetPurchaseStatus.Draft;

    private void EnsureIsDraft()
    {
        if (Status != RetPurchaseStatus.Draft)
            throw new DomainException($"Purchase items can only be changed while status is Draft (current: {Status}).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RecalculateTotal()
    {
        TotalAmount = _items.Sum(x => x.Total);
        SetUpdatedAt();
    }
}
