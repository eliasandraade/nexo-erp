namespace Nexo.Application.Modules.Service;

public sealed record SvcPackageItemDto(
    Guid     Id,
    Guid     PackageId,
    Guid     CatalogItemId,
    string   NameSnapshot,
    decimal  IncludedQuantity,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SvcPackageDto(
    Guid                          Id,
    Guid                          StoreId,
    string                        Name,
    string?                       Description,
    decimal                       Price,
    int?                          ValidityDays,
    bool                          IsActive,
    IReadOnlyList<SvcPackageItemDto> Items,
    DateTime                      CreatedAt,
    DateTime                      UpdatedAt);

public sealed record CreateSvcPackageRequest(
    string  Name,
    decimal Price,
    string? Description  = null,
    int?    ValidityDays = null);

public sealed record UpdateSvcPackageRequest(
    string  Name,
    string? Description  = null,
    int?    ValidityDays = null);

public sealed record UpdateSvcPackagePriceRequest(decimal Price);

public sealed record AddSvcPackageItemRequest(Guid CatalogItemId, decimal IncludedQuantity);

public sealed record UpdateSvcPackageItemRequest(decimal IncludedQuantity);
