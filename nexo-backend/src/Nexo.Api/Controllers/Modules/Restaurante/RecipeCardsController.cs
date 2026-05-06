using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

/// <summary>
/// Gerencia fichas técnicas de produtos.
/// CMV e custo calculados automaticamente pelo backend com base nos custos atuais dos ingredientes.
/// </summary>
[ApiController]
[Route("api/restaurante/recipe-cards")]
[Authorize]
[RequireModule("restaurante")]
public class RecipeCardsController : ControllerBase
{
    private readonly RecipeCardService _service;

    public RecipeCardsController(RecipeCardService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeCardDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RecipeCardDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpGet("product/{productId:guid}")]
    public async Task<ActionResult<RecipeCardDto>> GetByProduct(Guid productId, CancellationToken ct)
        => Ok(await _service.GetByProductIdAsync(productId, ct));

    [HttpPost]
    public async Task<ActionResult<RecipeCardDto>> Create(
        [FromBody] CreateRecipeCardRequest request, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RecipeCardDto>> Update(
        Guid id, [FromBody] UpdateRecipeCardRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    /// <summary>Adiciona ou substitui um ingrediente na ficha técnica.</summary>
    [HttpPost("{id:guid}/ingredients")]
    public async Task<ActionResult<RecipeCardDto>> AddIngredient(
        Guid id, [FromBody] AddIngredientRequest request, CancellationToken ct)
        => Ok(await _service.AddIngredientAsync(id, request, ct));

    /// <summary>Remove um ingrediente da ficha técnica.</summary>
    [HttpDelete("{id:guid}/ingredients/{ingredientId:guid}")]
    public async Task<ActionResult<RecipeCardDto>> RemoveIngredient(
        Guid id, Guid ingredientId, CancellationToken ct)
        => Ok(await _service.RemoveIngredientAsync(id, ingredientId, ct));

    /// <summary>Upload ou substituição da imagem da ficha técnica. Máximo 5 MB; JPEG, PNG ou WebP.</summary>
    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<RecipeCardDto>> UploadImage(
        Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType))
            return BadRequest("Only JPEG, PNG, and WebP images are accepted.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("Image must be smaller than 5 MB.");

        var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{id:N}{ext}";
        var wwwroot  = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "recipes");
        Directory.CreateDirectory(wwwroot);
        var fullPath = Path.Combine(wwwroot, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, ct);

        var imageUrl = $"/images/recipes/{fileName}";
        var dto      = await _service.SetImageAsync(id, imageUrl, ct);
        return Ok(dto);
    }
}
