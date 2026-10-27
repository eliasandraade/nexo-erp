using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Common;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

// Tax Intelligence API. Permissions map onto the existing role sets (docs/security/rbac-matrix.md):
//   tax.read / tax.calculate / tax.simulate / tax.opportunities.read / .review / tax.credits.manage → Management
//   tax.opportunities.approve and new tax profile versions (a change of regime)                    → Directors
//   tax.rules.manage (catalogue and legal sources)                                                  → platform admin only
// Every tenant-scoped query runs under the tenant + store filter, so an id from another tenant is a 404.

internal static class TaxToday
{
    public static DateOnly Brazil() => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));
}

/// <summary>Dashboard, profile, catalogue (read) of Tax Intelligence.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxController : ControllerBase
{
    private readonly TaxOverviewService _overview;
    private readonly TaxProfileService _profiles;
    private readonly TaxCatalogService _catalog;

    public TaxController(TaxOverviewService overview, TaxProfileService profiles, TaxCatalogService catalog)
    {
        _overview = overview;
        _profiles = profiles;
        _catalog = catalog;
    }

    /// <summary>Last 30 days, credits, open opportunities by nature of impact, split divergences, rules in force.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<TaxOverviewDto>> Overview(CancellationToken ct) => Ok(await _overview.GetAsync(TaxToday.Brazil(), ct));

    /// <summary>Profile versions of the current establishment, the one in force today and a suggestion from the fiscal profile.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<TaxProfileDto>> GetProfile(CancellationToken ct) => Ok(await _profiles.GetAsync(TaxToday.Brazil(), ct));

    /// <summary>Adds a profile version (e.g. a change of regime from a date). Earlier versions never change.</summary>
    [HttpPost("profile/versions")]
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxProfileVersionDto>> AddProfileVersion([FromBody] CreateTaxProfileVersionRequest request, CancellationToken ct)
        => Ok(await _profiles.AddVersionAsync(request, ct));

    /// <summary>Rule catalogue with every version, status and legal sources.</summary>
    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TaxRuleDto>>> Rules(CancellationToken ct) => Ok(await _catalog.GetRulesAsync(ct));

    /// <summary>Legal sources with their literal excerpts and verification.</summary>
    [HttpGet("legal-sources")]
    public async Task<ActionResult<IReadOnlyList<TaxLegalSourceDto>>> LegalSources(CancellationToken ct) => Ok(await _catalog.GetLegalSourcesAsync(ct));
}

