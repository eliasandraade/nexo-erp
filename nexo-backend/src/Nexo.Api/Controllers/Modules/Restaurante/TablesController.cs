using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/tables")]
[Authorize]
[RequireModule("restaurante")]
public class TablesController : ControllerBase
{
    private readonly TableService _service;
    private readonly OrderService _orderService;

    public TablesController(TableService service, OrderService orderService)
    {
        _service = service;
        _orderService = orderService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TableDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("by-area/{areaId:guid}")]
    public async Task<ActionResult<IReadOnlyList<TableDto>>> GetByArea(Guid areaId, CancellationToken ct)
        => Ok(await _service.GetByAreaAsync(areaId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TableDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<TableDto>> Create(
        [FromBody] CreateTableRequest request, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TableDto>> Update(
        Guid id, [FromBody] UpdateTableRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    /// <summary>
    /// Atualiza status manualmente: Available | Reserved | Maintenance.
    /// "Occupied" é definido automaticamente ao abrir uma comanda.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TableDto>> UpdateStatus(
        Guid id, [FromBody] UpdateTableStatusRequest request, CancellationToken ct)
        => Ok(await _service.UpdateStatusAsync(id, request, ct));

    [HttpGet("{id:guid}/orders")]
    public async Task<IActionResult> GetOrders(Guid id, CancellationToken ct)
        => Ok(await _orderService.GetByTableIdAsync(id, ct));
}
