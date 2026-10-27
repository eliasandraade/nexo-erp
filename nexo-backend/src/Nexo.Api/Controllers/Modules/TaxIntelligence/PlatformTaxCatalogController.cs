using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Catalogue administration — platform admins only. Versions are never edited; only their status moves.</summary>
[ApiController]
[Route("api/platform/tax")]
[Authorize(Policy = "Platform")]
[Nexo.Api.Filters.ValidateBody]
public class PlatformTaxCatalogController : ControllerBase
{
    private readonly TaxCatalogService _catalog;

    public PlatformTaxCatalogController(TaxCatalogService catalog) => _catalog = catalog;

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TaxRuleDto>>> Rules(CancellationToken ct) => Ok(await _catalog.GetRulesAsync(ct));

    [HttpGet("legal-sources")]
    public async Task<ActionResult<IReadOnlyList<TaxLegalSourceDto>>> LegalSources(CancellationToken ct) => Ok(await _catalog.GetLegalSourcesAsync(ct));

    [HttpPost("legal-sources")]
    public async Task<ActionResult<TaxLegalSourceDto>> CreateLegalSource([FromBody] CreateLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateLegalSourceAsync(request, ct));

    /// <summary>Records that the excerpt was compared with the official text.</summary>
    [HttpPost("legal-sources/{code}/verify")]
    public async Task<ActionResult<TaxLegalSourceDto>> VerifyLegalSource(string code, [FromBody] VerifyLegalSourceRequest request, CancellationToken ct)
        => Ok(await _catalog.VerifyLegalSourceAsync(code, request, ct));

    [HttpPost("rules")]
    public async Task<ActionResult<TaxRuleDto>> CreateRule([FromBody] CreateTaxRuleRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateRuleAsync(request, ct));

    /// <summary>Creates a new version as a draft (content fixed and fingerprinted).</summary>
    [HttpPost("rules/{key}/versions")]
    public async Task<ActionResult<TaxRuleVersionDto>> CreateVersion(string key, [FromBody] CreateTaxRuleVersionRequest request, CancellationToken ct)
        => Ok(await _catalog.CreateVersionAsync(key, request, ct));

    /// <summary>Moves a version's status. Activation requires verified legal sources and supersedes fully covered older versions.</summary>
    [HttpPost("rule-versions/{id:guid}/status")]
    public async Task<ActionResult<TaxRuleVersionDto>> ChangeStatus(Guid id, [FromBody] ChangeTaxRuleVersionStatusRequest request, CancellationToken ct)
        => Ok(await _catalog.ChangeStatusAsync(id, request, ct));
}
