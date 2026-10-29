using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Split payment expectations and reconciliation (no provider integrated).</summary>
[ApiController]
[Route("api/tax")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxSplitPaymentController : ControllerBase
{
    private readonly TaxSplitPaymentService _split;

    public TaxSplitPaymentController(TaxSplitPaymentService split) => _split = split;

    /// <summary>Expected split payment retentions of an outbound calculation (one per tax).</summary>
    [HttpPost("calculations/{id:guid}/split-expectations")]
    public async Task<ActionResult<IReadOnlyList<SplitExpectationDto>>> CreateSplitExpectations(Guid id, [FromBody] CreateSplitExpectationsRequest request, CancellationToken ct)
        => Ok(await _split.CreateExpectationsAsync(id, request, ct));

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
