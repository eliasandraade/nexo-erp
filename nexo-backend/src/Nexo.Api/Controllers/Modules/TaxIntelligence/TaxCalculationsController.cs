using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

/// <summary>
/// System calculations with the rules and the data indicated — reproducible, and refused (never zero)
/// when the operation is outside what engine V1 supports.
/// </summary>
[ApiController]
[Route("api/tax/calculations")]
[Authorize(Roles = AppRoles.Management)]
[Nexo.Api.Filters.ValidateBody]
public class TaxCalculationsController : ControllerBase
{
    private readonly TaxCalculationService _service;

    public TaxCalculationsController(TaxCalculationService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaxCalculationSummaryDto>>> List([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _service.ListAsync(take, ct));

    /// <summary>
    /// Whether engine V1 supports the operation (supported-operation matrix), without calculating or
    /// recording anything.
    /// </summary>
    [HttpPost("coverage")]
    public async Task<ActionResult<TaxCoverageDecision>> Coverage([FromBody] TaxOperationRequest request, CancellationToken ct)
        => Ok(await _service.CoverageAsync(request, ct));

    /// <summary>
    /// Calculates an operation described by its facts (a proposal, a manual entry). Same facts + same rules =
    /// same calculation. Outside the V1 recorte: 422 tax_operation_not_supported with the reasons.
    /// </summary>
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
}
