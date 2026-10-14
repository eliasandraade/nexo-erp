namespace Nexo.Application.Modules.Varejo;

// ── Requests ─────────────────────────────────────────────────────────────────

public record CreatePriceListRequest(
    string Name,
    string? Description = null,
    bool IsDefault = false,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    int Priority = 0);

public record UpdatePriceListRequest(
    string Name,
    string? Description = null,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    int Priority = 0);

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
    DateTime CreatedAt,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    int Priority = 0);

public record PriceListCustomerDto(Guid CustomerId, string Name);

public record PriceListDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<PriceListItemDto> Items,
    DateTime CreatedAt,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    int Priority = 0,
    IReadOnlyList<PriceListCustomerDto>? Customers = null);

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
