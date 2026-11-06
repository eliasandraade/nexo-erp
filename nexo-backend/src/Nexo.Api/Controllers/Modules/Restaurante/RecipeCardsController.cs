using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Controllers.Integrations;
using Nexo.Api.Filters;
using Nexo.Api.Uploads;
using Nexo.Application.Modules.Restaurante;
using Nexo.Api.Authorization;

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
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxImageRequestBytes = MaxImageBytes + UploadAdmission.MultipartOverheadBytes;

    private static readonly Dictionary<string, string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"]  = ".png",
        ["image/webp"] = ".webp",
    };

    private readonly RecipeCardService _service;
    private readonly UploadAdmission _uploads;

    public RecipeCardsController(RecipeCardService service, UploadAdmission uploads)
    {
        _service = service;
        _uploads = uploads;
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<IReadOnlyList<RecipeCardDto>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpGet("product/{productId:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> GetByProduct(Guid productId, CancellationToken ct)
        => Ok(await _service.GetByProductIdAsync(productId, ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> Create(
        [FromBody] CreateRecipeCardRequest request, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> Update(
        Guid id, [FromBody] UpdateRecipeCardRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    /// <summary>Adiciona ou substitui um ingrediente na ficha técnica.</summary>
    [HttpPost("{id:guid}/ingredients")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> AddIngredient(
        Guid id, [FromBody] AddIngredientRequest request, CancellationToken ct)
        => Ok(await _service.AddIngredientAsync(id, request, ct));

    /// <summary>Remove um ingrediente da ficha técnica.</summary>
    [HttpDelete("{id:guid}/ingredients/{ingredientId:guid}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<ActionResult<RecipeCardDto>> RemoveIngredient(
        Guid id, Guid ingredientId, CancellationToken ct)
        => Ok(await _service.RemoveIngredientAsync(id, ingredientId, ct));

    /// <summary>
    /// Uploads or replaces the recipe card's image (multipart field <c>file</c>): up to 5 MB, JPEG, PNG or WebP, checked by its
    /// first bytes. The card is checked before the body is read.
    /// </summary>
    [HttpPost("{id:guid}/image")]
    [Authorize(Roles = AppRoles.Stock)]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(UploadAdmission.RateLimitPolicy)]
    [DisableFormValueModelBinding]
    [MultipartFileBody("file")]
    public async Task<ActionResult<RecipeCardDto>> UploadImage(Guid id, CancellationToken ct)
    {
        await _service.GetByIdAsync(id, ct);   // 404 for a card that does not exist, with nothing read

        const string tooLarge = "A imagem pode ter até 5 MB.";
        var read = await _uploads.ReadAsync(HttpContext, MaxImageRequestBytes, tooLarge, ct);
        if (read.Refusal is { } refusal)
            return refusal.ToResult(HttpContext);

        var file = read.Form!.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Envie a imagem no campo file." });

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!ImageExtensions.TryGetValue(contentType, out var ext))
            return BadRequest(new { error = "Envie uma imagem JPEG, PNG ou WebP." });

        if (file.Length > MaxImageBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = tooLarge, code = UploadAdmission.TooLargeCode });

        if (!await FileSignature.MatchesAsync(file, contentType, ct))
            return BadRequest(new { error = "O conteúdo do arquivo não corresponde a uma imagem válida." });

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
