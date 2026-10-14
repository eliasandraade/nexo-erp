using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Varejo;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Modules.Varejo;

/// <summary>
/// Gerencia compras (entrada de mercadoria).
/// Requer módulo "varejo" ativo.
/// </summary>
[ApiController]
[Route("api/varejo/purchases")]
// Compras are Core (stores and restaurants both buy) — no vertical module gate.
[Authorize]
public class PurchasesController : ControllerBase
{
    private readonly PurchaseService _service;

    public PurchasesController(PurchaseService service) => _service = service;

    [HttpGet]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<IReadOnlyList<PurchaseDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PurchaseDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>Cria uma compra em Draft.</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PurchaseDto>> Create(
        [FromBody] CreatePurchaseRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Adiciona um produto à compra (apenas Draft).</summary>
    [HttpPost("{id:guid}/items")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PurchaseDto>> AddItem(
        Guid id,
        [FromBody] AddPurchaseItemRequest request,
        CancellationToken ct)
        => Ok(await _service.AddItemAsync(id, request, ct));

    /// <summary>Remove um item da compra (apenas Draft).</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PurchaseDto>> RemoveItem(
        Guid id,
        Guid itemId,
        CancellationToken ct)
        => Ok(await _service.RemoveItemAsync(id, itemId, ct));

    /// <summary>Envia o pedido ao fornecedor (itens congelados, sem efeito em estoque).</summary>
    [HttpPost("{id:guid}/order")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<PurchaseDto>> Order(Guid id, CancellationToken ct)
        => Ok(await _service.OrderAsync(id, ct));

    /// <summary>
    /// Recebe a compra: entrada no estoque, custo do produto e conta a pagar ao fornecedor —
    /// uma transação. Corpo opcional: vencimento, já pago, data e NF.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<PurchaseDto>> Confirm(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ReceivePurchaseRequest? request,
        CancellationToken ct)
        => Ok(await _service.ConfirmAsync(id, request, ct));

    /// <summary>Cancela a compra; se recebida, estorna estoque e financeiro.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<ActionResult<PurchaseDto>> Cancel(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelPurchaseRequest? request,
        CancellationToken ct)
        => Ok(await _service.CancelAsync(id, request?.Reason, ct));
}
