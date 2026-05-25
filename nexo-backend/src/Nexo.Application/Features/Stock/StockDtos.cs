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

// Enriched DTO for the paged inventory listing — includes product fields to
// avoid separate /api/products call from the frontend.
public record StockPagedItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string Unit,
    Guid? CategoryId,
    string? CategoryName,
    decimal? MinStockQuantity,
    decimal CurrentQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime? LastMovementAt);

// Extends paged result with KPI summary counts so the inventory page can
// display header cards without a separate aggregation request.
public record StockPagedResponse(
    IReadOnlyList<StockPagedItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int BelowMinCount,
    int NoTurnoverCount)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNext   => Page < TotalPages;
    public bool HasPrev   => Page > 1;
}

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
