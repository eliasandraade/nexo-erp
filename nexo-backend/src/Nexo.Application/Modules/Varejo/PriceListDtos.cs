namespace Nexo.Application.Modules.Varejo;

// ── Requests ─────────────────────────────────────────────────────────────────

public record CreatePriceListRequest(
    string Name,
    string? Description = null,
    bool IsDefault = false);

public record UpdatePriceListRequest(
    string Name,
    string? Description = null);

public record SetProductPriceRequest(
    Guid ProductId,
    decimal Price);

// ── Responses ─────────────────────────────────────────────────────────────────

public record PriceListItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal Price);

public record PriceListDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive,
    int ItemCount,
    DateTime CreatedAt);

public record PriceListDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<PriceListItemDto> Items,
    DateTime CreatedAt);

// ── PDV ───────────────────────────────────────────────────────────────────────

/// <summary>Preço resolvido para um produto no PDV.</summary>
public record ResolvedPriceDto(
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal ResolvedPrice,
    string Source,           // "PriceList" | "Default"
    Guid? PriceListId,
    string? PriceListName);
