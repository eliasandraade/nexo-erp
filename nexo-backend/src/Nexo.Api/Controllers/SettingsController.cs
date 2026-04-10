using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Settings;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly IValidator<UpdateSettingsRequest> _updateValidator;

    public SettingsController(
        SettingsService settingsService,
        IValidator<UpdateSettingsRequest> updateValidator)
    {
        _settingsService = settingsService;
        _updateValidator = updateValidator;
    }

    /// <summary>Returns application settings for the current tenant.</summary>
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var settings = await _settingsService.GetAsync(ct);
        return Ok(settings);
    }

    /// <summary>Replaces all settings sections. Diretoria and Gerente only.</summary>
    [HttpPut]
    [Authorize(Roles = "Gerente,Diretoria")]
    public async Task<ActionResult<SettingsDto>> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var settings = await _settingsService.UpdateAsync(request, ct);
        return Ok(settings);
    }
}
