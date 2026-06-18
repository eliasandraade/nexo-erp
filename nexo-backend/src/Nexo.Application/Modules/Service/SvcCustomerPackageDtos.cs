using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcCustomerPackageItemDto(
    Guid     Id,
    Guid     CustomerPackageId,
    Guid     CatalogItemId,
    string   NameSnapshot,
    decimal  TotalQuantity,
    decimal  RemainingQuantity,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SvcPackageUsageDto(
    Guid     Id,
    Guid     CustomerPackageId,
    Guid     CustomerPackageItemId,
    Guid     CatalogItemId,
    Guid?    OrderId,
    Guid?    OrderItemId,
    decimal  Quantity,
    string?  Notes,
    DateTime CreatedAt);

public sealed record SvcCustomerPackageDto(
    Guid                                  Id,
    Guid                                  StoreId,
    string                                Code,
    Guid                                  PackageId,
    Guid                                  CustomerId,
    Guid?                                 SubjectId,
    SvcCustomerPackageStatus              Status,
    DateTime                              StartsAt,
    DateTime?                             ExpiresAt,
    decimal                               PriceSnapshot,
    string?                               Notes,
    IReadOnlyList<SvcCustomerPackageItemDto> Items,
    IReadOnlyList<SvcPackageUsageDto>     Usages,
    DateTime                              CreatedAt,
    DateTime                              UpdatedAt);

public sealed record AssignSvcCustomerPackageRequest(
    Guid     PackageId,
    Guid     CustomerId,
    DateTime StartsAt,
    Guid?    SubjectId = null,
    string?  Notes     = null);

public sealed record ConsumeSvcPackageRequest(
    Guid    CatalogItemId,
    decimal Quantity,
    Guid?   OrderId     = null,
    Guid?   OrderItemId = null,
    string? Notes       = null);
