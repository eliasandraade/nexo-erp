namespace Nexo.Application.Features.Products;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateProductRequest(
    string Code,
    string Name,
    string Unit,                // "Unit" | "Kg" | "L" | "M" | "Box" | "Pack"
    decimal SalePrice,
    decimal CostPrice = 0,
    string? Barcode = null,
    string? Description = null,
    Guid? CategoryId = null,
    bool TrackStock = true,
    decimal? MinStockQuantity = null,
    decimal? MaxStockQuantity = null,
    bool IsIngredient = false);

public record UpdateProductRequest(
    string Name,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool TrackStock,
    string? Barcode = null,
    string? Description = null,
    Guid? CategoryId = null,
    decimal? MinStockQuantity = null,
    decimal? MaxStockQuantity = null,
    bool IsIngredient = false);

public record UpdateProductPricesRequest(decimal CostPrice, decimal SalePrice);

// ── Responses ───────────────────────────────────────────────────────────────

public record ProductDto(
    Guid Id,
    string Code,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool TrackStock,
    decimal? MinStockQuantity,
    decimal? MaxStockQuantity,
    bool IsActive,
    bool IsMenuVisible,
    bool IsIngredient,
    string? ImageUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);
