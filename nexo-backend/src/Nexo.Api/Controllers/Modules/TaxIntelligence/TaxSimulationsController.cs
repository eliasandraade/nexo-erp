using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

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
