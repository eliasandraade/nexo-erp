using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Per-store Service configuration. Holds the chosen internal preset (the vertical "ramo")
/// that adapts labels + capabilities for the single Service engine.
///
/// This is the separation the v1.1 correction introduces: the tenant is granted ONE commercial
/// module ("service"); the vertical is NOT a module — it is configured here, once per store,
/// via the in-app onboarding. One row per (tenant, store).
/// </summary>
public class SvcSettings : StoreEntity
{
    private SvcSettings() { }                            // EF Core
    private SvcSettings(Guid tenantId) : base(tenantId) { }

    /// <summary>The active internal preset key — one of <see cref="ServicePresetRegistry"/>'s verticals.</summary>
    public string PresetKey { get; private set; } = string.Empty;

    // ── Public booking portal (PR12) ─────────────────────────────────────────────
    // Per-store configuration for the customer-facing booking site. The slug itself lives on
    // Store.PublicSlug (globally unique) and is reused as-is — it is NOT duplicated here.

    /// <summary>Master switch for the public booking site. False ⇒ booking endpoints answer 403.</summary>
    public bool PublicBookingEnabled { get; private set; }

    /// <summary>How many days into the future a client may book. Default 14.</summary>
    public int BookingDaysAhead { get; private set; } = 14;

    /// <summary>Minimum minutes between "now" and a bookable slot (antecedência mínima). Default 120.</summary>
    public int MinLeadMinutes { get; private set; } = 120;

    /// <summary>Slot grid step in minutes used to enumerate availability. Default 30.</summary>
    public int SlotIntervalMinutes { get; private set; } = 30;

    /// <summary>Whether catalog prices are shown on the public site. Default true.</summary>
    public bool ShowPrices { get; private set; } = true;

    /// <summary>When true, a public booking is created already Confirmed; otherwise it stays Scheduled.</summary>
    public bool AutoConfirmAppointments { get; private set; }

    /// <summary>IANA timezone the working hours are expressed in (wall-clock → UTC). Default America/Sao_Paulo.</summary>
    public string TimeZoneId { get; private set; } = "America/Sao_Paulo";

    /// <summary>App-path factory: StoreId is auto-injected on INSERT by the interceptor.</summary>
    public static SvcSettings Create(Guid tenantId, string presetKey)
    {
        var key = Normalize(presetKey);
        EnsureValidPreset(key);
        return new SvcSettings(tenantId) { PresetKey = key };
    }

    /// <summary>
    /// Updates the public booking configuration. Numeric inputs are range-validated so a bad
    /// payload can never poison availability generation (e.g. a non-positive slot interval).
    /// </summary>
    public void UpdatePublicBooking(
        bool enabled, int bookingDaysAhead, int minLeadMinutes, int slotIntervalMinutes,
        bool showPrices, bool autoConfirmAppointments, string timeZoneId)
    {
        if (bookingDaysAhead is < 1 or > 365)
            throw new DomainException("BookingDaysAhead must be between 1 and 365.");
        if (minLeadMinutes is < 0 or > 43200)
            throw new DomainException("MinLeadMinutes must be between 0 and 43200 (30 days).");
        if (slotIntervalMinutes is < 5 or > 240)
            throw new DomainException("SlotIntervalMinutes must be between 5 and 240.");
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("TimeZoneId is required.");

        PublicBookingEnabled    = enabled;
        BookingDaysAhead        = bookingDaysAhead;
        MinLeadMinutes          = minLeadMinutes;
        SlotIntervalMinutes     = slotIntervalMinutes;
        ShowPrices              = showPrices;
        AutoConfirmAppointments = autoConfirmAppointments;
        TimeZoneId              = timeZoneId.Trim();
        SetUpdatedAt();
    }

    /// <summary>
    /// Seeding factory with an explicit store — used only by the data seeder, which has no
    /// request/store context for the interceptor to inject from.
    /// </summary>
    public static SvcSettings CreateForStore(Guid tenantId, Guid storeId, string presetKey)
    {
        var settings = Create(tenantId, presetKey);
        settings.SetStoreId(storeId);
        return settings;
    }

    public void SetPreset(string presetKey)
    {
        var key = Normalize(presetKey);
        EnsureValidPreset(key);
        PresetKey = key;
        SetUpdatedAt();
    }

    private static string Normalize(string? presetKey) =>
        (presetKey ?? string.Empty).Trim().ToLowerInvariant();

    private static void EnsureValidPreset(string presetKey)
    {
        if (!ServicePresetRegistry.IsValidPresetKey(presetKey))
            throw new DomainException($"Invalid service preset key: '{presetKey}'.");
    }
}
