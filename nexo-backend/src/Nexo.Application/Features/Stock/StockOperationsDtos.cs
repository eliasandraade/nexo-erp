namespace Nexo.Application.Features.Stock;

// ── Inventário ──────────────────────────────────────────────────────────────

/// <summary>ProductIds null/empty = every active product that tracks stock.</summary>
public record CreateInventoryCountRequest(List<Guid>? ProductIds = null, string? Notes = null);

/// <summary>CountedQuantity null clears a count typed by mistake.</summary>
public record InventoryCountEntry(Guid ProductId, decimal? CountedQuantity);

public record UpdateInventoryCountRequest(List<InventoryCountEntry> Items);

public record InventoryCountItemDto(
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? SystemQuantityAtCount,
    decimal? SystemQuantityAtApply,
    decimal? Difference);

public record InventoryCountDto(
    Guid Id,
    int Number,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    int CountedItems,
    IReadOnlyList<InventoryCountItemDto> Items);

// ── Transferência ───────────────────────────────────────────────────────────

public record StockTransferItemInput(Guid ProductId, decimal Quantity);

/// <summary>From the current store to <paramref name="ToStoreId"/> (one of the user's stores).</summary>
public record CreateStockTransferRequest(Guid ToStoreId, List<StockTransferItemInput> Items, string? Notes = null);

public record StockTransferItemDto(Guid ProductId, string ProductName, decimal Quantity);

public record StockTransferDto(
    Guid Id,
    int Number,
    Guid FromStoreId,
    Guid ToStoreId,
    /// <summary>"out" when the current store sent it, "in" when it received it.</summary>
    string Direction,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<StockTransferItemDto> Items);
