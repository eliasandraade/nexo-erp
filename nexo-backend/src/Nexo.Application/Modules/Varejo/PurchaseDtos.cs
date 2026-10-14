namespace Nexo.Application.Modules.Varejo;

// ── Requests ─────────────────────────────────────────────────────────────────

public record CreatePurchaseRequest(
    Guid SupplierId,
    string? InvoiceNumber = null,
    DateTime? ReceivedAt = null,
    string? Notes = null);

/// <summary>
/// Recebimento: entrada no estoque, custo e conta a pagar. DueDate = vencimento da conta (padrão:
/// recebimento + prazo de pagamento do fornecedor); PaidNow = a conta já nasce paga.
/// </summary>
public record ReceivePurchaseRequest(
    DateTime? DueDate = null,
    bool PaidNow = false,
    DateTime? ReceivedAt = null,
    string? InvoiceNumber = null);

public record CancelPurchaseRequest(string? Reason = null);

public record AddPurchaseItemRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitCost,
    string? Notes = null);

// ── Responses ─────────────────────────────────────────────────────────────────

public record PurchaseItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal Quantity,
    decimal UnitCost,
    decimal Total,
    string? Notes);

public record PurchaseDto(
    Guid Id,
    int PurchaseNumber,
    string Status,
    Guid SupplierId,
    string SupplierName,
    decimal TotalAmount,
    string? InvoiceNumber,
    DateTime? ReceivedAt,
    DateTime? ConfirmedAt,
    DateTime? CancelledAt,
    string? Notes,
    IReadOnlyList<PurchaseItemDto> Items,
    DateTime CreatedAt,
    DateTime? OrderedAt = null,
    string? CancellationReason = null);
