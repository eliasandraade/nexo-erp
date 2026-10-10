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
    Guid?    ProfessionalId,
    Guid?    AppointmentId,
    decimal? BaseAmountSnapshot,
    decimal? CommissionPercentSnapshot,
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

/// <summary>
/// ProfessionalId is who performed the consumed service (earns the commission). When omitted, it
/// is inherited from the linked order item / order, then from the linked appointment.
/// AppointmentId links the consumption to the appointment it paid for — this is what stops the
/// same service from being commissioned by the appointment AND by the package.
/// </summary>
public sealed record ConsumeSvcPackageRequest(
    Guid    CatalogItemId,
    decimal Quantity,
    Guid?   OrderId        = null,
    Guid?   OrderItemId    = null,
    string? Notes          = null,
    Guid?   ProfessionalId = null,
    Guid?   AppointmentId  = null);
