namespace Nexo.Application.Modules.Restaurante;

public record CouponDto(
    Guid      Id,
    string    Code,
    string?   Description,
    string    DiscountType,
    decimal   DiscountValue,
    bool      IsActive,
    decimal?  MinOrderAmount,
    decimal?  MinDeliveryFee,
    string[]? RestrictToNeighborhoods,
    Guid[]?   RestrictToProductIds,
    bool      IsFirstOrderOnly,
    string?   RestrictToCustomerPhone,
    int?      MaxUses,
    int       UsedCount,
    DateTime? ValidFrom,
    DateTime? ValidUntil);

public record CreateCouponRequest(
    string    Code,
    string    DiscountType,
    decimal   DiscountValue,
    string?   Description             = null,
    decimal?  MinOrderAmount          = null,
    decimal?  MinDeliveryFee          = null,
    string[]? RestrictToNeighborhoods = null,
    Guid[]?   RestrictToProductIds    = null,
    bool      IsFirstOrderOnly        = false,
    string?   RestrictToCustomerPhone = null,
    int?      MaxUses                 = null,
    DateTime? ValidFrom               = null,
    DateTime? ValidUntil              = null);

public record UpdateCouponRequest(
    string    DiscountType,
    decimal   DiscountValue,
    string?   Description             = null,
    decimal?  MinOrderAmount          = null,
    decimal?  MinDeliveryFee          = null,
    string[]? RestrictToNeighborhoods = null,
    Guid[]?   RestrictToProductIds    = null,
    bool      IsFirstOrderOnly        = false,
    string?   RestrictToCustomerPhone = null,
    int?      MaxUses                 = null,
    DateTime? ValidFrom               = null,
    DateTime? ValidUntil              = null);

public record ValidateCouponRequest(
    string  PublicSlug,
    string  CouponCode,
    string  CustomerPhone,
    decimal ItemsSubtotal,
    decimal DeliveryFee,
    string? Neighborhood = null);

public record ValidateCouponResponse(
    bool    Valid,
    string? Error,
    decimal DiscountAmount,
    string  DiscountType,
    decimal DiscountValue);
