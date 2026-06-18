namespace Nexo.Application.Modules.Service;

/// <summary>
/// Wire shape of the per-store Service configuration. <see cref="IsConfigured"/> is false when
/// the store has not chosen a vertical yet — the frontend shows onboarding in that case.
/// </summary>
public sealed record ServiceSettingsDto(bool IsConfigured, string? PresetKey);

/// <summary>Sets the active internal preset (the vertical "ramo") for the current store.</summary>
public sealed record SetServicePresetRequest(string PresetKey);

/// <summary>
/// Current public booking configuration for the active store. The slug itself is managed via
/// <c>PATCH /api/stores/{id}/public-slug</c> (Store.PublicSlug) and read from the stores API, so it
/// is intentionally not duplicated here. <see cref="IsConfigured"/> is false when the store has not
/// chosen a preset yet — booking cannot be enabled before onboarding.
/// </summary>
public sealed record PublicBookingSettingsDto(
    bool   IsConfigured,
    bool   PublicBookingEnabled,
    int    BookingDaysAhead,
    int    MinLeadMinutes,
    int    SlotIntervalMinutes,
    bool   ShowPrices,
    bool   AutoConfirmAppointments,
    string TimeZoneId);

/// <summary>Updates the public booking configuration for the active store.</summary>
public sealed record UpdatePublicBookingRequest(
    bool   PublicBookingEnabled,
    int    BookingDaysAhead,
    int    MinLeadMinutes,
    int    SlotIntervalMinutes,
    bool   ShowPrices,
    bool   AutoConfirmAppointments,
    string TimeZoneId);
