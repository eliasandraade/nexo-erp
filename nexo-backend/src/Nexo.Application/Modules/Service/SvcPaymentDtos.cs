using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcPaymentDto(
    Guid             Id,
    Guid             StoreId,
    Guid             CustomerId,
    Guid?            OrderId,
    Guid?            CustomerPackageId,
    decimal          Amount,
    SvcPaymentMethod Method,
    SvcPaymentStatus Status,
    DateTime         PaidAt,
    string?          ExternalReference,
    string?          Notes,
    string?          VoidReason,
    DateTime?        VoidedAt,
    DateTime         CreatedAt,
    DateTime         UpdatedAt);

public sealed record SvcPaymentSummaryDto(
    Guid    TargetId,
    string  TargetType,         // "Order" | "CustomerPackage"
    decimal TotalAmount,
    decimal PaidAmount,
    decimal VoidedAmount,
    decimal RemainingAmount,
    bool    IsFullyPaid);

public sealed record CreateSvcPaymentRequest(
    decimal          Amount,
    SvcPaymentMethod Method,
    DateTime         PaidAt,
    Guid?            OrderId           = null,
    Guid?            CustomerPackageId = null,
    string?          ExternalReference = null,
    string?          Notes             = null);

public sealed record VoidSvcPaymentRequest(string? Reason = null);
