using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Per-store Service configuration use cases (the internal preset / vertical "ramo").
///
/// The commercial entitlement is the single "service" module (gate); the vertical lives here,
/// chosen via onboarding. <see cref="ResolveEffectivePresetKeyAsync"/> centralises resolution:
///   1. stored SvcSettings.PresetKey, else
///   2. null (not configured) → the frontend shows onboarding, never an auto-picked preset.
/// </summary>
public class SvcSettingsService
{
    private readonly ISvcSettingsRepository _repo;
    private readonly ICurrentTenant         _currentTenant;

    public SvcSettingsService(ISvcSettingsRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<string?> ResolveEffectivePresetKeyAsync(CancellationToken ct = default)
    {
        if (!_currentTenant.IsResolved) return null;

        // The per-store row is the only source of the vertical. Tenants that used to carry it in
        // their module key were rewritten by the ConvertLegacyServiceSubscriptions migration,
        // which also seeded their SvcSettings; a store with no row is genuinely unconfigured.
        var settings = await _repo.GetForCurrentStoreAsync(ct);
        return settings?.PresetKey;
    }

    public async Task<ServiceSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var key = await ResolveEffectivePresetKeyAsync(ct);
        return new ServiceSettingsDto(IsConfigured: key is not null, PresetKey: key);
    }

    public async Task<ServiceSettingsDto> SetPresetAsync(string presetKey, CancellationToken ct = default)
    {
        var existing = await _repo.GetForCurrentStoreAsync(ct);
        if (existing is null)
        {
            // Domain validates the key (invalid → DomainException → 422); the request validator
            // rejects it earlier with 400.
            var settings = SvcSettings.Create(_currentTenant.Id, presetKey);
            await _repo.AddAsync(settings, ct);
            await _repo.SaveChangesAsync(ct);
            return new ServiceSettingsDto(IsConfigured: true, PresetKey: settings.PresetKey);
        }

        existing.SetPreset(presetKey);
        _repo.Update(existing);
        await _repo.SaveChangesAsync(ct);
        return new ServiceSettingsDto(IsConfigured: true, PresetKey: existing.PresetKey);
    }

    // ── Public booking configuration ─────────────────────────────────────────────

    public async Task<PublicBookingSettingsDto> GetPublicBookingAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetForCurrentStoreAsync(ct);
        if (settings is null)
            // Not onboarded yet — surface the domain defaults with booking off.
            return new PublicBookingSettingsDto(
                IsConfigured: false, PublicBookingEnabled: false, BookingDaysAhead: 14,
                MinLeadMinutes: 120, SlotIntervalMinutes: 30, ShowPrices: true,
                AutoConfirmAppointments: false, TimeZoneId: "America/Sao_Paulo",
                DisplayName: null, Description: null, LogoUrl: null, CoverImageUrl: null,
                BrandColor: null, WhatsApp: null, Address: null);

        return Map(settings);
    }

    public async Task<PublicBookingSettingsDto> UpdatePublicBookingAsync(
        UpdatePublicBookingRequest request, CancellationToken ct = default)
    {
        var settings = await _repo.GetForCurrentStoreAsync(ct)
            ?? throw new DomainException("Choose the service vertical (preset) before enabling public booking.");

        settings.UpdatePublicBooking(
            request.PublicBookingEnabled, request.BookingDaysAhead, request.MinLeadMinutes,
            request.SlotIntervalMinutes, request.ShowPrices, request.AutoConfirmAppointments,
            request.TimeZoneId);

        _repo.Update(settings);
        await _repo.SaveChangesAsync(ct);
        return Map(settings);
    }

    public async Task<PublicBookingSettingsDto> UpdatePortalBrandingAsync(
        UpdatePortalBrandingRequest request, CancellationToken ct = default)
    {
        var settings = await _repo.GetForCurrentStoreAsync(ct)
            ?? throw new DomainException("Choose the service vertical (preset) before setting branding.");

        settings.UpdateBranding(
            request.DisplayName, request.Description, request.LogoUrl, request.CoverImageUrl,
            request.BrandColor, request.WhatsApp, request.Address);

        _repo.Update(settings);
        await _repo.SaveChangesAsync(ct);
        return Map(settings);
    }

    private static PublicBookingSettingsDto Map(SvcSettings s) => new(
        IsConfigured:            true,
        PublicBookingEnabled:    s.PublicBookingEnabled,
        BookingDaysAhead:        s.BookingDaysAhead,
        MinLeadMinutes:          s.MinLeadMinutes,
        SlotIntervalMinutes:     s.SlotIntervalMinutes,
        ShowPrices:              s.ShowPrices,
        AutoConfirmAppointments: s.AutoConfirmAppointments,
        TimeZoneId:              s.TimeZoneId,
        DisplayName:             s.DisplayName,
        Description:             s.Description,
        LogoUrl:                 s.LogoUrl,
        CoverImageUrl:           s.CoverImageUrl,
        BrandColor:              s.BrandColor,
        WhatsApp:                s.WhatsApp,
        Address:                 s.Address);
}
