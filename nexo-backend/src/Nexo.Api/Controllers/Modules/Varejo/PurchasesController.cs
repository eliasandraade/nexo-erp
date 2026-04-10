using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Varejo;

namespace Nexo.Api.Controllers.Modules.Varejo;

/// <summary>
/// Gerencia compras (entrada de mercadoria).
/// Requer módulo "varejo" ativo.
/// </summary>
[ApiController]
[Route("api/varejo/purchases")]
[Authorize]
[RequireModule("varejo")]
public class PurchasesController : ControllerBase
{
    private readonly PurchaseService _service;

    public PurchasesController(PurchaseService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PurchaseDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>Cria uma compra em Draft.</summary>
    [HttpPost]
    public async Task<ActionResult<PurchaseDto>> Create(
        [FromBody] CreatePurchaseRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Adiciona um produto à compra (apenas Draft).</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<PurchaseDto>> AddItem(
        Guid id,
        [FromBody] AddPurchaseItemRequest request,
        CancellationToken ct)
        => Ok(await _service.AddItemAsync(id, request, ct));

    /// <summary>Remove um item da compra (apenas Draft).</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<PurchaseDto>> RemoveItem(
        Guid id,
        Guid itemId,
        CancellationToken ct)
        => Ok(await _service.RemoveItemAsync(id, itemId, ct));

    /// <summary>
    /// Confirma a compra: gera StockMovement(PurchaseEntry) e atualiza custo do produto.
    /// Operação atômica — uma transação por compra.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<PurchaseDto>> Confirm(Guid id, CancellationToken ct)
        => Ok(await _service.ConfirmAsync(id, ct));

    /// <summary>
    /// Cancela a compra. Se já confirmada, reverte o estoque automaticamente.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<PurchaseDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _service.CancelAsync(id, ct));
}
