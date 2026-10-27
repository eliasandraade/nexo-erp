using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>The establishment's versioned tax profile.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxProfileController : ControllerBase
{
    private readonly TaxProfileService _profiles;

    public TaxProfileController(TaxProfileService profiles) => _profiles = profiles;

    /// <summary>Profile versions of the current establishment, the one in force today and a suggestion from the fiscal profile.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<TaxProfileDto>> GetProfile(CancellationToken ct) => Ok(await _profiles.GetAsync(TaxToday.Brazil(), ct));

    /// <summary>Adds a profile version (e.g. a change of regime from a date). Earlier versions never change.</summary>
    [HttpPost("profile/versions")]
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxProfileVersionDto>> AddProfileVersion([FromBody] CreateTaxProfileVersionRequest request, CancellationToken ct)
        => Ok(await _profiles.AddVersionAsync(request, ct));
}
