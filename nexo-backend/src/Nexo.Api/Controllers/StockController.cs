using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Stock;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController : ControllerBase
{
    private readonly StockService _service;

    public StockController(StockService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StockItemDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("product/{productId:guid}")]
    public async Task<ActionResult<StockItemDto>> GetByProduct(Guid productId, CancellationToken ct)
        => Ok(await _service.GetByProductIdAsync(productId, ct));

    [HttpGet("product/{productId:guid}/movements")]
    public async Task<ActionResult<IReadOnlyList<StockMovementDto>>> GetMovements(Guid productId, CancellationToken ct)
        => Ok(await _service.GetMovementsAsync(productId, ct));

    [HttpPost("adjust")]
    public async Task<ActionResult<StockItemDto>> Adjust(
        [FromBody] AdjustStockRequest request,
        CancellationToken ct)
        => Ok(await _service.AdjustAsync(request, ct));
}
