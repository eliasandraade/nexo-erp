using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Products;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/purchase-prices")]
[Authorize]
public class ProductPurchasePricesController : ControllerBase
{
    private readonly ProductPurchasePriceService _service;
    public ProductPurchasePricesController(ProductPurchasePriceService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PurchasePriceHistoryDto>> GetHistory(
        Guid productId, CancellationToken ct)
        => Ok(await _service.GetHistoryAsync(productId, ct));

    [HttpPost]
    public async Task<ActionResult<PurchasePriceEntryDto>> Add(
        Guid productId, [FromBody] AddPurchasePriceRequest request, CancellationToken ct)
    {
        var dto = await _service.AddAsync(productId, request, ct);
        return CreatedAtAction(nameof(GetHistory), new { productId }, dto);
    }
}
