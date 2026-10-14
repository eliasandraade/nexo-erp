using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Application.Features.Stock;

namespace Nexo.Api.Controllers;

/// <summary>Inventário (contagem física) and transferências between the tenant's stores.</summary>
[ApiController]
[Route("api/stock")]
[Authorize(Roles = AppRoles.Stock)]
public class StockOperationsController : ControllerBase
{
    private readonly StockOperationsService _service;

    public StockOperationsController(StockOperationsService service) => _service = service;

    [HttpGet("counts")]
    public async Task<ActionResult<IReadOnlyList<InventoryCountDto>>> ListCounts(CancellationToken ct)
        => Ok(await _service.ListCountsAsync(ct));

    [HttpPost("counts")]
    public async Task<ActionResult<InventoryCountDto>> OpenCount([FromBody] CreateInventoryCountRequest request, CancellationToken ct)
    {
        var count = await _service.OpenCountAsync(request, ct);
        return CreatedAtAction(nameof(GetCount), new { id = count.Id }, count);
    }

    [HttpGet("counts/{id:guid}")]
    public async Task<ActionResult<InventoryCountDto>> GetCount(Guid id, CancellationToken ct)
        => Ok(await _service.GetCountAsync(id, ct));

    [HttpPut("counts/{id:guid}")]
    public async Task<ActionResult<InventoryCountDto>> UpdateCount(Guid id, [FromBody] UpdateInventoryCountRequest request, CancellationToken ct)
        => Ok(await _service.UpdateCountAsync(id, request, ct));

    [HttpPost("counts/{id:guid}/apply")]
    public async Task<ActionResult<InventoryCountDto>> ApplyCount(Guid id, CancellationToken ct)
        => Ok(await _service.ApplyCountAsync(id, ct));

    [HttpPost("counts/{id:guid}/cancel")]
    public async Task<ActionResult<InventoryCountDto>> CancelCount(Guid id, CancellationToken ct)
        => Ok(await _service.CancelCountAsync(id, ct));

    [HttpGet("transfers")]
    public async Task<ActionResult<IReadOnlyList<StockTransferDto>>> ListTransfers(CancellationToken ct)
        => Ok(await _service.ListTransfersAsync(ct));

    [HttpPost("transfers")]
    public async Task<ActionResult<StockTransferDto>> Transfer([FromBody] CreateStockTransferRequest request, CancellationToken ct)
        => Ok(await _service.TransferAsync(request, ct));
}
