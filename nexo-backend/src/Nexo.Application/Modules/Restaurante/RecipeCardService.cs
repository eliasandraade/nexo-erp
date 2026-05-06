using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class RecipeCardService
{
    private readonly IRecipeCardRepository          _recipes;
    private readonly IProductRepository             _products;
    private readonly IFoodServiceSettingsRepository _settings;
    private readonly ICurrentTenant                 _currentTenant;

    public RecipeCardService(
        IRecipeCardRepository          recipes,
        IProductRepository             products,
        IFoodServiceSettingsRepository settings,
        ICurrentTenant                 currentTenant)
    {
        _recipes       = recipes;
        _products      = products;
        _settings      = settings;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<RecipeCardDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var cards  = await _recipes.GetAllAsync(includeInactive, ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        var result = new List<RecipeCardDto>();
        foreach (var card in cards)
            result.Add(await MapAsync(card, config, ct));
        return result;
    }

    public async Task<RecipeCardDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var card   = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
    {
        var card   = await _recipes.GetByProductIdWithIngredientsAsync(productId, ct)
            ?? throw new NotFoundException("RecipeCard for product", productId);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> CreateAsync(CreateRecipeCardRequest request, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        var existing = await _recipes.GetByProductIdAsync(request.ProductId, ct);
        if (existing is not null)
            throw new ConflictException("A recipe card already exists for this product.");

        var card = RestRecipeCard.Create(
            _currentTenant.Id, request.ProductId,
            request.Yield, request.YieldUnit, request.Notes);

        await _recipes.AddAsync(card, ct);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> UpdateAsync(Guid id, UpdateRecipeCardRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);

        if (request.RequiresPackaging && request.PackagingProductId.HasValue)
        {
            var pkg = await _products.GetByIdAsync(request.PackagingProductId.Value, ct)
                ?? throw new NotFoundException("Packaging product", request.PackagingProductId.Value);
            if (!pkg.IsIngredient)
                throw new DomainException("Packaging must reference a product marked as IsIngredient.");
        }

        var steps = request.PrepSteps.Select(s => new PrepStep(s.Order, s.Description, s.DurationMinutes));

        card.Update(
            request.Yield, request.YieldUnit, request.Notes,
            request.HasPrep, steps,
            request.AssemblyNotes, request.RequiresPackaging, request.PackagingProductId,
            imageUrl: null);

        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> AddIngredientAsync(Guid id, AddIngredientRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);

        var ingProduct = await _products.GetByIdAsync(request.IngredientProductId, ct)
            ?? throw new NotFoundException("Ingredient product", request.IngredientProductId);

        if (!ingProduct.IsIngredient)
            throw new DomainException("Only products marked as IsIngredient can be added as recipe ingredients.");

        var ingredient = card.AddIngredient(
            _currentTenant.Id, request.IngredientProductId, request.Quantity, request.Unit);

        _recipes.TrackIngredient(ingredient);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> RemoveIngredientAsync(Guid id, Guid ingredientId, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.RemoveIngredient(ingredientId);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> SetImageAsync(Guid id, string imageUrl, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.SetImageUrl(imageUrl);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    // ── Mapping ──────────────────────────────────────────────────────────────────

    private async Task<RecipeCardDto> MapAsync(RestRecipeCard card, FoodServiceSettings? config, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(card.ProductId, ct);
        var ingredientDtos   = new List<RecipeIngredientDto>();
        decimal totalIngCost = 0m;

        foreach (var ing in card.Ingredients)
        {
            var ingProduct = await _products.GetByIdAsync(ing.IngredientProductId, ct);
            var lineCost   = ing.Quantity * (ingProduct?.CostPrice ?? 0m);
            totalIngCost  += lineCost;

            ingredientDtos.Add(new RecipeIngredientDto(
                ing.Id, ing.IngredientProductId,
                ingProduct?.Name ?? string.Empty,
                ingProduct?.Code ?? string.Empty,
                ing.Quantity, ing.Unit,
                ingProduct?.CostPrice ?? 0m,
                lineCost));
        }

        // TODO (Task 7): replace 0m with config?.CostPerMinuteGas and config?.CostPerMinuteLaborRate
        // once those fields are added to FoodServiceSettings.
        var gasRate   = 0m;
        var laborRate = 0m;
        var prepMin   = (decimal)(card.TotalPrepTimeMin ?? 0);

        var unitIngCost = card.Yield > 0 ? totalIngCost / card.Yield : 0m;
        var gasCost     = prepMin * gasRate;
        var laborCost   = prepMin * laborRate;
        var totalCost   = unitIngCost + gasCost + laborCost;
        var salePrice   = product?.SalePrice ?? 0m;
        var cmvPct      = salePrice > 0 ? (totalCost / salePrice) * 100m : 0m;

        string? packagingName = null;
        if (card.PackagingProductId.HasValue)
        {
            var pkg = await _products.GetByIdAsync(card.PackagingProductId.Value, ct);
            packagingName = pkg?.Name;
        }

        var prepSteps = card.GetPrepSteps()
            .Select(s => new PrepStepDto(s.Order, s.Description, s.DurationMinutes))
            .ToList();

        return new RecipeCardDto(
            card.Id, card.ProductId,
            product?.Name ?? string.Empty,
            product?.Code ?? string.Empty,
            salePrice,
            card.ImageUrl,
            card.Yield, card.YieldUnit,
            card.HasPrep, prepSteps,
            card.TotalPrepTimeMin,
            card.AssemblyNotes,
            card.RequiresPackaging, card.PackagingProductId, packagingName,
            card.IsActive, card.Notes,
            Math.Round(unitIngCost, 4),
            Math.Round(gasCost,     4),
            Math.Round(laborCost,   4),
            Math.Round(totalCost,   4),
            Math.Round(cmvPct,      2),
            ingredientDtos,
            card.CreatedAt);
    }
}
