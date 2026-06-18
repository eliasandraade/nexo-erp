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

    /// <summary>App-path factory: StoreId is auto-injected on INSERT by the interceptor.</summary>
    public static SvcSettings Create(Guid tenantId, string presetKey)
    {
        var key = Normalize(presetKey);
        EnsureValidPreset(key);
        return new SvcSettings(tenantId) { PresetKey = key };
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
