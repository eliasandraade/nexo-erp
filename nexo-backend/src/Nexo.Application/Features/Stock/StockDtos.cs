namespace Nexo.Application.Features.Stock;

// ── Requests ────────────────────────────────────────────────────────────────

public record AdjustStockRequest(
    Guid ProductId,
    decimal Quantity,           // positive = entry, negative = exit
    string MovementType,        // "ManualEntry" | "ManualExit" | "Adjustment" | "Loss"
    string? Notes = null);

// ── Responses ───────────────────────────────────────────────────────────────

public record StockItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal CurrentQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime? LastMovementAt);

public record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string MovementType,
    decimal Quantity,
    decimal QuantityBefore,
    decimal QuantityAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Notes,
    Guid CreatedByUserId,
    DateTime CreatedAt);
