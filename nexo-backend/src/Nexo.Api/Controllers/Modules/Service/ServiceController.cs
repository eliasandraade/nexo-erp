using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Orken Service — engine bootstrap + per-store configuration.
///
/// The commercial entitlement is the single "service" module (gate, decision D1). The internal
/// preset (vertical "ramo") is chosen per store via /settings and drives which surfaces show
/// (decision D2). /preset returns the resolved preset, or 404 when not configured yet.
/// </summary>
[ApiController]
[Route("api/v1/service")]
[Authorize]
[RequireServiceModule]
public class ServiceController : ControllerBase
{
    private readonly ServicePresetService _presets;
    private readonly SvcSettingsService _settings;
    private readonly IValidator<SetServicePresetRequest> _setPresetValidator;
    private readonly IValidator<UpdatePublicBookingRequest> _publicBookingValidator;

    public ServiceController(
        ServicePresetService presets,
        SvcSettingsService settings,
        IValidator<SetServicePresetRequest> setPresetValidator,
        IValidator<UpdatePublicBookingRequest> publicBookingValidator)
    {
        _presets = presets;
        _settings = settings;
        _setPresetValidator = setPresetValidator;
        _publicBookingValidator = publicBookingValidator;
    }

    /// <summary>
    /// Returns the active Service preset for the current store — display labels and the capability
    /// flags that toggle which engine surfaces (agenda / OS / pacotes / …) are shown. 404 when the
    /// store has not chosen a preset yet (not configured).
    /// </summary>
    [HttpGet("preset")]
    public async Task<ActionResult<ServicePresetDto>> GetPreset(CancellationToken ct)
    {
        var preset = await _presets.GetActivePresetAsync(ct);
        return preset is null ? NotFound() : Ok(preset);
    }

    /// <summary>
    /// Returns whether the active store has chosen a Service preset and which one.
    /// <c>isConfigured: false</c> ⇒ the frontend shows the onboarding (vertical selection).
    /// </summary>
    [HttpGet("settings")]
    public async Task<ActionResult<ServiceSettingsDto>> GetSettings(CancellationToken ct)
        => Ok(await _settings.GetSettingsAsync(ct));

    /// <summary>Sets (or changes) the active store's Service preset. Invalid key → 400.</summary>
    [HttpPut("settings/preset")]
    public async Task<ActionResult<ServiceSettingsDto>> SetPreset(
        [FromBody] SetServicePresetRequest request, CancellationToken ct)
    {
        await _setPresetValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _settings.SetPresetAsync(request.PresetKey, ct));
    }

    /// <summary>Returns the public booking configuration for the active store (defaults when not onboarded).</summary>
    [HttpGet("settings/public-booking")]
    public async Task<ActionResult<PublicBookingSettingsDto>> GetPublicBooking(CancellationToken ct)
        => Ok(await _settings.GetPublicBookingAsync(ct));

    /// <summary>
    /// Updates the public booking configuration. The store must have chosen a preset first
    /// (otherwise 422). The public slug is managed separately via <c>PATCH /api/stores/{id}/public-slug</c>.
    /// </summary>
    [HttpPut("settings/public-booking")]
    public async Task<ActionResult<PublicBookingSettingsDto>> UpdatePublicBooking(
        [FromBody] UpdatePublicBookingRequest request, CancellationToken ct)
    {
        await _publicBookingValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _settings.UpdatePublicBookingAsync(request, ct));
    }
}
