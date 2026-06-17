using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Editable appointment fields shared by create + update, validated by one rule set.</summary>
public interface ISvcAppointmentFields
{
    Guid     CustomerId { get; }
    Guid     ProfessionalId { get; }
    Guid     CatalogItemId { get; }
    Guid?    SubjectId { get; }
    DateTime StartsAt { get; }
    DateTime EndsAt { get; }
    string?  Notes { get; }
}

public sealed record SvcAppointmentDto(
    Guid                 Id,
    Guid                 StoreId,
    Guid                 CustomerId,
    Guid                 ProfessionalId,
    Guid                 CatalogItemId,
    Guid?                SubjectId,
    DateTime             StartsAt,
    DateTime             EndsAt,
    SvcAppointmentStatus Status,
    string?              Notes,
    string?              CancellationReason,
    decimal              PriceSnapshot,
    DateTime             CreatedAt,
    DateTime             UpdatedAt);

public sealed record CreateSvcAppointmentRequest(
    Guid     CustomerId,
    Guid     ProfessionalId,
    Guid     CatalogItemId,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid?    SubjectId = null,
    string?  Notes     = null) : ISvcAppointmentFields;

public sealed record UpdateSvcAppointmentRequest(
    Guid     CustomerId,
    Guid     ProfessionalId,
    Guid     CatalogItemId,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid?    SubjectId = null,
    string?  Notes     = null) : ISvcAppointmentFields;

public sealed record ChangeSvcAppointmentStatusRequest(
    SvcAppointmentStatus? Status,
    string?               Reason = null);
