using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Wire shape of the resolved Service preset. Serialized camelCase by the API.</summary>
public sealed record ServicePresetDto(
    string Key,
    string DisplayName,
    ServiceLabelsDto Labels,
    ServiceCapabilitiesDto Capabilities);

public sealed record ServiceLabelsDto(
    string Customer,
    string Professional,
    string CatalogItem,
    string Appointment,
    string Order,
    string Subject);

public sealed record ServiceCapabilitiesDto(
    bool Appointments,
    bool Orders,
    bool Quotes,
    bool Parts,
    bool Packages,
    bool SimpleRecord,
    bool Commissions,
    bool Recurrence,
    string? SubjectKind);
