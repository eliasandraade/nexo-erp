using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Resolves the active Service preset (labels + capability flags) for the current store from the
/// chosen internal preset (SvcSettings), with a temporary legacy fallback to a per-vertical
/// module key — both via <see cref="SvcSettingsService.ResolveEffectivePresetKeyAsync"/>.
///
/// Returns null when the store has not chosen a preset yet (not configured) → the controller
/// answers NotFound and the frontend shows onboarding. No preset is ever auto-picked.
/// </summary>
public sealed class ServicePresetService
{
    private readonly SvcSettingsService _settings;

    public ServicePresetService(SvcSettingsService settings) => _settings = settings;

    public async Task<ServicePresetDto?> GetActivePresetAsync(CancellationToken ct = default)
    {
        var key = await _settings.ResolveEffectivePresetKeyAsync(ct);
        if (key is null) return null;

        var preset = ServicePresetRegistry.GetByKey(key);
        return preset is null
            ? null
            : new ServicePresetDto(preset.Key, preset.DisplayName, preset.Labels, preset.Capabilities);
    }
}
