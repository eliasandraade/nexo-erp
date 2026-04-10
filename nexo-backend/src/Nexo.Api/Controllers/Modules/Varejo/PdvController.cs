using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Varejo;

namespace Nexo.Api.Controllers.Modules.Varejo;

/// <summary>
/// Endpoints do PDV (Ponto de Venda) do módulo Varejo.
/// Resolve preços, busca produtos por código de barras, etc.
/// Requer módulo "varejo" ativo.
/// </summary>
[ApiController]
[Route("api/varejo/pdv")]
[Authorize]
[RequireModule("varejo")]
public class PdvController : ControllerBase
{
    private readonly PriceListService _priceListService;

    public PdvController(PriceListService priceListService)
    {
        _priceListService = priceListService;
    }

    /// <summary>
    /// Resolve o preço de venda de um produto para o PDV.
    ///
    /// Prioridade:
    ///   1. Lista de preço vinculada ao cliente
    ///   2. Lista de preço padrão do tenant
    ///   3. Preço padrão do produto (product.SalePrice)
    ///
    /// O frontend DEVE chamar este endpoint antes de adicionar o item ao carrinho.
    /// Nunca calcule o preço no frontend.
    /// </summary>
    [HttpGet("resolve-price")]
    public async Task<ActionResult<ResolvedPriceDto>> ResolvePrice(
        [FromQuery] Guid productId,
        [FromQuery] Guid? customerId,
        CancellationToken ct)
        => Ok(await _priceListService.ResolvePriceAsync(productId, customerId, ct));
}
