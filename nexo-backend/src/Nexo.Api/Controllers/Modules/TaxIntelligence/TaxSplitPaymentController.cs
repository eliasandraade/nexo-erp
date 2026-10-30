using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>Filters of the split expectation list (query string).</summary>
public sealed class SplitListQuery
{
    public Guid? CalculationId { get; set; }
    public SplitReconciliationStatus? Status { get; set; }
    public TaxType? Tax { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? MinAgeDays { get; set; }
    public int? Skip { get; set; }
    public int? Take { get; set; }
}

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

    /// <summary>Expectations with their reconciliation (computed in the database), filtered and paged. An expectation is not a retention.</summary>
    [HttpGet("split/expectations")]
    public async Task<ActionResult<TaxPage<SplitExpectationDto>>> SplitExpectations([FromQuery] SplitListQuery q, CancellationToken ct)
        => Ok(await _split.ListAsync(new SplitFilter(q.CalculationId, q.Status, q.Tax, q.From, q.To, q.MinAgeDays, q.Skip ?? 0, q.Take ?? 100), ct));

    [HttpGet("split/expectations/export")]
    public async Task<IActionResult> ExportSplit([FromQuery] SplitReconciliationStatus? status, [FromQuery] TaxType? tax, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int? minAgeDays, CancellationToken ct = default)
        => this.Csv(await _split.ExportCsvAsync(new SplitFilter(null, status, tax, from, to, minAgeDays), ct));

    /// <summary>Imports retentions reported by a configured provider. With no provider integrated it refuses — nothing is invented.</summary>
    [HttpPost("split/provider/import")]
    [Authorize(Roles = TaxCapabilities.Decide)]
    public async Task<ActionResult<int>> ImportFromProvider([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _split.ImportFromProviderAsync(from, to, ct));

    /// <summary>Records an actual retention (manual — no provider is integrated) and returns the reconciliation.</summary>
    [HttpPost("split/expectations/{id:guid}/settlements")]
    public async Task<ActionResult<SplitExpectationDto>> RecordSettlement(Guid id, [FromBody] RecordSplitSettlementRequest request, CancellationToken ct)
        => Ok(await _split.RecordSettlementAsync(id, request, ct));

    /// <summary>Cancels a wrong retention with an opposite entry (the original stays on record); then record the right amount.</summary>
    [HttpPost("split/settlements/{id:guid}/reverse")]
    public async Task<ActionResult<SplitExpectationDto>> ReverseSettlement(Guid id, [FromBody] ReverseSplitSettlementRequest request, CancellationToken ct)
        => Ok(await _split.ReverseSettlementAsync(id, request, ct));
}
