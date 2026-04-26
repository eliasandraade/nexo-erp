using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

/// <summary>
/// Hub de deliveries — gerencia o ciclo de vida dos pedidos externos.
///
/// Fluxo:
///   POST   /delivery-orders              → cria pedido (status Received)
///   POST   /delivery-orders/{id}/accept  → aceita → cria RestOrder interno
///   POST   /delivery-orders/{id}/reject  → rejeita
///   PATCH  /delivery-orders/{id}/status  → OutForDelivery | Delivered
///   POST   /delivery-orders/{id}/rider   → atribui entregador
///   POST   /delivery-orders/{id}/cancel  → cancela
///
/// Rastreamento público: GET /api/public/orders/{trackingToken} (sem auth)
/// </summary>
[ApiController]
[Route("api/restaurante/delivery-orders")]
[Authorize]
[RequireModule("restaurante")]
public class DeliveryOrdersController : ControllerBase
{
    private readonly DeliveryOrderService _service;

    public DeliveryOrdersController(DeliveryOrderService service) => _service = service;

    // ── Queries ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeliveryOrderDto>>> GetAll(
        [FromQuery] string[]? status,
        [FromQuery] string[]? channel,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
        => Ok(await _service.GetAllAsync(status, channel, date, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeliveryOrderDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<DeliveryOrderDto>> Create(
        CreateDeliveryOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("manual")]
    public async Task<ActionResult<DeliveryOrderDto>> CreateManual(
        CreateManualOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CreateManualAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    // ── Accept ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<DeliveryOrderDto>> Accept(
        Guid id, AcceptDeliveryOrderRequest request, CancellationToken ct)
        => Ok(await _service.AcceptAsync(id, request, ct));

    // ── Reject ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<DeliveryOrderDto>> Reject(
        Guid id, RejectDeliveryOrderRequest request, CancellationToken ct)
        => Ok(await _service.RejectAsync(id, request, ct));

    // ── Status update ─────────────────────────────────────────────────────────

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<DeliveryOrderDto>> UpdateStatus(
        Guid id, UpdateDeliveryStatusRequest request, CancellationToken ct)
        => Ok(await _service.UpdateStatusAsync(id, request, ct));

    // ── Rider assignment ──────────────────────────────────────────────────────

    [HttpPost("{id:guid}/rider")]
    public async Task<ActionResult<DeliveryOrderDto>> AssignRider(
        Guid id, AssignRiderRequest request, CancellationToken ct)
        => Ok(await _service.AssignRiderAsync(id, request, ct));

    // ── Cancel ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<DeliveryOrderDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _service.CancelAsync(id, ct));

}
