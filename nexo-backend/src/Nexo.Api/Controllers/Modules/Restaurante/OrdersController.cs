using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

/// <summary>
/// Gerencia comandas (pedidos) do restaurante.
///
/// Fluxo principal:
///   POST /orders           → abre comanda (mesa → Occupied)
///   POST /orders/{id}/items → adiciona item (snapshot de preço)
///   PATCH /orders/{id}/items/{itemId}/status → fluxo de cozinha
///   DELETE /orders/{id}/items/{itemId}       → cancela item
///   POST /orders/{id}/close → fecha conta (gera Sale em Draft)
///   POST /orders/{id}/pay   → confirma pagamento (StockMovement, RecipeOutput, mesa → Available)
///   POST /orders/{id}/cancel → cancela comanda (mesa → Available)
/// </summary>
[ApiController]
[Route("api/restaurante/orders")]
[Authorize]
[RequireModule("restaurante")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _service;

    public OrdersController(OrderService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>Abre uma comanda para a mesa. Mesa → Occupied.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Open(
        [FromBody] OpenOrderRequest request, CancellationToken ct)
    {
        var dto = await _service.OpenAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Adiciona um item à comanda (snapshot do preço atual do produto).</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<OrderDto>> AddItem(
        Guid id, [FromBody] AddOrderItemRequest request, CancellationToken ct)
        => Ok(await _service.AddItemAsync(id, request, ct));

    /// <summary>
    /// Atualiza status de um item (fluxo de cozinha).
    /// Valores válidos: Preparing | Ready | Delivered
    /// </summary>
    [HttpPatch("{id:guid}/items/{itemId:guid}/status")]
    public async Task<ActionResult<OrderDto>> UpdateItemStatus(
        Guid id, Guid itemId,
        [FromBody] UpdateOrderItemStatusRequest request, CancellationToken ct)
        => Ok(await _service.UpdateItemStatusAsync(id, itemId, request, ct));

    /// <summary>Cancela um item individual da comanda.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<OrderDto>> CancelItem(
        Guid id, Guid itemId, CancellationToken ct)
        => Ok(await _service.CancelItemAsync(id, itemId, ct));

    /// <summary>
    /// Fecha a conta: gera Sale em Draft no CORE.
    /// Mesa permanece Occupied. Retorna saleId para exibir ao cliente.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<CloseOrderResponse>> Close(Guid id, CancellationToken ct)
        => Ok(await _service.CloseAsync(id, ct));

    /// <summary>
    /// Confirma o pagamento. Chama SaleService.ConfirmAsync e faz baixa de ingredientes.
    /// Mesa → Available após sucesso.
    /// Idempotente: segunda chamada retorna 409.
    /// </summary>
    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<OrderDto>> Pay(
        Guid id, [FromBody] PayOrderRequest request, CancellationToken ct)
        => Ok(await _service.PayAsync(id, request, ct));

    /// <summary>Cancela a comanda. Mesa → Available.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _service.CancelAsync(id, ct));
}
