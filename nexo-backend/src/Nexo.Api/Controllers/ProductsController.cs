using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common;
using Nexo.Application.Features.Products;

namespace Nexo.Api.Controllers;

public record SetProductImageRequest(string? ImageUrl);

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly ProductService _service;

    public ProductsController(ProductService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool? isIngredient = null,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, isIngredient, ct));

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool? isIngredient = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? unit = null,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return Ok(await _service.GetPagedAsync(
            page, pageSize, search, includeInactive, isIngredient, categoryId, unit, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    [HttpPatch("{id:guid}/prices")]
    public async Task<ActionResult<ProductDto>> UpdatePrices(
        Guid id,
        [FromBody] UpdateProductPricesRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdatePricesAsync(id, request, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await _service.ActivateAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _service.DeactivateAsync(id, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/image")]
    public async Task<ActionResult<ProductDto>> SetImage(
        Guid id,
        [FromBody] SetProductImageRequest request,
        CancellationToken ct)
        => Ok(await _service.SetImageUrlAsync(id, request.ImageUrl, ct));
}
