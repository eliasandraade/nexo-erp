using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Varejo;

namespace Nexo.Api.Controllers.Modules.Varejo;

/// <summary>
/// Gerencia listas de preço e resolução de preço no PDV.
/// Requer módulo "varejo" ativo.
/// </summary>
[ApiController]
[Route("api/varejo/price-lists")]
[Authorize]
[RequireModule("varejo")]
public class PriceListsController : ControllerBase
{
    private readonly PriceListService _service;

    public PriceListsController(PriceListService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PriceListDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PriceListDetailDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<PriceListDto>> Create(
        [FromBody] CreatePriceListRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PriceListDto>> Update(
        Guid id,
        [FromBody] UpdatePriceListRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    /// <summary>Define esta lista como a lista padrão do tenant.</summary>
    [HttpPost("{id:guid}/set-default")]
    public async Task<ActionResult<PriceListDto>> SetDefault(Guid id, CancellationToken ct)
        => Ok(await _service.SetAsDefaultAsync(id, ct));

    /// <summary>Define ou atualiza o preço de um produto nesta lista.</summary>
    [HttpPut("{id:guid}/products")]
    public async Task<ActionResult<PriceListDetailDto>> SetProductPrice(
        Guid id,
        [FromBody] SetProductPriceRequest request,
        CancellationToken ct)
        => Ok(await _service.SetProductPriceAsync(id, request, ct));

    /// <summary>Remove o preço de um produto desta lista.</summary>
    [HttpDelete("{id:guid}/products/{productId:guid}")]
    public async Task<ActionResult<PriceListDetailDto>> RemoveProductPrice(
        Guid id,
        Guid productId,
        CancellationToken ct)
        => Ok(await _service.RemoveProductPriceAsync(id, productId, ct));
}
