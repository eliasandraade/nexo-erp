namespace Nexo.Application.Modules.Service;

/// <summary>
/// Wire shape of the per-store Service configuration. <see cref="IsConfigured"/> is false when
/// the store has not chosen a vertical yet — the frontend shows onboarding in that case.
/// </summary>
public sealed record ServiceSettingsDto(bool IsConfigured, string? PresetKey);

/// <summary>Sets the active internal preset (the vertical "ramo") for the current store.</summary>
public sealed record SetServicePresetRequest(string PresetKey);
