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
///   1. stored SvcSettings.PresetKey (the correct, new path), else
///   2. TEMPORARY legacy fallback — a still-active per-vertical family module key, else
///   3. null (not configured) → the frontend shows onboarding, never an auto-picked preset.
/// </summary>
public class SvcSettingsService
{
    private readonly ISvcSettingsRepository _repo;
    private readonly ICurrentTenant         _currentTenant;
    private readonly ITenantRepository      _tenants;

    public SvcSettingsService(
        ISvcSettingsRepository repo, ICurrentTenant currentTenant, ITenantRepository tenants)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
        _tenants       = tenants;
    }

    public async Task<string?> ResolveEffectivePresetKeyAsync(CancellationToken ct = default)
    {
        if (!_currentTenant.IsResolved) return null;

        var settings = await _repo.GetForCurrentStoreAsync(ct);
        if (settings is not null) return settings.PresetKey;

        // Legacy fallback (temporary): a tenant still holding a per-vertical family key.
        var activeKeys = await _tenants.GetActiveModuleKeysAsync(_currentTenant.Id, ct);
        return ServicePresetRegistry.Resolve(activeKeys)?.Key;
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
                AutoConfirmAppointments: false, TimeZoneId: "America/Sao_Paulo");

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

    private static PublicBookingSettingsDto Map(SvcSettings s) => new(
        IsConfigured:            true,
        PublicBookingEnabled:    s.PublicBookingEnabled,
        BookingDaysAhead:        s.BookingDaysAhead,
        MinLeadMinutes:          s.MinLeadMinutes,
        SlotIntervalMinutes:     s.SlotIntervalMinutes,
        ShowPrices:              s.ShowPrices,
        AutoConfirmAppointments: s.AutoConfirmAppointments,
        TimeZoneId:              s.TimeZoneId);
}
