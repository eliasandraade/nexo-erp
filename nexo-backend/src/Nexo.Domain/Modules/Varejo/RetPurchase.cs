using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Pedido de compra / entrada de mercadoria de UMA loja (a que recebe — o estoque é por loja).
/// Estado: Draft → (Ordered) → Confirmed (recebida) · Draft/Ordered/Confirmed → Cancelled
///
/// Confirmed: entrada no estoque (PurchaseEntry), custo do produto e conta a pagar ao fornecedor.
/// Cancelled: se já recebida, a mercadoria sai do estoque e o financeiro é estornado.
/// </summary>
public class RetPurchase : StoreEntity
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
    public DateTime? OrderedAt { get; private set; }    // enviado ao fornecedor
    public DateTime? ConfirmedAt { get; private set; }  // recebida
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

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
            ReceivedAt     = Utc(receivedAt),   // set on receipt unless known upfront
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

        if (_items.Any(i => i.ProductId == productId))
            throw new DomainException("Este produto já está no pedido. Remova a linha e lance de novo com a quantidade total.");
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
    /// <summary>Draft → Ordered: the order goes to the supplier; its items are frozen.</summary>
    public void MarkOrdered()
    {
        EnsureIsDraft();
        if (!_items.Any())
            throw new DomainException("Inclua ao menos um item antes de enviar o pedido.");

        Status    = RetPurchaseStatus.Ordered;
        OrderedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>Draft/Ordered → Confirmed (recebida).</summary>
    public void Confirm(DateTime? receivedAt = null, string? invoiceNumber = null)
    {
        if (Status is not (RetPurchaseStatus.Draft or RetPurchaseStatus.Ordered))
            throw new DomainException($"Só um pedido em elaboração ou enviado pode ser recebido (status atual: {Status}).");

        if (!_items.Any())
            throw new DomainException("Cannot confirm a purchase with no items.");

        // Received = when the goods arrive (now unless informed) — never when the draft was created.
        ReceivedAt = Utc(receivedAt) ?? DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(invoiceNumber)) InvoiceNumber = invoiceNumber.Trim();
        Status      = RetPurchaseStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Cancela a compra. Se já confirmada, o PurchaseService é responsável
    /// por reverter o estoque antes de chamar este método.
    /// </summary>
    public void Cancel(string? reason = null)
    {
        if (Status == RetPurchaseStatus.Cancelled)
            throw new DomainException("Purchase is already cancelled.");
        if (reason is { Length: > 500 })
            throw new DomainException("O motivo do cancelamento tem no máximo 500 caracteres.");

        Status             = RetPurchaseStatus.Cancelled;
        CancelledAt        = DateTime.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
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

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        { } v => v.ToUniversalTime(),
    };

    private void RecalculateTotal()
    {
        TotalAmount = _items.Sum(x => x.Total);
        SetUpdatedAt();
    }
}
