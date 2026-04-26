namespace Nexo.Application.Modules.Restaurante;

// ── Public menu response ──────────────────────────────────────────────────────

public record PublicModifierDto(
    Guid    Id,
    string  Name,
    decimal PriceAdjustment);

public record PublicModifierGroupDto(
    Guid    Id,
    string  Name,
    bool    IsRequired,
    int     MinSelections,
    int     MaxSelections,
    IReadOnlyList<PublicModifierDto> Options);

public record PublicMenuProductDto(
    Guid    Id,
    string  Name,
    string? Description,
    decimal Price,
    string? ImageUrl,
    IReadOnlyList<PublicModifierGroupDto> ModifierGroups);

public record PublicMenuCategoryDto(
    Guid?   Id,
    string  Name,
    int     SortOrder,
    IReadOnlyList<PublicMenuProductDto> Products);

public record PublicMenuDto(
    string  StoreName,
    string? Description,
    string? LogoUrl,
    string? CoverImageUrl,
    string? WhatsAppPhone,
    string? BusinessHoursJson,
    bool    AcceptingOrders,
    bool    DeliveryEnabled,
    bool    TakeawayEnabled,
    IReadOnlyList<PublicMenuCategoryDto> Categories);