/// <summary>Official, reproducible calculations.</summary>
[ApiController]
[Route("api/tax/calculations")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxCalculationsController : ControllerBase
{
    private readonly TaxCalculationService _service;
    private readonly TaxSplitPaymentService _split;

    public TaxCalculationsController(TaxCalculationService service, TaxSplitPaymentService split)
    {
        _service = service;
        _split = split;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaxCalculationSummaryDto>>> List([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _service.ListAsync(take, ct));

    /// <summary>Calculates an operation described by its facts (a proposal, a manual entry). Same facts + same rules = same calculation.</summary>
    [HttpPost]
    public async Task<ActionResult<TaxCalculationDto>> Create([FromBody] TaxOperationRequest request, CancellationToken ct)
        => Ok(await _service.CalculateAsync(request, ct));

    /// <summary>Calculates a confirmed sale of the current store (read-only on the sale).</summary>
    [HttpPost("from-sale/{saleId:guid}")]
    public async Task<ActionResult<TaxCalculationDto>> FromSale(Guid saleId, CancellationToken ct)
        => Ok(await _service.CalculateSaleAsync(saleId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaxCalculationDto>> Get(Guid id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

    /// <summary>Runs the stored input again with the rule versions it used and compares the output hash. Writes nothing.</summary>
    [HttpPost("{id:guid}/verify")]
    public async Task<ActionResult<TaxCalculationVerificationDto>> Verify(Guid id, CancellationToken ct) => Ok(await _service.VerifyAsync(id, ct));

    /// <summary>Runs the opportunity detectors again. They only propose; nothing is applied.</summary>
    [HttpPost("{id:guid}/opportunities/detect")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> Detect(Guid id, CancellationToken ct) => Ok(await _service.DetectAgainAsync(id, ct));

    /// <summary>Expected split payment retentions of an outbound calculation (one per tax).</summary>
    [HttpPost("{id:guid}/split-expectations")]
    public async Task<ActionResult<IReadOnlyList<SplitExpectationDto>>> CreateSplitExpectations(Guid id, [FromBody] CreateSplitExpectationsRequest request, CancellationToken ct)
        => Ok(await _split.CreateExpectationsAsync(id, request, ct));
}

/// <summary>What-if simulations — they never touch official data.</summary>
[ApiController]
[Route("api/tax/simulations")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxSimulationsController : ControllerBase
{
    private readonly TaxSimulationService _service;

    public TaxSimulationsController(TaxSimulationService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaxSimulationSummaryDto>>> List([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _service.ListAsync(take, ct));

    [HttpPost]
    public async Task<ActionResult<TaxSimulationDto>> Create([FromBody] CreateTaxSimulationRequest request, CancellationToken ct)
        => Ok(await _service.CreateAsync(request, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaxSimulationDto>> Get(Guid id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

    /// <summary>Effective cost of buying from each supplier (price − estimated credit). Nothing is stored.</summary>
    [HttpPost("supplier-comparison")]
    public async Task<ActionResult<IReadOnlyList<SupplierOfferResultDto>>> CompareSuppliers([FromBody] CompareSuppliersRequest request, CancellationToken ct)
        => Ok(await _service.CompareSuppliersAsync(request, ct));
}

/// <summary>Opportunity review workflow and decision dossiers.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxOpportunitiesController : ControllerBase
{
    private readonly TaxOpportunityService _service;

    public TaxOpportunitiesController(TaxOpportunityService service) => _service = service;

    [HttpGet("opportunities")]
    public async Task<ActionResult<IReadOnlyList<TaxOpportunitySummaryDto>>> List(
        [FromQuery] TaxOpportunityStatus? status, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _service.ListAsync(status, take, ct));

    [HttpGet("opportunities/{id:guid}")]
    public async Task<ActionResult<TaxOpportunityDto>> Get(Guid id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

    [HttpPost("opportunities/{id:guid}/review")]
    public async Task<ActionResult<TaxOpportunityDto>> StartReview(Guid id, CancellationToken ct) => Ok(await _service.StartReviewAsync(id, ct));

    /// <summary>Answers a requirement. Some "not met" answers block the opportunity as a prohibited structure.</summary>
    [HttpPost("opportunities/{id:guid}/requirements/{requirementId:guid}")]
    public async Task<ActionResult<TaxOpportunityDto>> AnswerRequirement(Guid id, Guid requirementId, [FromBody] AnswerRequirementRequest request, CancellationToken ct)
        => Ok(await _service.AnswerRequirementAsync(id, requirementId, request, ct));

    [HttpPost("opportunities/{id:guid}/evidence")]
    public async Task<ActionResult<TaxOpportunityDto>> AddEvidence(Guid id, [FromBody] AddEvidenceRequest request, CancellationToken ct)
        => Ok(await _service.AddEvidenceAsync(id, request, ct));

    [HttpPost("opportunities/{id:guid}/block")]
    public async Task<ActionResult<TaxOpportunityDto>> Block(Guid id, [FromBody] BlockOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.BlockAsync(id, request, ct));

    [HttpPost("opportunities/{id:guid}/reject")]
    public async Task<ActionResult<TaxOpportunityDto>> Reject(Guid id, [FromBody] RejectOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.RejectAsync(id, request, ct));

    /// <summary>Approves and records the decision dossier. Diretoria only; high risk must be acknowledged.</summary>
    [HttpPost("opportunities/{id:guid}/approve")]
    [Authorize(Roles = AppRoles.Directors)]
    public async Task<ActionResult<TaxOpportunityDto>> Approve(Guid id, [FromBody] ApproveOpportunityRequest request, CancellationToken ct)
        => Ok(await _service.ApproveAsync(id, request, ct));

    [HttpGet("decisions/{id:guid}")]
    public async Task<ActionResult<TaxDecisionDto>> Decision(Guid id, CancellationToken ct) => Ok(await _service.GetDecisionAsync(id, ct));
}

/// <summary>Credit Hunter ledger and split payment reconciliation.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxCreditsController : ControllerBase
{
    private readonly TaxCreditService _credits;
    private readonly TaxSplitPaymentService _split;

    public TaxCreditsController(TaxCreditService credits, TaxSplitPaymentService split)
    {
        _credits = credits;
        _split = split;
    }

    [HttpGet("credits")]
    public async Task<ActionResult<IReadOnlyList<TaxCreditDto>>> List([FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _credits.ListAsync(take, ct));

    [HttpGet("credits/totals")]
    public async Task<ActionResult<IReadOnlyList<TaxCreditTotalsDto>>> Totals(CancellationToken ct) => Ok(await _credits.TotalsAsync(ct));

    [HttpGet("credits/{id:guid}")]
    public async Task<ActionResult<TaxCreditDto>> Get(Guid id, CancellationToken ct) => Ok(await _credits.GetAsync(id, ct));

    [HttpPost("credits")]
    public async Task<ActionResult<TaxCreditDto>> Register([FromBody] RegisterTaxCreditRequest request, CancellationToken ct)
        => Ok(await _credits.RegisterAsync(request, ct));

    /// <summary>Appends a movement (appropriate, use, block, unblock, reverse, expire). Balances are sums of the entries.</summary>
    [HttpPost("credits/{id:guid}/movements")]
    public async Task<ActionResult<TaxCreditDto>> Move(Guid id, [FromBody] TaxCreditMovementRequest request, CancellationToken ct)
        => Ok(await _credits.MoveAsync(id, request, TaxToday.Brazil(), ct));

    [HttpGet("split/provider")]
    public ActionResult<SplitProviderStatusDto> SplitProvider() => Ok(_split.ProviderStatus());

    [HttpGet("split/expectations")]
    public async Task<ActionResult<IReadOnlyList<SplitExpectationDto>>> SplitExpectations(
        [FromQuery] Guid? calculationId, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _split.ListAsync(calculationId, take, ct));

    /// <summary>Records an actual retention (manual — no provider is integrated) and returns the reconciliation.</summary>
    [HttpPost("split/expectations/{id:guid}/settlements")]
    public async Task<ActionResult<SplitExpectationDto>> RecordSettlement(Guid id, [FromBody] RecordSplitSettlementRequest request, CancellationToken ct)
        => Ok(await _split.RecordSettlementAsync(id, request, ct));

    /// <summary>Cancels a wrong retention with an opposite entry (the original stays on record); then record the right amount.</summary>
    [HttpPost("split/settlements/{id:guid}/reverse")]
    public async Task<ActionResult<SplitExpectationDto>> ReverseSettlement(Guid id, [FromBody] ReverseSplitSettlementRequest request, CancellationToken ct)
        => Ok(await _split.ReverseSettlementAsync(id, request, ct));
}

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
