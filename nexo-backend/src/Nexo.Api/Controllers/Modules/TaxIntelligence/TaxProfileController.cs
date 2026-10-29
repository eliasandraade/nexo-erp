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
    private readonly TaxComplianceService _compliance;

    public TaxProfileController(TaxProfileService profiles, TaxComplianceService compliance)
    {
        _profiles = profiles;
        _compliance = compliance;
    }

    /// <summary>Profile versions of the current establishment, the one in force today and a suggestion from the fiscal profile.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<TaxProfileDto>> GetProfile(CancellationToken ct) => Ok(await _profiles.GetAsync(TaxToday.Brazil(), ct));

    /// <summary>Adds a profile version (e.g. a change of regime from a date). Earlier versions never change.</summary>
    [HttpPost("profile/versions")]
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxProfileVersionDto>> AddProfileVersion([FromBody] CreateTaxProfileVersionRequest request, CancellationToken ct)
        => Ok(await _profiles.AddVersionAsync(request, ct));

    /// <summary>Ancillary-obligation assessments of the establishment, by period and scope.</summary>
    [HttpGet("compliance-assessments")]
    public async Task<ActionResult<IReadOnlyList<TaxComplianceAssessmentDto>>> ComplianceAssessments(CancellationToken ct)
        => Ok(await _compliance.ListAsync(ct));

    /// <summary>
    /// Records an assessment for a period and a scope. Only "Verified" can satisfy a rule that depends on
    /// compliance; a declaration, a remediation or PNCT participation never does. Earlier assessments stay.
    /// </summary>
    [HttpPost("compliance-assessments")]
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxComplianceAssessmentDto>> RecordComplianceAssessment(
        [FromBody] RecordComplianceAssessmentRequest request, CancellationToken ct)
        => Ok(await _compliance.RecordAsync(request, DateTime.UtcNow, ct));
}
