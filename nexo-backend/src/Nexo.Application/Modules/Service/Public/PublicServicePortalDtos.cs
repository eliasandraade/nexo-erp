using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Public;

// ── Read models (customer-facing — deliberately minimal, no internal/sensitive fields) ──────────

/// <summary>
/// Public portal header: enough for the booking site to brand itself and adapt copy to the vertical.
/// No tenantId / storeId / user / cost / commission — only what a customer needs to see.
/// </summary>
public sealed record PublicServicePortalDto(
    string              StoreName,
    string              PresetKey,
    string              PresetDisplayName,
    ServiceLabels       Labels,
    ServiceCapabilities Capabilities,
    bool                ShowPrices,
    bool                RequiresProfessionalSelection,
    bool                IsBookingEnabled);

/// <summary>A bookable service. <see cref="Price"/> is null when the store hides prices.</summary>
public sealed record PublicServiceCatalogItemDto(
    Guid     Id,
    string   Name,
    string?  Description,
    string?  Category,
    int      DurationMinutes,
    decimal? Price,
    bool     RequiresSubject);

/// <summary>A bookable professional — only public-safe presentation fields.</summary>
public sealed record PublicServiceProfessionalDto(
    Guid    Id,
    string  Name,
    string? Role,
    string? Specialty,
    string? Color);

/// <summary>A free slot, both bounds in UTC. The client echoes <see cref="StartsAt"/> back on booking.</summary>
public sealed record PublicAvailabilitySlotDto(DateTime StartsAt, DateTime EndsAt);

public sealed record PublicAvailabilityDto(
    Guid                                  ProfessionalId,
    Guid                                  CatalogItemId,
    int                                   DurationMinutes,
    IReadOnlyList<PublicAvailabilitySlotDto> Slots);

// ── Write model (booking) ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Subject detail (pet / veículo / aluno) collected only when the preset or the chosen service
/// requires it. The client never sends a SubjectId — a subject is created server-side and tied to
/// the resolved customer. <see cref="Kind"/> is optional; when omitted it defaults to the preset's
/// subject kind.
/// </summary>
public sealed record PublicAppointmentSubjectRequest(
    string  DisplayName,
    string? Kind  = null,
    string? Notes = null);

/// <summary>
/// Public booking payload. No CustomerId/SubjectId/StoreId/price are accepted from the client:
/// the customer is resolved/created from the phone, the price is snapshotted from the catalog, and
/// the store comes from the slug in the route.
/// </summary>
public sealed record CreatePublicAppointmentRequest(
    string                          CustomerName,
    string                          Phone,
    Guid                            CatalogItemId,
    Guid                            ProfessionalId,
    DateTime                        StartsAt,
    string?                         Email   = null,
    PublicAppointmentSubjectRequest? Subject = null,
    string?                         Notes   = null);

/// <summary>Booking confirmation. Echoes the human-readable details; exposes only the appointment's own id.</summary>
public sealed record PublicAppointmentCreatedDto(
    Guid     AppointmentId,
    string   Status,
    DateTime StartsAt,
    DateTime EndsAt,
    string   ServiceName,
    string   ProfessionalName,
    string   CustomerName);
