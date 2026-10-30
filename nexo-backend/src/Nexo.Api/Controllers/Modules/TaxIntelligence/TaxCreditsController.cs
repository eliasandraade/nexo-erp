using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Credit Hunter ledger.</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxCreditsController : ControllerBase
{
    private readonly TaxCreditService _credits;

    public TaxCreditsController(TaxCreditService credits) => _credits = credits;

    [HttpGet("credits")]
    public async Task<ActionResult<TaxPage<TaxCreditDto>>> List([FromQuery] CreditTax? tax, [FromQuery] TaxCreditStage? stage,
        [FromQuery] bool? expiring, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _credits.ListAsync(tax, stage, expiring, skip, take, TaxToday.Brazil(), ct));

    [HttpGet("credits/totals")]
    public async Task<ActionResult<IReadOnlyList<TaxCreditTotalsDto>>> Totals(CancellationToken ct) => Ok(await _credits.TotalsAsync(ct));

    [HttpGet("credits/{id:guid}")]
    public async Task<ActionResult<TaxCreditDto>> Get(Guid id, CancellationToken ct) => Ok(await _credits.GetAsync(id, ct));

    [HttpPost("credits")]
    public async Task<ActionResult<TaxCreditDto>> Register([FromBody] RegisterTaxCreditRequest request, CancellationToken ct)
        => Ok(await _credits.RegisterAsync(request, ct));

    /// <summary>
    /// Appends a movement. Reviewing a potential credit (open the review, discard) is management work; anything that writes the ledger
    /// (confirm, appropriate, use, block, unblock, reverse, expire) is the board's. Balances are sums of the entries; a client key makes a
    /// retried request idempotent.
    /// </summary>
    [HttpPost("credits/{id:guid}/movements")]
    public async Task<ActionResult<TaxCreditDto>> Move(Guid id, [FromBody] TaxCreditMovementRequest request, CancellationToken ct)
    {
        if (request.Movement is not (TaxCreditMovement.StartRequirementsReview or TaxCreditMovement.Discard) && !User.IsInRole(AppRoles.Directors))
            return Forbid();
        return Ok(await _credits.MoveAsync(id, request, TaxToday.Brazil(), ct));
    }
}
