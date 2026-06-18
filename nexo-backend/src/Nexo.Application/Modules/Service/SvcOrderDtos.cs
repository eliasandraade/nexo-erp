using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcOrderItemDto(
    Guid     Id,
    Guid     OrderId,
    Guid     CatalogItemId,
    Guid?    ProfessionalId,
    string   NameSnapshot,
    string?  DescriptionSnapshot,
    decimal  Quantity,
    decimal  UnitPriceSnapshot,
    decimal? CommissionPercentSnapshot,
    decimal  TotalAmount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SvcOrderDto(
    Guid                           Id,
    Guid                           StoreId,
    string                         Code,
    Guid                           CustomerId,
    Guid?                          SubjectId,
    Guid?                          ProfessionalId,
    Guid?                          AppointmentId,
    SvcOrderStatus                 Status,
    string?                        Notes,
    string?                        CancellationReason,
    decimal                        TotalAmount,
    IReadOnlyList<SvcOrderItemDto> Items,
    DateTime                       CreatedAt,
    DateTime                       UpdatedAt);

public sealed record CreateSvcOrderRequest(
    Guid    CustomerId,
    Guid?   SubjectId      = null,
    Guid?   ProfessionalId = null,
    string? Notes          = null);

public sealed record UpdateSvcOrderRequest(
    Guid?   SubjectId      = null,
    Guid?   ProfessionalId = null,
    string? Notes          = null);

public sealed record ChangeSvcOrderStatusRequest(
    SvcOrderStatus? Status,
    string?         Reason = null);

public sealed record AddSvcOrderItemRequest(
    Guid    CatalogItemId,
    decimal Quantity,
    Guid?   ProfessionalId = null);

public sealed record UpdateSvcOrderItemRequest(
    decimal Quantity,
    Guid?   ProfessionalId = null);
