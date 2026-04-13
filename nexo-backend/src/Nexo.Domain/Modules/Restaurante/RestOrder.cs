using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Comanda operacional do restaurante.
///
/// Estado: Open → InPreparation → Ready → Closed → (pago via PayAsync)
///         Open | InPreparation | Ready → Cancelled
///
/// Closed = Sale criada em Draft. A mesa continua Occupied.
/// A mesa só fica Available após PayAsync bem-sucedido.
///
/// Concorrência: a restrição de "uma comanda aberta por mesa" é garantida
/// pelo índice UNIQUE parcial (tenant_id, table_id) WHERE status NOT IN ('Closed','Cancelled'),
/// aplicado na migration. O OrderService usa SELECT FOR UPDATE (row-level lock)
/// ao validar a mesa antes de abrir uma nova comanda.
/// </summary>
public class RestOrder : StoreEntity
{
    private RestOrder() { }
    private RestOrder(Guid tenantId) : base(tenantId) { }

    public int              OrderNumber  { get; private set; }
    public RestOrderStatus  Status       { get; private set; }
    public Guid             TableId      { get; private set; }
    public Guid             WaiterId     { get; private set; }
    public Guid?            CustomerId   { get; private set; }
    public Guid?            SaleId       { get; private set; }   // preenchido em CloseAsync
    public string?          Notes        { get; private set; }

    public DateTime  OpenedAt    { get; private set; }
    public DateTime? ClosedAt    { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    // Navigation
    public RestTable? Table { get; private set; }

    private readonly List<RestOrderItem> _items = [];
    public IReadOnlyList<RestOrderItem> Items => _items.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RestOrder Create(
        Guid tenantId, int orderNumber, Guid tableId,
        Guid waiterId, Guid? customerId = null, string? notes = null)
        => new RestOrder(tenantId)
        {
            OrderNumber = orderNumber,
            Status      = RestOrderStatus.Open,
            TableId     = tableId,
            WaiterId    = waiterId,
            CustomerId  = customerId,
            Notes       = notes?.Trim(),
            OpenedAt    = DateTime.UtcNow,
        };

    // ── Items ─────────────────────────────────────────────────────────────────

    public RestOrderItem AddItem(
        Guid tenantId, Guid productId,
        decimal quantity, decimal unitPrice, string? notes = null)
    {
        EnsureModifiable();
        var item = RestOrderItem.Create(tenantId, Id, productId, quantity, unitPrice, notes);
        _items.Add(item);
        return item;
    }

    public void CancelItem(Guid itemId)
    {
        EnsureModifiable();
        var item = _items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("OrderItem", itemId);
        item.Cancel();
    }

    // ── Computed ──────────────────────────────────────────────────────────────

    public decimal Subtotal => _items.Where(i => i.IsActive).Sum(i => i.Total);

    public IReadOnlyList<RestOrderItem> ActiveItems => _items.Where(i => i.IsActive).ToList();

    // ── State machine ─────────────────────────────────────────────────────────

    public void SetInPreparation()
    {
        if (Status != RestOrderStatus.Open)
            throw new DomainException($"Order must be Open to move to InPreparation (current: {Status}).");
        Status = RestOrderStatus.InPreparation;
        SetUpdatedAt();
    }

    public void SetReady()
    {
        if (Status is not (RestOrderStatus.Open or RestOrderStatus.InPreparation))
            throw new DomainException($"Order cannot be set to Ready from {Status}.");
        Status = RestOrderStatus.Ready;
        SetUpdatedAt();
    }

    /// <summary>
    /// Fecha a comanda e vincula a Sale gerada em Draft.
    /// Pré-condição: pelo menos 1 item ativo.
    /// </summary>
    public void Close(Guid saleId)
    {
        if (Status is RestOrderStatus.Closed or RestOrderStatus.Cancelled)
            throw new DomainException($"Cannot close an order with status {Status}.");
        if (!ActiveItems.Any())
            throw new DomainException("Cannot close an order with no active items.");

        Status   = RestOrderStatus.Closed;
        SaleId   = saleId;
        ClosedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Marks the order as Paid after SaleService.ConfirmAsync succeeds.
    /// Pre-condition: order must be in Closed status.
    /// </summary>
    public void MarkPaid()
    {
        if (Status != RestOrderStatus.Closed)
            throw new DomainException($"Cannot mark order as Paid from status {Status}.");
        Status = RestOrderStatus.Paid;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == RestOrderStatus.Closed)
            throw new DomainException("Order is already Closed. Cancel the Sale instead.");
        if (Status == RestOrderStatus.Paid)
            throw new DomainException("Order is already Paid and cannot be cancelled.");
        if (Status == RestOrderStatus.Cancelled)
            throw new DomainException("Order is already Cancelled.");
        Status      = RestOrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    public bool IsModifiable =>
        Status is RestOrderStatus.Open or RestOrderStatus.InPreparation or RestOrderStatus.Ready;

    private void EnsureModifiable()
    {
        if (!IsModifiable)
            throw new DomainException($"Order cannot be modified in status {Status}.");
    }
}
